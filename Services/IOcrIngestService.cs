using app_tramites.Models.ViewModel;

namespace app_ocr_ai_models.Services
{
    /// <summary>
    /// Servicio de ingesta de documentos: sube archivos a Azure Blob Storage
    /// y ejecuta OCR con Azure AI Document Intelligence.
    /// Diseñado para ser consumido por el Área Studio (motor nuevo) sin afectar el flujo legacy.
    /// </summary>
    public interface IOcrIngestService
    {
        /// <summary>
        /// Sube un archivo (desde base64 o URL) a Blob Storage y ejecuta OCR sobre él.
        /// Devuelve la URL pública del blob y el texto extraído por OCR.
        /// </summary>
        /// <param name="file">Archivo con contenido base64 o URL remota, más extensión.</param>
        /// <param name="timeoutMilliseconds">Tiempo máximo de espera para la operación completa.</param>
        /// <returns>Tupla con la URL pública del blob y el texto OCR extraído.</returns>
        Task<(string Url, string Text)> ProcessFileAsync(
            OcrFile file,
            int timeoutMilliseconds = 90000);

        /// <summary>
        /// Sube un archivo (desde base64 o URL) a Blob Storage y devuelve su URL pública.
        /// </summary>
        /// <param name="file">Archivo con contenido base64 o URL remota, más extensión.</param>
        /// <returns>URL pública del blob creado.</returns>
        Task<string> UploadBlobAsync(OcrFile file);

        /// <summary>
        /// Sube un stream a Blob Storage y devuelve su URL pública.
        /// </summary>
        /// <param name="fileStream">Stream del archivo a subir.</param>
        /// <param name="extension">Extensión del archivo (p. ej. ".pdf").</param>
        /// <returns>URL pública del blob creado.</returns>
        Task<string> UploadFileAsync(Stream fileStream, string extension);

        /// <summary>
        /// Convierte una cadena base64 a un <see cref="Stream"/>.
        /// </summary>
        /// <param name="base64String">Cadena en formato base64.</param>
        /// <returns>Stream con los bytes decodificados.</returns>
        Stream Base64ToStream(string base64String);
    }
}
