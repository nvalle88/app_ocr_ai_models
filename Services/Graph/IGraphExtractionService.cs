namespace app_tramites.Services.Graph;

// ============================================================
// REQ-019 T20 — Interfaz del servicio de extracción de entidades
// del grafo a partir de texto/documentos (structured outputs).
// ============================================================

/// <summary>
/// Extrae entidades (diagnósticos, procedimientos, preexistencias, hallazgos, cédula)
/// de un texto o respuesta IA usando structured outputs de Claude, y las escribe
/// al grafo de conocimiento vía <see cref="IGraphService"/>.
/// </summary>
/// <remarks>
/// La extracción es <b>opcional/configurable</b>: se activa por paso del proceso
/// cuando <c>ProcessStep.EnableGraphExtraction</c> es <c>true</c>.
/// El hook en el orquestador llama a <see cref="ExtractAndMergeAsync"/> después
/// de obtener la respuesta del agente, antes de persistir el <c>StepExecution</c>.
/// </remarks>
public interface IGraphExtractionService
{
    /// <summary>
    /// Extrae entidades del <paramref name="text"/> usando structured outputs de Claude
    /// y hace MERGE idempotente de todos los nodos/relaciones en el grafo.
    /// </summary>
    /// <param name="text">Texto a analizar (OCR, respuesta IA o mensaje de usuario).</param>
    /// <param name="dataFileId">ID del <c>DataFile</c> origen (para la relación Caso→Documento→entidades).</param>
    /// <param name="caseCode">Código del caso (UUID) al que pertenece el documento.</param>
    /// <param name="origen">
    /// Indica el origen del texto para etiquetar hallazgos:
    /// <c>"documento"</c>, <c>"respuesta_ia"</c> o <c>"mensaje_usuario"</c>.
    /// </param>
    /// <param name="claudeFileId">
    /// ID del archivo en la Files API de Claude (opcional, solo si ya fue subido).
    /// Se guarda en el nodo <c>Documento</c> del grafo.
    /// </param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>
    /// El resultado de la extracción (para diagnóstico/traza), o <see langword="null"/>
    /// si el grafo no está configurado (B6) o el texto está vacío.
    /// </returns>
    Task<GraphExtractionResult?> ExtractAndMergeAsync(
        string text,
        string dataFileId,
        string caseCode,
        string origen = "documento",
        string? claudeFileId = null,
        CancellationToken ct = default);
}
