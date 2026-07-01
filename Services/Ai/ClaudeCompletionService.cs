using Anthropic;
using Anthropic.Models.Messages;
using app_tramites.Models.ModelAi;
using app_tramites.Services.Ai.Tools;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace app_tramites.Services.Ai;

/// <summary>
/// Implementación de <see cref="IAiCompletionService"/> para Anthropic Claude
/// usando el SDK oficial <c>Anthropic</c> (NuGet, v10+).
/// </summary>
/// <remarks>
/// <para>
/// Resolución de la API key en orden de prioridad:
/// <list type="number">
///   <item><description>
///     <see cref="OPAIConfiguration.SecretRef"/> → Azure Key Vault
///     (si el vault está configurado en el ambiente). Pendiente integración B-serie;
///     por ahora se omite y se cae al siguiente.
///   </description></item>
///   <item><description>
///     Variable de entorno <c>ANTHROPIC_API_KEY</c> (estándar del SDK; el
///     <see cref="AnthropicClient"/> la resuelve automáticamente cuando ApiKey es vacío/null).
///   </description></item>
///   <item><description>
///     <see cref="OPAIConfiguration.ApiKey"/> — solo fallback para entornos de prueba
///     (db-nexus-test). NUNCA hardcodear; vendrá del appsettings de dev, nunca en código.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// El campo <see cref="OPAIConfiguration.ApiKey"/> que llega de la BD en producción
/// debe estar vacío o apuntar a una referencia de Key Vault, no a un valor en claro.
/// </para>
/// </remarks>
public sealed class ClaudeCompletionService : IAiCompletionService
{
    private readonly OPAIConfiguration _config;
    private readonly ILogger<ClaudeCompletionService> _logger;

    // REQ-019: cachear el AnthropicClient para evitar socket-churn.
    // La clave de caché distingue el origen de la API key (env vs BD) para no mezclar credenciales.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, AnthropicClient>
        _clientCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Crea el servicio con la configuración del agente.
    /// </summary>
    /// <param name="config">Configuración del modelo (ApiKey / SecretRef).</param>
    /// <param name="logger">Logger para advertencias de schema de tools.</param>
    public ClaudeCompletionService(OPAIConfiguration config, ILogger<ClaudeCompletionService> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<AiCompletionResult> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = BuildClient();

        var parameters = new MessageCreateParams
        {
            // claude-opus-4-8 — se pasa como string porque el enum del SDK v10.4.0
            // no incluye aún este identificador (Model.ClaudeOpus4_8 no existe en esa versión).
            Model     = "claude-opus-4-8",
            MaxTokens = request.MaxTokens,
            System    = request.SystemPrompt,
            Messages  =
            [
                new() { Role = Role.User, Content = request.UserMessage }
            ]
        };

        var message = await client.Messages.Create(parameters, cancellationToken);

        // Extraer texto: el primer bloque de tipo text
        var text = message.Content
            .OfType<TextBlock>()
            .Select(b => b.Text)
            .FirstOrDefault() ?? string.Empty;

        // Tokens estándar — InputTokens/OutputTokens son long en el SDK; cast explícito a int.
        int promptTokens     = (int)(message.Usage?.InputTokens  ?? 0L);
        int completionTokens = (int)(message.Usage?.OutputTokens ?? 0L);

        // Tokens adicionales de Claude (cache / thinking) — nullable para compatibilidad
        int? thinkingTokens      = null;
        int? cacheReadTokens     = null;
        int? cacheCreationTokens = null;

        // El SDK v10+ expone propiedades de caché en Usage cuando se activó prompt caching.
        // Se leen con reflexión defensiva: los tipos pueden ser long? o int? según la versión
        // del SDK; Convert.ToInt32 maneja ambos sin lanzar para valores nulos.
        if (message.Usage is { } usage)
        {
            var usageType = usage.GetType();

            var cacheReadProp = usageType.GetProperty("CacheReadInputTokens");
            if (cacheReadProp != null)
            {
                var val = cacheReadProp.GetValue(usage);
                if (val != null) cacheReadTokens = Convert.ToInt32(val);
            }

            var cacheCreationProp = usageType.GetProperty("CacheCreationInputTokens");
            if (cacheCreationProp != null)
            {
                var val = cacheCreationProp.GetValue(usage);
                if (val != null) cacheCreationTokens = Convert.ToInt32(val);
            }
        }

        return new AiCompletionResult
        {
            Text                = text.Trim(),
            PromptTokens        = promptTokens,
            CompletionTokens    = completionTokens,
            ThinkingTokens      = thinkingTokens,
            CacheReadTokens     = cacheReadTokens,
            CacheCreationTokens = cacheCreationTokens
        };
    }

    // ── Streaming (REQ-019 T7) ────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// Mapeo de chunks del SDK Anthropic v10.4.0:
    /// <list type="bullet">
    ///   <item>
    ///     <c>RawMessageStreamEvent.TryPickContentBlockDelta</c> devuelve un
    ///     <see cref="RawContentBlockDeltaEvent"/> cuyo campo <c>Delta</c>
    ///     (<see cref="RawContentBlockDelta"/>) admite <c>TryPickText</c>
    ///     (<see cref="TextDelta"/>) y <c>TryPickThinking</c> (<see cref="ThinkingDelta"/>).
    ///   </item>
    ///   <item>
    ///     <c>RawMessageStreamEvent.TryPickDelta</c> devuelve un
    ///     <see cref="RawMessageDeltaEvent"/> con <c>Usage</c>
    ///     (<see cref="MessageDeltaUsage"/>): contiene <c>OutputTokens</c> (long)
    ///     e <c>InputTokens</c> (long?).
    ///   </item>
    /// </list>
    /// El chunk <see cref="AiStreamChunkType.Done"/> se emite tras el <c>message_stop</c>
    /// o al agotar el <c>await foreach</c>, usando los últimos tokens capturados.
    /// </remarks>
    public async IAsyncEnumerable<AiStreamChunk> StreamAsync(
        AiCompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = BuildClient();

        var parameters = new MessageCreateParams
        {
            Model     = "claude-opus-4-8",
            MaxTokens = request.MaxTokens,
            System    = request.SystemPrompt,
            Messages  =
            [
                new() { Role = Role.User, Content = request.UserMessage }
            ]
        };

        // Acumuladores de tokens para el chunk Done
        long outputTokens = 0;
        long inputTokens  = 0;

        await foreach (var ev in client.Messages.CreateStreaming(parameters, ct).ConfigureAwait(false))
        {
            // ── content_block_delta: text o thinking ─────────────────────────
            if (ev.TryPickContentBlockDelta(out var blockDeltaEvent))
            {
                var delta = blockDeltaEvent.Delta;

                if (delta.TryPickText(out var textDelta) && !string.IsNullOrEmpty(textDelta.Text))
                {
                    yield return AiStreamChunk.TextChunk(textDelta.Text);
                }
                else if (delta.TryPickThinking(out var thinkingDelta) && !string.IsNullOrEmpty(thinkingDelta.Thinking))
                {
                    yield return AiStreamChunk.ThinkingChunk(thinkingDelta.Thinking);
                }
            }

            // ── message_delta: tokens de salida ──────────────────────────────
            else if (ev.TryPickDelta(out var msgDelta) && msgDelta.Usage is { } usage)
            {
                outputTokens = usage.OutputTokens;
                if (usage.InputTokens.HasValue)
                    inputTokens = usage.InputTokens.Value;
            }
        }

        yield return AiStreamChunk.DoneChunk(
            promptTokens:     (int)inputTokens,
            completionTokens: (int)outputTokens);
    }

    // ── Tool-use loop (REQ-019 T5) ────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// Tipos del SDK Anthropic v10.4.0 verificados por compilación:
    /// <list type="bullet">
    ///   <item><see cref="Tool"/> — definición de tool para <c>MessageCreateParams.Tools</c>.</item>
    ///   <item><see cref="ToolUnion"/> — union type; <c>Tool</c> convierte implícitamente.</item>
    ///   <item><see cref="ToolUseBlock"/> — bloque de respuesta con <c>ID</c>, <c>Name</c>, <c>Input</c>.</item>
    ///   <item><see cref="ToolResultBlockParam"/> — resultado enviado de vuelta, con <c>ToolUseID</c> y <c>Content</c>.</item>
    ///   <item><see cref="StopReason.ToolUse"/> — <c>Message.StopReason</c> cuando el modelo quiere invocar tools.</item>
    ///   <item><see cref="ContentBlockParam"/> — se construye implícitamente desde <see cref="ToolResultBlockParam"/>.</item>
    ///   <item><see cref="MessageParamContent"/> — acepta <c>List&lt;ContentBlockParam&gt;</c> implícitamente.</item>
    /// </list>
    /// </remarks>
    public async Task<AiCompletionResult> CompleteWithToolsAsync(
        AiCompletionRequest request,
        ToolsContext toolsContext,
        IToolExecutor toolExecutor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(toolsContext);
        ArgumentNullException.ThrowIfNull(toolExecutor);

        var client = BuildClient();

        // Mapear OPAITool → Tool del SDK Anthropic
        var sdkTools = MapToSdkTools(toolsContext.AvailableTools);

        // Construir el historial de mensajes (empieza con el user message)
        var messages = new List<MessageParam>
        {
            new() { Role = Role.User, Content = request.UserMessage }
        };

        // Acumuladores de tokens (se suman en cada vuelta del loop)
        int totalPromptTokens     = 0;
        int totalCompletionTokens = 0;
        string finalText          = string.Empty;

        // Tool-use loop: continúa hasta end_turn o max iteraciones de seguridad
        const int maxLoopIterations = 10;
        for (int iteration = 0; iteration < maxLoopIterations; iteration++)
        {
            var parameters = new MessageCreateParams
            {
                Model     = "claude-opus-4-8",
                MaxTokens = request.MaxTokens,
                System    = request.SystemPrompt,
                Messages  = messages,
                Tools     = sdkTools
            };

            var message = await client.Messages.Create(parameters, ct).ConfigureAwait(false);

            // Acumular tokens
            totalPromptTokens     += (int)(message.Usage?.InputTokens  ?? 0L);
            totalCompletionTokens += (int)(message.Usage?.OutputTokens ?? 0L);

            // Extraer texto si hay bloques de texto en esta vuelta
            // Nota: ContentBlock es un union type; usar TryPickText() (no OfType<TextBlock>).
            foreach (var block in message.Content)
            {
                if (block.TryPickText(out var tb) && !string.IsNullOrEmpty(tb.Text))
                {
                    finalText = tb.Text;
                    break;
                }
            }

            // Si el modelo terminó (end_turn o sin tool_use), salir del loop
            // Value() es un método (no propiedad) en ApiEnum<string, StopReason>.
            if (!(message.StopReason.HasValue && message.StopReason.Value.Value() == StopReason.ToolUse))
                break;

            // Hay bloques tool_use: recopilar todos usando TryPick (patrón union type SDK)
            var toolUseBlocksFound = new List<ToolUseBlock>();
            var assistantContentBlocks = new List<ContentBlockParam>();
            foreach (var block in message.Content)
            {
                if (block.TryPickText(out var tb))
                    assistantContentBlocks.Add((TextBlockParam)new TextBlockParam { Text = tb.Text });
                else if (block.TryPickToolUse(out var tub))
                {
                    assistantContentBlocks.Add((ToolUseBlockParam)new ToolUseBlockParam
                    {
                        ID    = tub.ID,
                        Name  = tub.Name,
                        Input = tub.Input
                    });
                    toolUseBlocksFound.Add(tub);
                }
            }
            messages.Add(new MessageParam
            {
                Role    = Role.Assistant,
                Content = assistantContentBlocks
            });

            // Ejecutar cada tool_use y recopilar los tool_result
            var toolResultBlocks = new List<ContentBlockParam>();
            foreach (var block in toolUseBlocksFound)
            {
                // Emitir evento SSE: tool_use (chip en el chat)
                if (toolsContext.StreamEventCallback != null)
                {
                    await toolsContext.StreamEventCallback(new ToolStreamEvent
                    {
                        EventType    = "tool_use",
                        ToolName     = block.Name,
                        InputSummary = BuildInputSummary(block.Input)
                    }).ConfigureAwait(false);
                }

                string toolResultJson;
                bool toolIsError;

                try
                {
                    // Convertir el Dictionary<string, JsonElement> del SDK a Dictionary<string, object?>
                    var toolInput = ConvertSdkInput(block.Input);
                    toolResultJson = await toolExecutor.ExecuteAsync(
                        block.Name,
                        toolsContext.AgentCode,
                        toolInput,
                        toolsContext.ExecutionId,
                        toolsContext.CaseIdentity,
                        ct).ConfigureAwait(false);
                    toolIsError = false;
                }
                catch (Exception ex)
                {
                    toolResultJson = JsonSerializer.Serialize(new { error = ex.Message });
                    toolIsError = true;
                }

                // Emitir evento SSE: tool_result (chip en el chat)
                if (toolsContext.StreamEventCallback != null)
                {
                    await toolsContext.StreamEventCallback(new ToolStreamEvent
                    {
                        EventType    = "tool_result",
                        ToolName     = block.Name,
                        ResultStatus = toolIsError ? "error" : "ok"
                    }).ConfigureAwait(false);
                }

                toolResultBlocks.Add((ToolResultBlockParam)new ToolResultBlockParam
                {
                    ToolUseID = block.ID,
                    Content   = toolResultJson,
                    IsError   = toolIsError ? true : null
                });
            }

            // Agregar los tool_result como mensaje user al historial
            messages.Add(new MessageParam
            {
                Role    = Role.User,
                Content = toolResultBlocks
            });
        }

        return new AiCompletionResult
        {
            Text             = finalText.Trim(),
            PromptTokens     = totalPromptTokens,
            CompletionTokens = totalCompletionTokens
        };
    }

    // ── Helpers de tool-use ───────────────────────────────────────────────

    /// <summary>
    /// Mapea las <see cref="OPAITool"/> del catálogo a los tipos del SDK Anthropic.
    /// </summary>
    private List<ToolUnion> MapToSdkTools(IReadOnlyList<OPAITool> tools)
    {
        var result = new List<ToolUnion>(tools.Count);
        foreach (var tool in tools)
        {
            var inputSchema = BuildInputSchema(tool.InputSchema, tool.Name);
            ToolUnion tu = new Tool
            {
                Name        = tool.Name,
                Description = tool.Description,
                InputSchema = inputSchema
            };
            result.Add(tu);
        }
        return result;
    }

    /// <summary>
    /// Construye un <see cref="InputSchema"/> a partir del JSON Schema de la tool.
    /// Si el schema es inválido, degrada a schema vacío y emite una advertencia en el log.
    /// </summary>
    /// <param name="inputSchemaJson">JSON Schema de entrada de la tool.</param>
    /// <param name="toolName">Nombre de la tool, para identificarla en el log.</param>
    private InputSchema BuildInputSchema(string? inputSchemaJson, string? toolName)
    {
        if (string.IsNullOrWhiteSpace(inputSchemaJson))
        {
            return new InputSchema
            {
                Type = JsonSerializer.SerializeToElement("object"),
                Properties = new Dictionary<string, JsonElement>()
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(inputSchemaJson);
            var root = doc.RootElement;

            var properties = new Dictionary<string, JsonElement>();
            if (root.TryGetProperty("properties", out var propsEl))
            {
                foreach (var prop in propsEl.EnumerateObject())
                    properties[prop.Name] = prop.Value.Clone();
            }

            var required = new List<string>();
            if (root.TryGetProperty("required", out var reqEl))
            {
                foreach (var item in reqEl.EnumerateArray())
                    required.Add(item.GetString() ?? string.Empty);
            }

            return new InputSchema
            {
                Type       = JsonSerializer.SerializeToElement("object"),
                Properties = properties,
                Required   = required
            };
        }
        catch (Exception ex)
        {
            // REQ-019: JSON Schema malformado — loguear advertencia y devolver schema vacío válido
            _logger.LogWarning(ex, "Tool '{ToolName}' tiene un InputSchema JSON inválido; se usará schema vacío.", toolName ?? "(desconocido)");
            return new InputSchema
            {
                Type       = JsonSerializer.SerializeToElement("object"),
                Properties = new Dictionary<string, JsonElement>()
            };
        }
    }

    /// <summary>
    /// Convierte el <c>Dictionary&lt;string, JsonElement&gt;</c> del SDK Anthropic
    /// a <c>IReadOnlyDictionary&lt;string, object?&gt;</c> esperado por el executor.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ConvertSdkInput(
        Dictionary<string, JsonElement> sdkInput)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, element) in sdkInput)
        {
            result[key] = element.ValueKind switch
            {
                JsonValueKind.String  => element.GetString(),
                JsonValueKind.Number  => element.TryGetInt64(out var l) ? (object?)l : element.GetDouble(),
                JsonValueKind.True    => true,
                JsonValueKind.False   => false,
                JsonValueKind.Null    => null,
                _                    => element.GetRawText()
            };
        }
        return result;
    }

    /// <summary>
    /// Construye un resumen corto del input de una tool para mostrar en la UI (chip).
    /// Muestra las primeras 3 claves/valores, truncado a 120 caracteres.
    /// </summary>
    private static string BuildInputSummary(Dictionary<string, JsonElement> input)
    {
        var parts = input.Take(3)
            .Select(kvp =>
            {
                var val = kvp.Value.ValueKind == JsonValueKind.String
                    ? kvp.Value.GetString()
                    : kvp.Value.GetRawText();
                return $"{kvp.Key}={val}";
            });
        var summary = string.Join(", ", parts);
        return summary.Length > 120 ? summary[..117] + "..." : summary;
    }

    // ── Construcción del cliente ──────────────────────────────────────────

    private AnthropicClient BuildClient()
    {
        // 1. SecretRef → Key Vault (no implementado hasta T0a/B-serie)
        // 2. ANTHROPIC_API_KEY del entorno → el AnthropicClient lo resuelve solo si ApiKey vacío
        // 3. ApiKey de la BD (fallback dev/test únicamente)
        //
        // REQ-019 T23: BaseUrl configurable para apuntar a endpoints Anthropic-compatibles
        // (ej. Azure AI Foundry). Si OPAIConfiguration.EndpointUrl está poblado se usa como
        // BaseUrl del SDK; de lo contrario el cliente usa el default (https://api.anthropic.com).
        //
        // Clave de caché: combina el origen de la key Y el endpoint para no mezclar clientes
        // de distintos proveedores/endpoints.
        //
        // Propiedad verificada en SDK Anthropic v10.4.0: BaseUrl (tipo Uri).

        var endpointUrl = string.IsNullOrWhiteSpace(_config.EndpointUrl) ? null : _config.EndpointUrl.Trim();

        var envKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var hasEnvKey = !string.IsNullOrWhiteSpace(envKey);
        var hasConfigKey = !string.IsNullOrWhiteSpace(_config.ApiKey)
                           && _config.ApiKey != "PLACEHOLDER";

        // Sufijo de endpoint para aislar entradas del caché por destino.
        // Se usa hash del endpointUrl para no exponer la URL en la clave.
        var endpointSuffix = endpointUrl is null
            ? string.Empty
            : "__ep__" + Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(endpointUrl)));

        if (hasEnvKey)
        {
            var cacheKey = "__env__" + endpointSuffix;
            return _clientCache.GetOrAdd(cacheKey, _ => BuildAnthropicClient(apiKey: null, endpointUrl));
        }

        if (hasConfigKey)
        {
            // La clave de caché es el hash SHA256 de la API key para no almacenarla en texto claro.
            var cacheKey = "bd__" + Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(_config.ApiKey!))) + endpointSuffix;
            return _clientCache.GetOrAdd(cacheKey, _ => BuildAnthropicClient(_config.ApiKey, endpointUrl));
        }

        // Sin ninguna key: dejar al SDK que intente desde entorno y falle en runtime
        // (AnthropicUnauthorizedException) con mensaje claro, no en startup.
        var fallbackKey = "__env__" + endpointSuffix;
        return _clientCache.GetOrAdd(fallbackKey, _ => BuildAnthropicClient(apiKey: null, endpointUrl));
    }

    /// <summary>
    /// Instancia un <see cref="AnthropicClient"/> con la API key y, opcionalmente, un
    /// BaseUrl alternativo (p. ej. Azure AI Foundry Anthropic-compatible).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="AnthropicClient.APIKey"/> y <see cref="AnthropicClient.BaseUrl"/> son
    /// propiedades <c>init</c>-only en el SDK v10.4.0, por lo que se asignan exclusivamente
    /// en el inicializador de objeto. Las cuatro combinaciones (key×baseUrl) se cubren
    /// explícitamente para respetar esa restricción del compilador (CS8852).
    /// </para>
    /// <para>
    /// Si <paramref name="apiKey"/> es <see langword="null"/>, el SDK toma la key desde la
    /// variable de entorno <c>ANTHROPIC_API_KEY</c> automáticamente.
    /// </para>
    /// <para>
    /// Si <paramref name="baseUrl"/> es <see langword="null"/>, el SDK apunta al default
    /// <c>https://api.anthropic.com</c> y agrega <c>/v1/messages</c> internamente.
    /// Para Azure AI Foundry usar <c>https://&lt;recurso&gt;.services.ai.azure.com/anthropic</c>.
    /// </para>
    /// </remarks>
    /// <param name="apiKey">API key explícita, o <see langword="null"/> para resolución por entorno.</param>
    /// <param name="baseUrl">URL base Anthropic-compatible, o <see langword="null"/> para el default del SDK.</param>
    private static AnthropicClient BuildAnthropicClient(string? apiKey, string? baseUrl)
    {
        var hasKey     = !string.IsNullOrWhiteSpace(apiKey);
        var hasBaseUrl = !string.IsNullOrWhiteSpace(baseUrl);

        // APIKey y BaseUrl son init-only en AnthropicClient v10.4.0 (CS8852);
        // se deben asignar en el inicializador de objeto, no después de la construcción.
        return (hasKey, hasBaseUrl) switch
        {
            (true,  true)  => new AnthropicClient { APIKey = apiKey!, BaseUrl = new Uri(baseUrl!) },
            (true,  false) => new AnthropicClient { APIKey = apiKey! },
            (false, true)  => new AnthropicClient { BaseUrl = new Uri(baseUrl!) },
            (false, false) => new AnthropicClient()
        };
    }
}
