using app_ocr_ai_models.Data;
using app_tramites.Extensions;
using app_tramites.Models.ModelAi;
using app_tramites.Models.ViewModel;
using app_tramites.Utils;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;

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
        public async Task<(string Url, string Text)> ProcessFileAsync(
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
                var blobUrl = await UploadBlobInternalAsync(file, blobCfg, cts.Token);

                var clientOcr = new DocumentIntelligenceClient(
                    new Uri(ocrSetting.Endpoint),
                    new AzureKeyCredential(ocrSetting.ApiKey!));

                var operation = await clientOcr.AnalyzeDocumentAsync(
                    WaitUntil.Completed,
                    ocrSetting.ModelId,
                    new Uri(blobUrl),
                    cancellationToken: cts.Token);

                return (Url: blobUrl, Text: operation.Value.Content ?? string.Empty);
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
