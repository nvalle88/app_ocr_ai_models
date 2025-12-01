using System.Net;
using app_tramites.Extensions;

namespace app_tramites.Utils
{
    /// <summary>
    /// Servicio auxiliar para descargar un recurso remoto y devolver su contenido como MemoryStream.
    /// Diseñado para inyección vía IHttpClientFactory.
    /// </summary>
    public class FileDownloader
    {
        private readonly IHttpClientFactory _httpClientFactory;

        /// <summary>
        /// Tamaño máximo de descarga permitido en bytes (predeterminado 50 MB).
        /// </summary>
        public long MaxDownloadBytes { get; init; } = 50 * 1024 * 1024;

        public FileDownloader(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        /// <summary>
        /// Descarga el recurso en <paramref name="url"/> y devuelve su contenido como un MemoryStream.
        /// El flujo devuelto se posiciona en 0. El usuario debe desecharlo.
        /// Throws<see cref="NegocioException"/> para errores de validación o dominio.
        /// </summary>
        public async Task<MemoryStream> DownloadUrlToMemoryStreamAsync(string url, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new NegocioException("El parámetro 'url' es requerido.");

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new NegocioException("La URL proporcionada no es válida.");

            if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new NegocioException("Solo se permiten esquemas http/https.");
            }

            var client = _httpClientFactory.CreateClient("FileDownloaderClient");

            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new NegocioException("Recurso no encontrado (404).");

            if (!response.IsSuccessStatusCode)
                throw new NegocioException($"Error al descargar el recurso: {(int)response.StatusCode} {response.ReasonPhrase}");

            if (response.Content.Headers.ContentLength.HasValue)
            {
                var length = response.Content.Headers.ContentLength.Value;
                if (length > MaxDownloadBytes)
                    throw new NegocioException($"El archivo es demasiado grande ({length} bytes). Límite: {MaxDownloadBytes} bytes.");
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var ms = new MemoryStream();
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                total += read;
                if (total > MaxDownloadBytes)
                {
                    await ms.DisposeAsync();
                    throw new NegocioException($"El archivo excede el límite permitido de {MaxDownloadBytes} bytes.");
                }
                ms.Write(buffer, 0, read);
            }

            ms.Position = 0;
            return ms;
        }
    }
}