using app_tramites.Models.ViewModel;

namespace app_ocr_ai_models.Services.Anexos;

// ============================================================
// REQ-046 — Ingesta de anexos: subir → OCR → Claude estructura → guardar en BD.
// Las tools del agente auditor leen luego de esas tablas (no de M-Files).
// ============================================================

/// <summary>Servicio que convierte el PDF de un contrato/anexo en filas estructuradas
/// (contrato, anexo, coberturas, carencias, exclusiones, cláusulas) usando OCR + Claude.</summary>
public interface IAnexoIngestService
{
    /// <summary>
    /// Sube el archivo, ejecuta OCR, pide a Claude la estructura y la persiste en la
    /// biblioteca de anexos. Deja traza en <c>AnexoIngesta</c>.
    /// </summary>
    /// <param name="file">Archivo (base64 + extensión) del contrato o anexo.</param>
    /// <param name="tipoContratoHint">Tipo sugerido por el operador (Individual, Oncologico…), usado si el OCR no lo trae.</param>
    /// <param name="codigoPlanHint">Código de plan sugerido (opcional), usado si el OCR no lo trae.</param>
    /// <param name="usuario">Usuario que carga (para la traza).</param>
    /// <param name="ct">Token de cancelación.</param>
    Task<AnexoIngestaResult> CargarYEstructurarAsync(
        OcrFile file,
        string? tipoContratoHint,
        string? codigoPlanHint,
        string? usuario,
        CancellationToken ct = default);
}

/// <summary>Resultado de una ingesta: qué se guardó y advertencias.</summary>
public sealed class AnexoIngestaResult
{
    public int IngestaId { get; set; }
    public int? ContratoId { get; set; }
    public List<int> AnexoIds { get; set; } = new();
    public int Coberturas { get; set; }
    public int Carencias { get; set; }
    public int Exclusiones { get; set; }
    public int Clausulas { get; set; }
    public string Estado { get; set; } = "RECIBIDO";
    public string? Json { get; set; }
    public List<string> Advertencias { get; set; } = new();
}
