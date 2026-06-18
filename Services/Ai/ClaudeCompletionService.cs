using Anthropic;
using Anthropic.Models.Messages;
using app_tramites.Models.ModelAi;
using System.Runtime.CompilerServices;

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

    /// <summary>
    /// Crea el servicio con la configuración del agente.
    /// </summary>
    /// <param name="config">Configuración del modelo (ApiKey / SecretRef).</param>
    public ClaudeCompletionService(OPAIConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
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

    // ── Construcción del cliente ──────────────────────────────────────────

    private AnthropicClient BuildClient()
    {
        // 1. SecretRef → Key Vault (no implementado hasta T0a/B-serie)
        // 2. ANTHROPIC_API_KEY del entorno → el AnthropicClient lo resuelve solo si ApiKey vacío
        // 3. ApiKey de la BD (fallback dev/test únicamente)

        var envKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var hasEnvKey = !string.IsNullOrWhiteSpace(envKey);
        var hasConfigKey = !string.IsNullOrWhiteSpace(_config.ApiKey)
                           && _config.ApiKey != "PLACEHOLDER";

        if (hasEnvKey)
        {
            // El SDK leyó ANTHROPIC_API_KEY automáticamente
            return new AnthropicClient();
        }

        if (hasConfigKey)
        {
            // Fallback: key de la BD (solo db-nexus-test).
            // La propiedad del SDK v10+ es APIKey (mayúsculas).
            return new AnthropicClient { APIKey = _config.ApiKey };
        }

        // Sin ninguna key: dejar al SDK que intente desde entorno y falle en runtime
        // (AnthropicUnauthorizedException) con mensaje claro, no en startup.
        return new AnthropicClient();
    }
}
