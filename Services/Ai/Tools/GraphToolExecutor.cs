using System.Text.Json;
using System.Text.Json.Nodes;
using app_ocr_ai_models.Data;
using app_tramites.Models.ModelAi;
using app_tramites.Services.Graph;
using Microsoft.EntityFrameworkCore;

namespace app_tramites.Services.Ai.Tools;

// ============================================================
// REQ-019 T21 — Executor de la tool grafo_consultar.
// BindingType: 'Graph'
// Read-only; allow-list de consultas Cypher predefinidas.
// Persiste ToolInvocation igual que InternalApiToolExecutor.
// Respeta la identidad del caso (anti-IDOR D4).
// ============================================================

/// <summary>
/// Ejecuta la tool <c>grafo_consultar</c> sobre el grafo Neo4j.
/// </summary>
/// <remarks>
/// <para>
/// Seguridad:
/// <list type="bullet">
///   <item>D2: verifica que la tool esté vinculada+habilitada al agente en <c>OPAIModelTool</c>.</item>
///   <item>D4 (anti-IDOR): si la consulta recibe <c>caseCode</c>, valida que coincida con
///     el <c>caseCode</c> del contexto (extraído de <c>caseIdentity</c>).
///     Si recibe <c>cedula</c>, no se valida aquí (la cédula puede ser del titular o de un
///     beneficiario dentro del mismo caso; la responsabilidad de scope queda en el agente).</item>
///   <item>Allow-list: la consulta Cypher nunca llega del modelo en forma libre;
///     el modelo solo elige un <c>queryName</c> del enum definido en <c>InputSchema</c>.</item>
/// </list>
/// </para>
/// <para>
/// B6: si Neo4j no está configurado, falla en runtime con
/// <see cref="InvalidOperationException"/> que referencia el bloqueo B6.
/// </para>
/// </remarks>
public sealed class GraphToolExecutor : IToolExecutor
{
    private readonly OCRDbContext _db;
    private readonly IGraphService _graph;
    private readonly IToolAuthorizationGuard _authGuard;
    private readonly ILogger<GraphToolExecutor> _logger;

    private static readonly JsonSerializerOptions SerializerOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Crea el executor del grafo.
    /// </summary>
    /// <param name="db">Contexto EF para persistir <see cref="ToolInvocation"/> y verificar D2.</param>
    /// <param name="graph">Servicio del grafo de conocimiento.</param>
    /// <param name="authGuard">Guardián anti-IDOR D4.</param>
    /// <param name="logger">Logger.</param>
    public GraphToolExecutor(
        OCRDbContext db,
        IGraphService graph,
        IToolAuthorizationGuard authGuard,
        ILogger<GraphToolExecutor> logger)
    {
        _db        = db        ?? throw new ArgumentNullException(nameof(db));
        _graph     = graph     ?? throw new ArgumentNullException(nameof(graph));
        _authGuard = authGuard ?? throw new ArgumentNullException(nameof(authGuard));
        _logger    = logger    ?? throw new ArgumentNullException(nameof(logger));
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
        ArgumentNullException.ThrowIfNull(toolInput);

        // ── 1. Cargar la tool del catálogo ───────────────────────────────
        var tool = await _db.OPAITool
            .FirstOrDefaultAsync(t => t.Code == toolCode && t.IsActive, ct)
            .ConfigureAwait(false);

        if (tool == null)
            throw new InvalidOperationException(
                $"[T21] Tool '{toolCode}' no encontrada o inactiva en el catálogo.");

        if (!string.Equals(tool.BindingType, "Graph", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"[T21] GraphToolExecutor solo maneja BindingType 'Graph'. " +
                $"Tool '{toolCode}' tiene BindingType '{tool.BindingType}'.");

        // ── 2. D2: Verificar vinculación agente-tool ─────────────────────
        var modelTool = await _db.OPAIModelTool
            .FirstOrDefaultAsync(mt =>
                mt.ModelCode == agentCode
                && mt.ToolCode == toolCode
                && mt.IsEnabled, ct)
            .ConfigureAwait(false);

        if (modelTool == null)
            throw new UnauthorizedAccessException(
                $"[T21 D2] Tool '{toolCode}' no está habilitada para el agente '{agentCode}'. " +
                "Vincúlela en OPAIModelTool con IsEnabled=1.");

        // ── 3. Extraer queryName y params del input ──────────────────────
        if (!toolInput.TryGetValue("queryName", out var qnObj) || qnObj is null)
            throw new ArgumentException(
                "[T21] El campo 'queryName' es obligatorio en el input de grafo_consultar.");

        var queryName = qnObj.ToString()!;

        // Extraer params (puede ser un JsonElement, JsonObject, o dict ya parseado)
        var queryParams = ExtractParams(toolInput);

        // ── 4. D4: Anti-IDOR en consultas por caseCode ───────────────────
        // Si la consulta incluye un caseCode, debe coincidir con la identidad del caso.
        // caseIdentity en este contexto es el caseCode (GUID) del ProcessCase en curso.
        if (queryParams.TryGetValue("caseCode", out var inputCaseCode)
            && inputCaseCode is not null
            && !string.IsNullOrWhiteSpace(caseIdentity)
            && !string.Equals(
                inputCaseCode.ToString(), caseIdentity, StringComparison.OrdinalIgnoreCase))
        {
            var denialJson = JsonSerializer.Serialize(new
            {
                error   = "IDOR_DENIED",
                message = $"[T21 D4] El caseCode del input ('{inputCaseCode}') " +
                          $"no corresponde al Caso en contexto. Acceso denegado."
            });

            await PersistInvocationAsync(
                executionId, toolCode,
                requestJson:  JsonSerializer.Serialize(toolInput),
                responseJson: denialJson,
                isError:      true,
                start:        DateTime.UtcNow,
                ct:           ct)
                .ConfigureAwait(false);

            _logger.LogWarning(
                "[T21 D4] IDOR_DENIED grafo_consultar caseCode={InputCase} contexto={ContextCase} agente={Agent}",
                inputCaseCode, caseIdentity, agentCode);

            throw new UnauthorizedAccessException(
                $"[T21 D4] grafo_consultar: caseCode del input no coincide con el contexto del Caso.");
        }

        // ── 5. Ejecutar consulta read-only ───────────────────────────────
        var startDate   = DateTime.UtcNow;
        var requestJson = JsonSerializer.Serialize(toolInput);
        string responseJson;
        bool isError;

        try
        {
            var rows = await _graph.QueryAsync(queryName, queryParams, ct)
                .ConfigureAwait(false);

            responseJson = JsonSerializer.Serialize(new
            {
                queryName,
                rowCount = rows.Count,
                rows
            }, SerializerOptions);

            isError = false;

            _logger.LogInformation(
                "[T21] grafo_consultar query={QueryName} rows={RowCount} agente={Agent}",
                queryName, rows.Count, agentCode);
        }
        catch (ArgumentException ex)
        {
            // queryName no está en allow-list
            responseJson = JsonSerializer.Serialize(new
            {
                error   = "QUERY_NOT_ALLOWED",
                message = ex.Message
            });
            isError = true;
            _logger.LogWarning(ex, "[T21] Consulta de grafo denegada por allow-list: {QueryName}.", queryName);
        }
        catch (Exception ex)
        {
            responseJson = JsonSerializer.Serialize(new
            {
                error   = ex.GetType().Name,
                message = ex.Message
            });
            isError = true;
            _logger.LogWarning(ex, "[T21] Error ejecutando grafo_consultar query={QueryName}.", queryName);
        }

        // ── 6. Persistir ToolInvocation ──────────────────────────────────
        await PersistInvocationAsync(
            executionId, toolCode,
            requestJson, responseJson, isError,
            startDate, ct)
            .ConfigureAwait(false);

        if (isError)
            throw new InvalidOperationException(
                $"[T21] grafo_consultar respondió con error. Detalle: {responseJson}");

        return responseJson;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Extrae el sub-diccionario <c>params</c> del input de la tool.
    /// Soporta que <c>params</c> llegue como <c>JsonElement</c>, <c>JsonObject</c>,
    /// <c>Dictionary</c> o cualquier objeto serializable.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ExtractParams(
        IReadOnlyDictionary<string, object?> toolInput)
    {
        if (!toolInput.TryGetValue("params", out var paramsObj) || paramsObj is null)
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        // JsonElement (llega del SDK Anthropic al deserializar tool_use input)
        if (paramsObj is System.Text.Json.JsonElement element)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                    dict[prop.Name] = prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.ToString();
            }
            return dict;
        }

        // Dictionary<string,object?> (llega cuando se deserializa manualmente)
        if (paramsObj is IReadOnlyDictionary<string, object?> roDict)
            return roDict;

        if (paramsObj is Dictionary<string, object?> mDict)
            return mDict;

        // Fallback: re-serializar y deserializar
        try
        {
            var json = JsonSerializer.Serialize(paramsObj);
            var deserialized = JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
            return deserialized
                ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
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
                "[T21] No se pudo persistir ToolInvocation para tool '{ToolCode}' / execution {ExecutionId}.",
                toolCode, executionId);
        }
    }
}
