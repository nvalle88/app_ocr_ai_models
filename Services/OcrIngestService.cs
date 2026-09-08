using app_ocr_ai_models.Data;
using app_tramites.Extensions;
using app_tramites.Models.ModelAi;
using app_tramites.Models.ViewModel;
using app_tramites.Utils;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace app_ocr_ai_models.Services
{
    /// <summary>
    /// Implementación de <see cref="IOcrIngestService"/> que encapsula la lógica de
    /// subida a Azure Blob Storage y ejecución de OCR con Azure AI Document Intelligence.
    /// La configuración (OCRSetting y AzureBlobConf) se lee desde <see cref="OCRDbContext"/>.
    /// </summary>
    public class OcrIngestService : IOcrIngestService
    {
        private readonly OCRDbContext _db;
        private readonly FileDownloader _fileDownloader;

        /// <summary>
        /// Inicializa el servicio con el contexto de base de datos y el descargador de archivos.
        /// </summary>
        /// <param name="db">Contexto EF que provee OCRSetting y AzureBlobConf.</param>
        /// <param name="fileDownloader">Servicio auxiliar para descargar recursos remotos (usa IHttpClientFactory internamente).</param>
        public OcrIngestService(OCRDbContext db, FileDownloader fileDownloader)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _fileDownloader = fileDownloader ?? throw new ArgumentNullException(nameof(fileDownloader));
        }

        /// <inheritdoc/>
        /// <remarks>
        /// WRAPPER de <see cref="ProcessFileDetailedAsync"/>: se conserva sin cambios de
        /// firma ni de semántica para los callers existentes (SobresController,
        /// ArmonixDocumentProvider, ZendeskDocumentProvider y el legacy Nexus/OcrTest).
        /// El Text devuelto sigue siendo exactamente AnalyzeResult.Content.
        /// </remarks>
        public async Task<(string Url, string Text)> ProcessFileAsync(
            OcrFile file,
            int timeoutMilliseconds = 90000)
        {
            var resultado = await ProcessFileDetailedAsync(file, timeoutMilliseconds);
            return (Url: resultado.Url, Text: resultado.Text);
        }

        /// <inheritdoc/>
        public async Task<OcrResultado> ProcessFileDetailedAsync(
            OcrFile file,
            int timeoutMilliseconds = 90000)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));

            using var cts = new CancellationTokenSource(timeoutMilliseconds);

            var ocrSetting = await _db.OCRSetting
                .FirstOrDefaultAsync(x => x.SettingCode == "DEFAULT" && x.PlatformCode == "AZURE", cts.Token)
                ?? throw new InvalidOperationException("Configuración OCR 'DEFAULT/AZURE' no encontrada.");

            var blobCfg = await _db.AzureBlobConf.AsNoTracking().FirstOrDefaultAsync(cts.Token)
                ?? throw new InvalidOperationException("AzureBlobConf no encontrada.");

            try
            {
                // Los BYTES una sola vez: sirven para guardar el fichero en el
                // blob Y para el OCR, sin volver a bajarlo.
                var bytes = await ObtenerBytesAsync(file, cts.Token);
                var blobUrl = await SubirBytesAsync(bytes, file.Extension, blobCfg, cts.Token);

                var clientOcr = new DocumentIntelligenceClient(
                    new Uri(ocrSetting.Endpoint),
                    new AzureKeyCredential(ocrSetting.ApiKey!));

                // Se le manda el CONTENIDO a DocIntel, no la URL del blob.
                //
                // Con la URL, DocIntel tiene que ir a DESCARGAR el blob, y para
                // eso el contenedor debe ser publico o la URL venir firmada. En
                // pruebas el contenedor era publico y "colaba"; en produccion el
                // contenedor es PRIVADO -y debe serlo: son facturas medicas de
                // afiliados- asi que DocIntel no podia leerlo y el OCR salia
                // VACIO aunque la factura estuviera clara. Mandando los bytes no
                // hace falta exponer nada y funciona con el blob cerrado.
                var operation = await clientOcr.AnalyzeDocumentAsync(
                    WaitUntil.Completed,
                    ocrSetting.ModelId,
                    BinaryData.FromBytes(bytes),
                    cancellationToken: cts.Token);

                var analyze = operation.Value;

                // Text = AnalyzeResult.Content, TAL CUAL. No se recompone desde las
                // páginas: así el contrato del wrapper no cambia ni un carácter.
                var contenidoCompleto = analyze.Content ?? string.Empty;

                return new OcrResultado
                {
                    Url     = blobUrl,
                    Text    = contenidoCompleto,
                    Paginas = ExtraerPaginas(analyze, contenidoCompleto)
                };
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException(
                    $"El procesamiento del archivo superó el tiempo límite de {timeoutMilliseconds} ms.");
            }
        }

        /// <inheritdoc/>
        public async Task<string> UploadBlobAsync(OcrFile file)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));

            var blobCfg = await _db.AzureBlobConf.AsNoTracking().FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("AzureBlobConf no encontrada.");

            return await UploadBlobInternalAsync(file, blobCfg, CancellationToken.None);
        }

        /// <inheritdoc/>
        public async Task<string> UploadFileAsync(Stream fileStream, string extension)
        {
            if (fileStream == null) throw new ArgumentNullException(nameof(fileStream));

            var blobCfg = await _db.AzureBlobConf.AsNoTracking().FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("AzureBlobConf no encontrada.");

            ValidateBlobCfg(blobCfg);

            var blobServiceClient = new BlobServiceClient(blobCfg.ConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(blobCfg.ContainerName);
            await containerClient.CreateIfNotExistsAsync();

            var ext = NormalizeExtension(extension);
            var blobClient = containerClient.GetBlobClient($"{Guid.NewGuid()}{ext}");
            await blobClient.UploadAsync(fileStream, overwrite: true);

            return blobClient.Uri.ToString();
        }

        /// <inheritdoc/>
        public Stream Base64ToStream(string base64String)
        {
            if (string.IsNullOrWhiteSpace(base64String))
                throw new ArgumentNullException(nameof(base64String));

            return new MemoryStream(Convert.FromBase64String(base64String));
        }

        // ── Helpers privados ────────────────────────────────────────────────────

        /// <summary>
        /// Proyecta AnalyzeResult.Pages a <see cref="PaginaOcr"/>.
        /// NUNCA lanza: si el modelo no devolvió Pages retorna lista vacía, porque
        /// perder el desglose por página no debe tumbar la ingesta del documento.
        /// </summary>
        private static List<PaginaOcr> ExtraerPaginas(AnalyzeResult result, string contenidoCompleto)
        {
            var paginas = new List<PaginaOcr>();

            var pages = result?.Pages;
            if (pages == null || pages.Count == 0)
                return paginas;

            foreach (var page in pages)
            {
                var lineas = page.Lines;
                string texto;
                var desdeSpans = false;

                if (lineas != null && lineas.Count > 0)
                {
                    // ── VÍA PRINCIPAL ────────────────────────────────────────────
                    // DocumentLine.Content ya viene concatenado en orden de lectura;
                    // unir las líneas con '\n' reconstruye la página sin depender de
                    // la aritmética de offsets de DocumentSpan.
                    var sb = new StringBuilder(1024);
                    foreach (var linea in lineas)
                    {
                        if (string.IsNullOrEmpty(linea.Content))
                            continue;
                        if (sb.Length > 0)
                            sb.Append('\n');
                        sb.Append(linea.Content);
                    }
                    texto = sb.ToString();
                }
                else
                {
                    // ── FALLBACK ─────────────────────────────────────────────────
                    // Sin Lines: recortar el Content global con los DocumentSpan de la
                    // página. Los offsets están en las unidades del stringIndexType del
                    // servicio (que la sobrecarga 1.0.0 no permite fijar), así que se
                    // acotan los límites y se marca el origen del texto.
                    texto = RecortarPorSpans(contenidoCompleto, page.Spans);
                    desdeSpans = texto.Length > 0;

                    // Documento de una sola página: el Content global ES la página.
                    if (texto.Length == 0 && pages.Count == 1)
                        texto = contenidoCompleto;
                }

                paginas.Add(new PaginaOcr
                {
                    PageNumber      = page.PageNumber,
                    Text            = texto,
                    Width           = page.Width,
                    Height          = page.Height,
                    Unit            = page.Unit?.ToString(),
                    Angle           = page.Angle,
                    LineCount       = lineas?.Count ?? 0,
                    WordCount       = page.Words?.Count ?? 0,
                    TextoDesdeSpans = desdeSpans
                });
            }

            return paginas;
        }

        /// <summary>
        /// Recorta el Content global usando los spans indicados, acotando offset y
        /// longitud al tamaño real de la cadena (nunca lanza ArgumentOutOfRange).
        /// </summary>
        private static string RecortarPorSpans(string contenido, IReadOnlyList<DocumentSpan>? spans)
        {
            if (string.IsNullOrEmpty(contenido) || spans == null || spans.Count == 0)
                return string.Empty;

            var sb = new StringBuilder(256);

            foreach (var span in spans)   // DocumentSpan es struct: Offset/Length
            {
                var offset = span.Offset;
                var length = span.Length;

                if (offset < 0 || offset >= contenido.Length || length <= 0)
                    continue;

                if (offset + length > contenido.Length)
                    length = contenido.Length - offset;   // clamp

                sb.Append(contenido, offset, length);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Los bytes del archivo, vengan como Base64 (Content) o por URL. Una
        /// sola lectura para blob y OCR.
        /// </summary>
        private async Task<byte[]> ObtenerBytesAsync(OcrFile file, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(file.Content))
            {
                try { return Convert.FromBase64String(file.Content); }
                catch (FormatException) { throw new NegocioException("Content no es Base64 válido."); }
            }
            if (!string.IsNullOrWhiteSpace(file.Url))
            {
                await using var s = await _fileDownloader.DownloadUrlToMemoryStreamAsync(file.Url, ct);
                using var ms = new MemoryStream();
                await s.CopyToAsync(ms, ct);
                return ms.ToArray();
            }
            throw new NegocioException("Archivo sin Content ni Url.");
        }

        /// <summary>Sube bytes ya materializados al blob y devuelve su URL.</summary>
        private async Task<string> SubirBytesAsync(
            byte[] bytes, string? extension, AzureBlobConf blobCfg, CancellationToken ct)
        {
            ValidateBlobCfg(blobCfg);
            var blobServiceClient = new BlobServiceClient(blobCfg.ConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(blobCfg.ContainerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);
            var ext = NormalizeExtension(extension);
            var blobClient = containerClient.GetBlobClient($"{Guid.NewGuid()}{ext}");
            await using var ms = new MemoryStream(bytes, writable: false);
            await blobClient.UploadAsync(ms, overwrite: true, cancellationToken: ct);
            return blobClient.Uri.ToString();
        }

        private async Task<string> UploadBlobInternalAsync(
            OcrFile file,
            AzureBlobConf blobCfg,
            CancellationToken cancellationToken)
        {
            ValidateBlobCfg(blobCfg);

            var blobServiceClient = new BlobServiceClient(blobCfg.ConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(blobCfg.ContainerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            var ext = NormalizeExtension(file.Extension);
            var blobClient = containerClient.GetBlobClient($"{Guid.NewGuid()}{ext}");

            if (!string.IsNullOrWhiteSpace(file.Content))
            {
                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(file.Content);
                }
                catch (FormatException)
                {
                    throw new NegocioException("Content no es Base64 válido.");
                }

                await using var ms = new MemoryStream(bytes, writable: false);
                await blobClient.UploadAsync(ms, overwrite: true, cancellationToken: cancellationToken);
                return blobClient.Uri.ToString();
            }

            if (!string.IsNullOrWhiteSpace(file.Url))
            {
                await using var remoteStream = await _fileDownloader.DownloadUrlToMemoryStreamAsync(
                    file.Url, cancellationToken);
                await blobClient.UploadAsync(remoteStream, overwrite: true, cancellationToken: cancellationToken);
                return blobClient.Uri.ToString();
            }

            throw new NegocioException("Archivo sin Content ni Url.");
        }

        private static void ValidateBlobCfg(AzureBlobConf blobCfg)
        {
            if (string.IsNullOrWhiteSpace(blobCfg.ConnectionString))
                throw new InvalidOperationException("AzureBlobConf.ConnectionString vacío.");
            if (string.IsNullOrWhiteSpace(blobCfg.ContainerName))
                throw new InvalidOperationException("AzureBlobConf.ContainerName vacío.");
        }

        private static string NormalizeExtension(string? extension)
        {
            var ext = (extension ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(ext) && !ext.StartsWith('.'))
                ext = "." + ext;
            return ext;
        }
    }
}
