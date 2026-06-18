using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using app_ocr_ai_models.Data;
using app_tramites.Models.ModelAi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace app_tramites.Services.Ai.Tools;

// ============================================================
// REQ-019 T5 — Executor genérico de tools (InternalApi + Armonix).
// D2: solo ejecuta tools vinculadas+IsEnabled al agente.
// D4: aplica guardián anti-IDOR antes de salir a la API.
// ============================================================

/// <summary>
/// Implementación de <see cref="IToolExecutor"/> para los binding types
/// <c>InternalApi</c> y <c>Armonix</c> (mismo executor HTTP, distinta baseUrl).
/// Lee <c>OPAITool.BindingConfig</c>, arma el request, inyecta el token Saludsa,
/// y persiste <see cref="ToolInvocation"/> en BD.
/// </summary>
/// <remarks>
/// Dispatcher por <c>BindingType</c>:
/// <list type="bullet">
///   <item><c>InternalApi</c> — APIs internas (api-contrato, api-prestador).</item>
///   <item><c>Armonix</c>    — APIs de Armonix (api-armonix); mismo flujo HTTP.</item>
///   <item><c>Mcp</c>        — Reconocido pero lanza <see cref="NotImplementedException"/>.
///     Comentario: "MCP-ready: futuro servidor MCP".</item>
/// </list>
/// Las <c>baseUrl</c> con placeholder (<c>{api-contrato}</c>, etc.) se resuelven
/// desde configuración (B1/T0a). No hay red si no están configuradas.
/// </remarks>
public sealed class InternalApiToolExecutor : IToolExecutor
{
    private readonly OCRDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISaludsaTokenProvider _tokenProvider;
    private readonly IToolAuthorizationGuard _authGuard;
    private readonly IConfiguration _config;
    private readonly ILogger<InternalApiToolExecutor> _logger;

    private static readonly JsonSerializerOptions SerializerOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Inicializa el executor con sus dependencias.
    /// </summary>
    /// <param name="db">Contexto EF para persistir <see cref="ToolInvocation"/>.</param>
    /// <param name="httpClientFactory">Factory de HttpClient para llamadas a la API.</param>
    /// <param name="tokenProvider">Proveedor de token OAuth2 Saludsa.</param>
    /// <param name="authGuard">Guardián anti-IDOR (D4).</param>
    /// <param name="config">Configuración de la aplicación (resolución de baseUrl).</param>
    /// <param name="logger">Logger.</param>
    public InternalApiToolExecutor(
        OCRDbContext db,
        IHttpClientFactory httpClientFactory,
        ISaludsaTokenProvider tokenProvider,
        IToolAuthorizationGuard authGuard,
        IConfiguration config,
        ILogger<InternalApiToolExecutor> logger)
    {
        _db              = db              ?? throw new ArgumentNullException(nameof(db));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _tokenProvider   = tokenProvider   ?? throw new ArgumentNullException(nameof(tokenProvider));
        _authGuard       = authGuard       ?? throw new ArgumentNullException(nameof(authGuard));
        _config          = config          ?? throw new ArgumentNullException(nameof(config));
        _logger          = logger          ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(
        string toolCode,
        string agentCode,
        IReadOnlyDictionary<string, object?> toolInput,
        long executionId,
        string? caseIdentity = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentCode);

        // ── 1. Cargar la tool del catálogo ───────────────────────────────
        var tool = await _db.OPAITool
            .FirstOrDefaultAsync(t => t.Code == toolCode && t.IsActive, ct)
            .ConfigureAwait(false);

        if (tool == null)
            throw new InvalidOperationException(
                $"[T5] Tool '{toolCode}' no encontrada o inactiva en el catálogo.");

        // ── 2. D2: Verificar que la tool está vinculada al agente (IsEnabled) ──
        var modelTool = await _db.OPAIModelTool
            .FirstOrDefaultAsync(mt =>
                mt.ModelCode == agentCode
                && mt.ToolCode == toolCode
                && mt.IsEnabled, ct)
            .ConfigureAwait(false);

        if (modelTool == null)
            throw new UnauthorizedAccessException(
                $"[T5 D2] Tool '{toolCode}' no está habilitada para el agente '{agentCode}'. " +
                "Vincúlela en OPAIModelTool con IsEnabled=1.");

        // ── 3. Deserializar BindingConfig ────────────────────────────────
        if (string.IsNullOrWhiteSpace(tool.BindingConfig))
            throw new InvalidOperationException(
                $"[T5] Tool '{toolCode}' no tiene BindingConfig configurado.");

        ToolBindingConfig binding;
        try
        {
            binding = JsonSerializer.Deserialize<ToolBindingConfig>(
                tool.BindingConfig,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("BindingConfig deserializó como null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"[T5] BindingConfig de la tool '{toolCode}' no es JSON válido: {ex.Message}", ex);
        }

        // ── 4. Dispatcher por BindingType ────────────────────────────────
        if (string.Equals(tool.BindingType, "Mcp", StringComparison.OrdinalIgnoreCase))
        {
            // MCP-ready: futuro servidor MCP — no implementado en esta iteración (T5).
            // El soporte MCP se añadirá cuando se disponga del servidor MCP Saludsa.
            throw new NotImplementedException(
                "[T5] BindingType 'Mcp' no está implementado. " +
                "MCP-ready: se integrará cuando exista el servidor MCP Saludsa.");
        }

        // InternalApi y Armonix comparten el mismo executor HTTP
        if (!string.Equals(tool.BindingType, "InternalApi", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(tool.BindingType, "Armonix", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"[T5] BindingType '{tool.BindingType}' no soportado por InternalApiToolExecutor. " +
                "Tipos soportados: InternalApi, Armonix.");
        }

        // ── 5. D4: Guardián anti-IDOR ────────────────────────────────────
        if (!_authGuard.IsAuthorized(toolCode, toolInput, caseIdentity))
        {
            var denialJson = JsonSerializer.Serialize(new
            {
                error   = "IDOR_DENIED",
                message = $"[T5 D4] Los identificadores de afiliado en el input de la tool " +
                          $"'{toolCode}' no corresponden al titular del Caso. Acceso denegado."
            });

            // Registrar el intento en ToolInvocation con IsError=true
            await PersistInvocationAsync(
                executionId, toolCode,
                requestJson:  JsonSerializer.Serialize(toolInput),
                responseJson: denialJson,
                isError:      true,
                start:        DateTime.UtcNow,
                ct:           ct)
                .ConfigureAwait(false);

            _logger.LogWarning(
                "[T5 D4] IDOR_DENIED tool={ToolCode} agente={AgentCode} caseIdentity={CaseIdentity}",
                toolCode, agentCode, caseIdentity);

            throw new UnauthorizedAccessException(
                $"[T5 D4] Tool '{toolCode}': identificadores de afiliado no coinciden con el contexto del Caso.");
        }

        // ── 6. Resolver baseUrl desde configuración ──────────────────────
        var resolvedBaseUrl = ResolveBaseUrl(binding.BaseUrl);

        // ── 7. Obtener token Saludsa ─────────────────────────────────────
        IReadOnlyDictionary<string, string> authHeaders;
        try
        {
            authHeaders = await _tokenProvider.GetAuthHeadersAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[T5 B2] No se pudo obtener token OAuth2 Saludsa para tool '{ToolCode}'.", toolCode);
            throw;
        }

        // ── 8. Armar y ejecutar el request HTTP ──────────────────────────
        var startDate   = DateTime.UtcNow;
        var requestJson = JsonSerializer.Serialize(toolInput);
        string responseJson;
        bool isError;

        try
        {
            responseJson = await CallApiAsync(
                binding, resolvedBaseUrl, authHeaders, toolInput, ct)
                .ConfigureAwait(false);
            isError = false;
        }
        catch (Exception ex)
        {
            responseJson = JsonSerializer.Serialize(new
            {
                error   = ex.GetType().Name,
                message = ex.Message
            });
            isError = true;
            _logger.LogWarning(ex, "[T5] Error invocando tool '{ToolCode}'.", toolCode);
        }

        // ── 9. Persistir ToolInvocation ──────────────────────────────────
        await PersistInvocationAsync(
            executionId, toolCode,
            requestJson, responseJson, isError,
            startDate, ct)
            .ConfigureAwait(false);

        if (isError)
            throw new InvalidOperationException(
                $"[T5] La tool '{toolCode}' respondió con error. Detalle: {responseJson}");

        return responseJson;
    }

    // ── HTTP ─────────────────────────────────────────────────────────────

    private async Task<string> CallApiAsync(
        ToolBindingConfig binding,
        string resolvedBaseUrl,
        IReadOnlyDictionary<string, string> authHeaders,
        IReadOnlyDictionary<string, object?> toolInput,
        CancellationToken ct)
    {
        var httpClient = _httpClientFactory.CreateClient("SaludsaInternalApi");

        using var requestMessage = BuildRequest(binding, resolvedBaseUrl, toolInput);

        // Inyectar cabeceras de autenticación Saludsa
        foreach (var (name, value) in authHeaders)
            requestMessage.Headers.TryAddWithoutValidation(name, value);

        using var response = await httpClient.SendAsync(requestMessage, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"[T5] API respondió {(int)response.StatusCode}: {body}",
                null, response.StatusCode);

        return body;
    }

    private static HttpRequestMessage BuildRequest(
        ToolBindingConfig binding,
        string resolvedBaseUrl,
        IReadOnlyDictionary<string, object?> toolInput)
    {
        var method = binding.Method.ToUpperInvariant() == "POST"
            ? HttpMethod.Post
            : HttpMethod.Get;

        var uriBuilder = new UriBuilder(resolvedBaseUrl.TrimEnd('/') + binding.Path);

        if (method == HttpMethod.Get && binding.ParamMap.Count > 0)
        {
            // Armar query string con los campos de paramMap
            var queryParams = new List<string>();
            foreach (var (inputKey, queryKey) in binding.ParamMap)
            {
                if (toolInput.TryGetValue(inputKey, out var val) && val != null)
                    queryParams.Add($"{Uri.EscapeDataString(queryKey)}={Uri.EscapeDataString(val.ToString()!)}");
            }
            if (queryParams.Count > 0)
                uriBuilder.Query = string.Join("&", queryParams);
        }

        var request = new HttpRequestMessage(method, uriBuilder.Uri);

        if (method == HttpMethod.Post)
        {
            // Construir body JSON según bodyMap (soporta dot notation para objetos anidados)
            var bodyNode = new JsonObject();
            if (binding.BodyMap.Count > 0)
            {
                foreach (var fieldPath in binding.BodyMap)
                {
                    var inputKey = fieldPath.Contains('.') ? fieldPath.Split('.')[^1] : fieldPath;
                    if (toolInput.TryGetValue(inputKey, out var val) && val != null)
                        SetNestedJsonValue(bodyNode, fieldPath, val);
                }
            }
            else
            {
                // Sin bodyMap: enviar todos los campos del input como body plano
                foreach (var (k, v) in toolInput)
                    if (v != null)
                        bodyNode[k] = JsonValue.Create(v.ToString());
            }

            request.Content = new StringContent(
                bodyNode.ToJsonString(),
                Encoding.UTF8,
                "application/json");
        }

        return request;
    }

    /// <summary>
    /// Establece un valor en un <see cref="JsonObject"/> usando notación dot
    /// (ej: <c>filter.region</c> → <c>{ "filter": { "region": value } }</c>).
    /// </summary>
    private static void SetNestedJsonValue(JsonObject root, string dotPath, object value)
    {
        var parts = dotPath.Split('.');
        var current = root;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            var key = parts[i];
            if (current[key] is not JsonObject nested)
            {
                nested = new JsonObject();
                current[key] = nested;
            }
            current = nested;
        }

        current[parts[^1]] = JsonValue.Create(value.ToString());
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Resuelve los placeholders de baseUrl (<c>{api-contrato}</c>, etc.)
    /// con los valores de la configuración (sección <c>Saludsa:BaseUrls</c>).
    /// </summary>
    private string ResolveBaseUrl(string baseUrlTemplate)
    {
        // Mapeo: placeholder → clave de configuración
        // B1/T0a: las reglas de egress deben existir para cada URL en producción.
        var placeholders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{api-contrato}"]  = "Saludsa:BaseUrls:ApiContrato",
            ["{api-armonix}"]   = "Saludsa:BaseUrls:ApiArmonix",
            ["{api-prestador}"] = "Saludsa:BaseUrls:ApiPrestador"
        };

        foreach (var (placeholder, configKey) in placeholders)
        {
            if (!baseUrlTemplate.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
                continue;

            var resolved = _config[configKey];
            if (string.IsNullOrWhiteSpace(resolved))
                // B1/T0a: baseUrl no configurada — falla en runtime con mensaje claro
                throw new InvalidOperationException(
                    $"[T5 B1] BaseUrl '{placeholder}' no está configurada. " +
                    $"Configure '{configKey}' en appsettings / Key Vault (B1/T0a).");

            return resolved.TrimEnd('/');
        }

        // Si no era placeholder, usar tal cual (URL literal en la config)
        return baseUrlTemplate.TrimEnd('/');
    }

    private async Task PersistInvocationAsync(
        long executionId,
        string toolCode,
        string? requestJson,
        string? responseJson,
        bool isError,
        DateTime start,
        CancellationToken ct)
    {
        try
        {
            var invocation = new ToolInvocation
            {
                ExecutionId  = executionId,
                ToolCode     = toolCode,
                RequestJson  = requestJson,
                ResponseJson = responseJson,
                IsError      = isError,
                StartDate    = start,
                EndDate      = DateTime.UtcNow
            };
            _db.ToolInvocation.Add(invocation);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // No propagar errores de auditoría — registrar y continuar
            _logger.LogWarning(ex,
                "[T5] No se pudo persistir ToolInvocation para tool '{ToolCode}' / execution {ExecutionId}.",
                toolCode, executionId);
        }
    }
}
