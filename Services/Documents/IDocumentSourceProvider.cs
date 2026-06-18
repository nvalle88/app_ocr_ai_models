using app_ocr_ai_models.Data;
using app_tramites.Models.ModelAi;

namespace app_ocr_ai_models.Services.Documents;

// ============================================================
// REQ-019 T22 — Abstracción de fuentes documentales del Sobre.
// Permite agregar Armonix, Zendesk (y futuras) sin modificar
// el controller de destino.
// ============================================================

/// <summary>
/// Contrato para proveedores de documentos de un sobre de reembolso.
/// Cada implementación representa una fuente distinta (Zendesk, Armonix, etc.).
/// </summary>
public interface IDocumentSourceProvider
{
    /// <summary>
    /// Lista los nombres o identificadores de documentos disponibles para un trámite,
    /// sin descargar el contenido binario.
    /// Útil para previsualización antes de importar.
    /// </summary>
    /// <param name="filter">Identificadores del sobre/trámite.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Lista de nombres o IDs de documentos disponibles.</returns>
    Task<IReadOnlyList<string>> ListarDocumentosAsync(
        SobreDocumentosFilter filter,
        CancellationToken ct = default);

    /// <summary>
    /// Descarga todos los documentos del sobre, ejecuta OCR sobre cada uno
    /// y crea los <see cref="DataFile"/> correspondientes en el <paramref name="caso"/> dado.
    /// </summary>
    /// <param name="filter">Identificadores del sobre/trámite.</param>
    /// <param name="caso">Caso OCR al que se añaden los DataFile.</param>
    /// <param name="db">Contexto EF para persistir los DataFile.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>
    /// Resultado con los IDs de DataFile creados y las advertencias por archivos
    /// que no pudieron procesarse.
    /// </returns>
    Task<ImportarDocumentosResult> ImportarDocumentosAsync(
        SobreDocumentosFilter filter,
        ProcessCase caso,
        OCRDbContext db,
        CancellationToken ct = default);
}

/// <summary>
/// Filtro de identificación del sobre para consultas a la fuente documental.
/// Agrupa todos los identificadores que Armonix requiere; Zendesk solo usa
/// <see cref="NumeroSobre"/>.
/// </summary>
public sealed class SobreDocumentosFilter
{
    /// <summary>Número del sobre de reembolso.</summary>
    public string NumeroSobre { get; init; } = string.Empty;

    /// <summary>Número de contrato del afiliado (requerido por Armonix).</summary>
    public string? NumeroContrato { get; init; }

    /// <summary>Código de producto (requerido por Armonix).</summary>
    public string? CodigoProducto { get; init; }

    /// <summary>Código de región (requerido por Armonix).</summary>
    public string? CodigoRegion { get; init; }

    /// <summary>Número de persona/paciente (requerido por Armonix).</summary>
    public string? NumeroPersonaPaciente { get; init; }
}

/// <summary>
/// Resultado de la importación de documentos desde una fuente.
/// </summary>
public sealed class ImportarDocumentosResult
{
    /// <summary>IDs de <see cref="DataFile"/> creados exitosamente.</summary>
    public IReadOnlyList<int> DataFileIds { get; init; } = Array.Empty<int>();

    /// <summary>
    /// Advertencias por archivos que no pudieron procesarse (OCR falló, etc.).
    /// La importación parcial no es un error fatal: el estado del caso
    /// refleja la presencia de advertencias.
    /// </summary>
    public IReadOnlyList<string> Advertencias { get; init; } = Array.Empty<string>();
}
