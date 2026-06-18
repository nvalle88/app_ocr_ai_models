using app_ocr_ai_models.Data;
using app_ocr_ai_models.Services.Zendesk;
using app_tramites.Models.ModelAi;
using app_tramites.Models.ViewModel;

namespace app_ocr_ai_models.Services.Documents;

// ============================================================
// REQ-019 T22 — Adaptador Zendesk a IDocumentSourceProvider.
// Envuelve el IZendeskClient (T3) sin reimplementarlo.
// ============================================================

/// <summary>
/// Implementación de <see cref="IDocumentSourceProvider"/> que obtiene los documentos
/// del sobre desde la cuenta Zendesk correspondiente.
/// Adapta el <see cref="IZendeskClient"/> existente (T3) a la interfaz común.
/// </summary>
public sealed class ZendeskDocumentProvider : IDocumentSourceProvider
{
    private readonly IZendeskClient _zendesk;
    private readonly IOcrIngestService _ingest;
    private readonly ILogger<ZendeskDocumentProvider> _logger;

    /// <summary>
    /// Inicializa el proveedor con las dependencias de Zendesk e ingesta OCR.
    /// </summary>
    /// <param name="zendesk">Cliente Zendesk multi-cuenta (T3).</param>
    /// <param name="ingest">Servicio de ingesta OCR/Blob (T2).</param>
    /// <param name="logger">Logger de la aplicación.</param>
    public ZendeskDocumentProvider(
        IZendeskClient zendesk,
        IOcrIngestService ingest,
        ILogger<ZendeskDocumentProvider> logger)
    {
        _zendesk = zendesk ?? throw new ArgumentNullException(nameof(zendesk));
        _ingest  = ingest  ?? throw new ArgumentNullException(nameof(ingest));
        _logger  = logger  ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Para Zendesk, busca tickets por <see cref="SobreDocumentosFilter.NumeroSobre"/>
    /// y devuelve los nombres de los adjuntos de todos los tickets encontrados.
    /// </remarks>
    public async Task<IReadOnlyList<string>> ListarDocumentosAsync(
        SobreDocumentosFilter filter,
        CancellationToken ct = default)
    {
        var resultados = await _zendesk.BuscarPorNumeroSobreAsync(filter.NumeroSobre, ct).ConfigureAwait(false);

        var nombres = new List<string>();
        foreach (var grupo in resultados)
        {
            foreach (var item in grupo.Items)
            {
                var comentarios = await _zendesk.ObtenerComentariosAsync(item.Id, item.Cuenta, ct).ConfigureAwait(false);
                foreach (var comentario in comentarios)
                {
                    foreach (var adj in comentario.Attachments)
                    {
                        nombres.Add(adj.FileName);
                    }
                }
            }
        }

        return nombres;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Para Zendesk, descarga los adjuntos de todos los tickets encontrados por número de sobre,
    /// ejecuta OCR con <see cref="IOcrIngestService"/> y crea los <see cref="DataFile"/>.
    /// </remarks>
    public async Task<ImportarDocumentosResult> ImportarDocumentosAsync(
        SobreDocumentosFilter filter,
        ProcessCase caso,
        OCRDbContext db,
        CancellationToken ct = default)
    {
        var resultados = await _zendesk.BuscarPorNumeroSobreAsync(filter.NumeroSobre, ct).ConfigureAwait(false);

        var dataFileIds = new List<int>();
        var advertencias = new List<string>();

        foreach (var grupo in resultados)
        {
            foreach (var item in grupo.Items)
            {
                var comentarios = await _zendesk.ObtenerComentariosAsync(item.Id, item.Cuenta, ct).ConfigureAwait(false);

                foreach (var comentario in comentarios)
                {
                    foreach (var adj in comentario.Attachments)
                    {
                        try
                        {
                            var extension = string.IsNullOrEmpty(Path.GetExtension(adj.FileName))
                                ? ObtenerExtensionPorMime(adj.ContentType)
                                : Path.GetExtension(adj.FileName);

                            string fileUrl;
                            string ocrText;

                            try
                            {
                                await using var stream = await _zendesk.DescargarAdjuntoAsync(adj.ContentUrl, item.Cuenta, ct).ConfigureAwait(false);
                                using var ms = new MemoryStream();
                                await stream.CopyToAsync(ms, ct).ConfigureAwait(false);

                                var ocrFile = new OcrFile
                                {
                                    FileName  = adj.FileName,
                                    Content   = Convert.ToBase64String(ms.ToArray()),
                                    Extension = extension
                                };

                                (fileUrl, ocrText) = await _ingest.ProcessFileAsync(ocrFile).ConfigureAwait(false);
                            }
                            catch (Exception ocrEx)
                            {
                                _logger.LogWarning(ocrEx, "OCR falló para adjunto {FileName}; se sube solo el blob.", adj.FileName);
                                advertencias.Add($"OCR no disponible para '{adj.FileName}': {ocrEx.Message}");

                                await using var rawStream = await _zendesk.DescargarAdjuntoAsync(adj.ContentUrl, item.Cuenta, ct).ConfigureAwait(false);
                                fileUrl = await _ingest.UploadFileAsync(rawStream, extension).ConfigureAwait(false);
                                ocrText = string.Empty;
                            }

                            var dataFile = new DataFile
                            {
                                IsFileUri    = true,
                                FileUri      = fileUrl,
                                Text         = ocrText,
                                CaseCode     = caso.CaseCode,
                                CreatedDate  = DateTime.UtcNow,
                                OriginalName = adj.FileName
                            };
                            db.DataFile.Add(dataFile);
                            await db.SaveChangesAsync(ct).ConfigureAwait(false);
                            dataFileIds.Add(dataFile.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error al procesar adjunto {FileName} del ticket Zendesk #{TicketId}.", adj.FileName, item.Id);
                            advertencias.Add($"No se pudo procesar '{adj.FileName}': {ex.Message}");
                        }
                    }
                }
            }
        }

        return new ImportarDocumentosResult
        {
            DataFileIds  = dataFileIds,
            Advertencias = advertencias
        };
    }

    private static string ObtenerExtensionPorMime(string contentType) =>
        contentType?.ToLowerInvariant() switch
        {
            "application/pdf"  => ".pdf",
            "image/jpeg"       => ".jpg",
            "image/png"        => ".png",
            "image/tiff"       => ".tiff",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            "application/msword" => ".doc",
            _ => ".bin"
        };
}
