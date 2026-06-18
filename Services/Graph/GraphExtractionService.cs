using System.Text.Json;
using app_ocr_ai_models.Data;
using app_tramites.Services.Ai;
using Microsoft.EntityFrameworkCore;

namespace app_tramites.Services.Graph;

// ============================================================
// REQ-019 T20 — Extracción de entidades por structured outputs
// de Claude y escritura idempotente al grafo de conocimiento.
// ============================================================

/// <summary>
/// Implementación de <see cref="IGraphExtractionService"/> que usa structured outputs
/// de Claude para extraer entidades de texto y las persiste en Neo4j vía <see cref="IGraphService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Structured outputs: se pide al modelo que responda <b>exclusivamente</b> en JSON
/// que coincida con <see cref="GraphExtractionResult"/>. El prompt incluye el JSON Schema
/// esperado para guiar la extracción. Si el modelo responde con texto no parseable,
/// se registra en log y se retorna <see langword="null"/> sin propagar la excepción
/// (la extracción es no-bloqueante respecto al flujo principal).
/// </para>
/// <para>
/// Si el servicio de grafo no está configurado (B6 sin credenciales), la primera
/// llamada a <see cref="IGraphService"/> lanzará una <see cref="InvalidOperationException"/>
/// que se captura aquí y se registra como aviso; no bloquea el análisis del caso.
/// </para>
/// <para>
/// El servicio de IA se resuelve vía <see cref="AiCompletionServiceFactory"/> usando
/// el <c>OPAIConfiguration</c> activo de tipo Anthropic (Provider = "Anthropic").
/// Si no hay configuración activa Anthropic, la extracción se omite sin error.
/// </para>
/// </remarks>
public sealed class GraphExtractionService : IGraphExtractionService
{
    private readonly IGraphService _graph;
    private readonly AiCompletionServiceFactory _aiFactory;
    private readonly OCRDbContext _db;
    private readonly ILogger<GraphExtractionService> _logger;

    // Prompt del sistema para guided extraction (structured outputs)
    private const string ExtractionSystemPrompt = @"
Eres un extractor de entidades médicas. Analiza el texto proporcionado y responde
ÚNICAMENTE con un objeto JSON válido que contenga las entidades identificadas.
No incluyas ningún texto fuera del JSON. Si no encuentras una entidad, usa un array vacío.

Esquema de respuesta:
{
  ""cedula"": ""<string o null>"",
  ""nombreAfiliado"": ""<string o null>"",
  ""numeroSobre"": ""<string o null>"",
  ""diagnosticos"": [{ ""codigo"": ""<CIE-10>"", ""descripcion"": ""<string o null>"" }],
  ""procedimientos"": [{ ""codigo"": ""<string>"", ""descripcion"": ""<string o null>"" }],
  ""preexistencias"": [{ ""codigo"": ""<string>"", ""descripcion"": ""<string o null>"" }],
  ""hallazgos"": [{ ""texto"": ""<afirmación relevante>"", ""origen"": ""documento"" }]
}

Extrae solo lo que el texto mencione explícitamente. No inferas ni inventes datos.";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Crea el servicio de extracción.
    /// </summary>
    /// <param name="graph">Servicio de grafo para persistir los nodos/relaciones.</param>
    /// <param name="aiFactory">Factory de servicios IA (para resolver la instancia Claude).</param>
    /// <param name="db">Contexto EF para obtener la configuración activa de Anthropic.</param>
    /// <param name="logger">Logger.</param>
    public GraphExtractionService(
        IGraphService graph,
        AiCompletionServiceFactory aiFactory,
        OCRDbContext db,
        ILogger<GraphExtractionService> logger)
    {
        _graph     = graph     ?? throw new ArgumentNullException(nameof(graph));
        _aiFactory = aiFactory ?? throw new ArgumentNullException(nameof(aiFactory));
        _db        = db        ?? throw new ArgumentNullException(nameof(db));
        _logger    = logger    ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<GraphExtractionResult?> ExtractAndMergeAsync(
        string text,
        string dataFileId,
        string caseCode,
        string origen = "documento",
        string? claudeFileId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // ── 1. Resolver servicio Claude desde la factory ──────────────────
        // Se usa el primer OPAIConfiguration activo de tipo Anthropic disponible.
        // Si no hay ninguno, la extracción se omite sin error.
        var anthropicConfig = await _db.OPAIConfiguration
            .Where(c => c.IsActive
                        && c.Provider != null
                        && c.Provider == "Anthropic")
            .OrderByDescending(c => c.CreatedDate)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (anthropicConfig == null)
        {
            _logger.LogWarning(
                "[T20] No hay OPAIConfiguration activa de tipo Anthropic. " +
                "La extracción de grafo para dataFileId={DataFileId} se omite.",
                dataFileId);
            return null;
        }

        var ai = _aiFactory.Create(anthropicConfig);

        // ── 2. Llamada de structured outputs a Claude ─────────────────────
        GraphExtractionResult? extraction;
        try
        {
            var request = new AiCompletionRequest
            {
                SystemPrompt = ExtractionSystemPrompt,
                UserMessage  = $"Texto a analizar:\n\n{text}",
                MaxTokens    = 1024,
                Temperature  = 0m
            };

            var result = await ai.CompleteAsync(request, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(result.Text))
            {
                _logger.LogWarning("[T20] Extracción de grafo: respuesta vacía del modelo para dataFileId={DataFileId}.", dataFileId);
                return null;
            }

            // Limpiar posible markdown code fence que el modelo añada
            var json = result.Text.Trim();
            if (json.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = json.IndexOf('\n');
                var lastFence    = json.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewline > 0 && lastFence > firstNewline)
                    json = json[(firstNewline + 1)..lastFence].Trim();
            }

            extraction = JsonSerializer.Deserialize<GraphExtractionResult>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[T20] Error en extracción structured-outputs para dataFileId={DataFileId}. " +
                "La extracción al grafo se omite para este documento.",
                dataFileId);
            return null;
        }

        if (extraction == null)
        {
            _logger.LogWarning("[T20] Extracción resultó null para dataFileId={DataFileId}.", dataFileId);
            return null;
        }

        // ── 2. MERGE idempotente al grafo (no-bloqueante si B6 falta) ─────
        try
        {
            await MergeExtractionAsync(extraction, dataFileId, caseCode, claudeFileId, ct)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("B6"))
        {
            // Grafo no configurado (bloqueo B6) — no bloquear el análisis
            _logger.LogWarning(ex,
                "[T20 B6] Grafo Neo4j no configurado. " +
                "La extracción de entidades al grafo se omite hasta que B6 esté desbloqueado.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[T20] Error escribiendo extracción al grafo para dataFileId={DataFileId}. " +
                "Se continúa sin persistir en el grafo.",
                dataFileId);
        }

        return extraction;
    }

    // ── MERGE de nodos/relaciones ─────────────────────────────────────────

    private async Task MergeExtractionAsync(
        GraphExtractionResult extraction,
        string dataFileId,
        string caseCode,
        string? claudeFileId,
        CancellationToken ct)
    {
        // Nodo Caso (espejo del ProcessCase SQL)
        await _graph.MergeCasoAsync(caseCode, numeroSobre: extraction.NumeroSobre, ct)
            .ConfigureAwait(false);

        // Nodo Documento (referencia al DataFile)
        await _graph.MergeDocumentoAsync(
            dataFileId:   dataFileId,
            claudeFileId: claudeFileId,
            fileUri:      null,
            tipo:         "ocr",
            caseCode:     caseCode,
            ct:           ct)
            .ConfigureAwait(false);

        // Afiliado
        if (!string.IsNullOrWhiteSpace(extraction.Cedula))
        {
            await _graph.MergeAfiliadoAsync(extraction.Cedula, extraction.NombreAfiliado, ct)
                .ConfigureAwait(false);

            // Sobre → Afiliado (si se extrajeron ambos)
            if (!string.IsNullOrWhiteSpace(extraction.NumeroSobre))
            {
                await _graph.MergeSobreAsync(
                    extraction.NumeroSobre,
                    cuentaZendesk: null,
                    ticketId:      null,
                    cedula:        extraction.Cedula,
                    ct:            ct)
                    .ConfigureAwait(false);
            }
        }

        // Diagnósticos
        foreach (var diag in extraction.Diagnosticos)
        {
            if (!string.IsNullOrWhiteSpace(diag.Codigo))
                await _graph.MergeDiagnosticoAsync(diag.Codigo, diag.Descripcion, dataFileId, ct)
                    .ConfigureAwait(false);
        }

        // Procedimientos
        foreach (var proc in extraction.Procedimientos)
        {
            if (!string.IsNullOrWhiteSpace(proc.Codigo))
                await _graph.MergeProcedimientoAsync(proc.Codigo, proc.Descripcion, dataFileId, ct)
                    .ConfigureAwait(false);
        }

        // Preexistencias
        foreach (var px in extraction.Preexistencias)
        {
            if (!string.IsNullOrWhiteSpace(px.Codigo))
                await _graph.MergePreexistenciaAsync(
                    px.Codigo,
                    px.Descripcion,
                    dataFileId,
                    cedula: extraction.Cedula,
                    ct:     ct)
                    .ConfigureAwait(false);
        }

        // Hallazgos
        foreach (var h in extraction.Hallazgos)
        {
            if (!string.IsNullOrWhiteSpace(h.Texto))
                await _graph.MergeHallazgoAsync(h.Texto, h.Origen ?? "documento", dataFileId, ct)
                    .ConfigureAwait(false);
        }
    }
}
