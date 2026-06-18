using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using app_ocr_ai_models.Data;
using app_tramites.Models.ModelAi;
using app_tramites.Models.ViewModel;
using app_tramites.Services.Ai.Tools;

namespace app_ocr_ai_models.Services.Documents;

// ============================================================
// REQ-019 T22 — Proveedor documental Armonix (NUEVO).
// Llama a /api/sobres/BuscarDocumentos (listar) y
// /api/sobres/BuscarDocumentosCompleto (descargar base64).
// Auth vía ISaludsaTokenProvider (OAuth2, patrón T5).
// LIVE gated por B1/B2 (egress + OAuth2 a api-armonix).
// ============================================================

/// <summary>
/// Implementación de <see cref="IDocumentSourceProvider"/> que obtiene los documentos
/// del sobre desde la API de Armonix (MFiles).
/// Usa <see cref="ISaludsaTokenProvider"/> para la autenticación OAuth2 y
/// <see cref="IOcrIngestService"/> para Blob + OCR.
/// </summary>
/// <remarks>
/// La <c>baseUrl</c> de api-armonix se lee de la configuración
/// (clave <c>Saludsa:BaseUrls:ApiArmonix</c>). Si no está configurada, falla
/// en runtime con mensaje claro (B1/T0a). No hay tráfico de red si la clave
/// está ausente.
/// </remarks>
public sealed class ArmonixDocumentProvider : IDocumentSourceProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISaludsaTokenProvider _tokenProvider;
    private readonly IOcrIngestService _ingest;
    private readonly IConfiguration _config;
    private readonly ILogger<ArmonixDocumentProvider> _logger;

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Inicializa el proveedor con las dependencias necesarias.
    /// </summary>
    /// <param name="httpClientFactory">Factory de HttpClient para llamadas a Armonix.</param>
    /// <param name="tokenProvider">Proveedor de token OAuth2 Saludsa (B2).</param>
    /// <param name="ingest">Servicio de ingesta OCR/Blob (T2).</param>
    /// <param name="config">Configuración de la aplicación (resolución de baseUrl B1).</param>
    /// <param name="logger">Logger de la aplicación.</param>
    public ArmonixDocumentProvider(
        IHttpClientFactory httpClientFactory,
        ISaludsaTokenProvider tokenProvider,
        IOcrIngestService ingest,
        IConfiguration config,
        ILogger<ArmonixDocumentProvider> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _tokenProvider     = tokenProvider     ?? throw new ArgumentNullException(nameof(tokenProvider));
        _ingest            = ingest            ?? throw new ArgumentNullException(nameof(ingest));
        _config            = config            ?? throw new ArgumentNullException(nameof(config));
        _logger            = logger            ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Llama a <c>POST /api/sobres/BuscarDocumentos</c> de api-armonix,
    /// que devuelve <c>List&lt;string&gt;</c> con los nombres/IDs de los documentos en MFiles.
    /// Requiere todos los campos del <paramref name="filter"/> (CodigoProducto,
    /// CodigoRegion, NumeroContrato, NumeroSobre, NumeroPersonaPaciente).
    /// </remarks>
    public async Task<IReadOnlyList<string>> ListarDocumentosAsync(
        SobreDocumentosFilter filter,
        CancellationToken ct = default)
    {
        ValidarIdentificadores(filter);

        var baseUrl = ResolveBaseUrl();
        var authHeaders = await _tokenProvider.GetAuthHeadersAsync(ct).ConfigureAwait(false);
        var requestBody = BuildRequest(filter);

        using var http = _httpClientFactory.CreateClient("SaludsaInternalApi");
        using var msg = BuildHttpMessage(baseUrl, "/api/sobres/BuscarDocumentos", requestBody);

        foreach (var (name, value) in authHeaders)
            msg.Headers.TryAddWithoutValidation(name, value);

        using var response = await http.SendAsync(msg, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"[T22] Armonix BuscarDocumentos respondió {(int)response.StatusCode}: {body}",
                null, response.StatusCode);

        var lista = JsonSerializer.Deserialize<List<string>>(body, JsonOptions);
        return (IReadOnlyList<string>?)lista ?? Array.Empty<string>();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Llama a <c>POST /api/sobres/BuscarDocumentosCompleto</c> de api-armonix,
    /// que devuelve <c>List&lt;RespuestaMFileShift&gt;</c> con el contenido binario
    /// en Base64. Decodifica el Base64, ejecuta OCR con <see cref="IOcrIngestService"/>
    /// y crea cada <see cref="DataFile"/> en el <paramref name="caso"/>.
    /// </remarks>
    public async Task<ImportarDocumentosResult> ImportarDocumentosAsync(
        SobreDocumentosFilter filter,
        ProcessCase caso,
        OCRDbContext db,
        CancellationToken ct = default)
    {
        ValidarIdentificadores(filter);

        var baseUrl = ResolveBaseUrl();
        var authHeaders = await _tokenProvider.GetAuthHeadersAsync(ct).ConfigureAwait(false);
        var requestBody = BuildRequest(filter);

        // ── 1. Llamar a Armonix para obtener los documentos con contenido base64
        List<ArmonixDocumentoDto> documentos;
        try
        {
            using var http = _httpClientFactory.CreateClient("SaludsaInternalApi");
            using var msg = BuildHttpMessage(baseUrl, "/api/sobres/BuscarDocumentosCompleto", requestBody);
            foreach (var (name, value) in authHeaders)
                msg.Headers.TryAddWithoutValidation(name, value);

            using var response = await http.SendAsync(msg, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"[T22] Armonix BuscarDocumentosCompleto respondió {(int)response.StatusCode}: {body}",
                    null, response.StatusCode);

            documentos = JsonSerializer.Deserialize<List<ArmonixDocumentoDto>>(body, JsonOptions)
                ?? new List<ArmonixDocumentoDto>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[T22] Error al llamar a Armonix BuscarDocumentosCompleto para sobre {NumeroSobre}.", filter.NumeroSobre);
            throw;
        }

        // ── 2. Para cada documento: decodificar base64 → OCR → DataFile
        var dataFileIds  = new List<int>();
        var advertencias = new List<string>();

        foreach (var doc in documentos)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(doc.Contenido))
                {
                    advertencias.Add($"El documento '{doc.Nombre}' llegó sin contenido (Contenido vacío).");
                    continue;
                }

                var extension = NormalizarExtension(doc.Extension);
                var fileName  = string.IsNullOrWhiteSpace(doc.Nombre)
                    ? $"documento{extension}"
                    : doc.Nombre.Contains('.') ? doc.Nombre : doc.Nombre + extension;

                string fileUrl;
                string ocrText;

                try
                {
                    var ocrFile = new OcrFile
                    {
                        FileName  = fileName,
                        Content   = doc.Contenido,
                        Extension = extension
                    };

                    (fileUrl, ocrText) = await _ingest.ProcessFileAsync(ocrFile).ConfigureAwait(false);
                }
                catch (Exception ocrEx)
                {
                    _logger.LogWarning(ocrEx, "[T22] OCR falló para documento Armonix {NombreDoc}; se sube solo el blob.", doc.Nombre);
                    advertencias.Add($"OCR no disponible para '{doc.Nombre}': {ocrEx.Message}");

                    // Fallback: subir el stream sin OCR
                    var bytes = Convert.FromBase64String(doc.Contenido);
                    using var ms = new MemoryStream(bytes);
                    fileUrl  = await _ingest.UploadFileAsync(ms, extension).ConfigureAwait(false);
                    ocrText  = string.Empty;
                }

                var dataFile = new DataFile
                {
                    IsFileUri    = true,
                    FileUri      = fileUrl,
                    Text         = ocrText,
                    CaseCode     = caso.CaseCode,
                    CreatedDate  = DateTime.UtcNow,
                    OriginalName = fileName
                };
                db.DataFile.Add(dataFile);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                dataFileIds.Add(dataFile.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[T22] Error al procesar documento Armonix {NombreDoc}.", doc.Nombre);
                advertencias.Add($"No se pudo procesar '{doc.Nombre}': {ex.Message}");
            }
        }

        return new ImportarDocumentosResult
        {
            DataFileIds  = dataFileIds,
            Advertencias = advertencias
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Resuelve la baseUrl de api-armonix desde la configuración.
    /// Falla con <see cref="InvalidOperationException"/> si no está configurada (B1/T0a).
    /// </summary>
    private string ResolveBaseUrl()
    {
        const string configKey = "Saludsa:BaseUrls:ApiArmonix";
        var url = _config[configKey];
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException(
                $"[T22 B1] La baseUrl de api-armonix no está configurada. " +
                $"Configure '{configKey}' en appsettings / Key Vault (B1/T0a).");

        return url.TrimEnd('/');
    }

    private static ArmonixSobreRequest BuildRequest(SobreDocumentosFilter filter) =>
        new()
        {
            CodigoProducto      = filter.CodigoProducto      ?? string.Empty,
            CodigoRegion        = filter.CodigoRegion        ?? string.Empty,
            NumeroContrato      = filter.NumeroContrato      ?? string.Empty,
            NumeroSobre         = filter.NumeroSobre,
            NumeroPersonaPaciente = filter.NumeroPersonaPaciente ?? string.Empty
        };

    private static HttpRequestMessage BuildHttpMessage(
        string baseUrl,
        string path,
        ArmonixSobreRequest body)
    {
        var json    = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return new HttpRequestMessage(HttpMethod.Post, baseUrl + path)
        {
            Content = content
        };
    }

    /// <summary>
    /// Valida que el filtro tenga todos los campos requeridos por Armonix.
    /// Si el usuario solo tiene cédula debe resolver primero el contrato
    /// con la tool <c>resolver_contrato_por_cedula</c>.
    /// </summary>
    private static void ValidarIdentificadores(SobreDocumentosFilter filter)
    {
        if (string.IsNullOrWhiteSpace(filter.NumeroSobre))
            throw new ArgumentException("NumeroSobre es obligatorio para Armonix.", nameof(filter));

        // Armonix requiere los cuatro identificadores de contrato.
        // Si faltan, el operador debe resolver primero el contrato.
        if (string.IsNullOrWhiteSpace(filter.NumeroContrato)
            || string.IsNullOrWhiteSpace(filter.CodigoProducto)
            || string.IsNullOrWhiteSpace(filter.CodigoRegion)
            || string.IsNullOrWhiteSpace(filter.NumeroPersonaPaciente))
        {
            throw new ArgumentException(
                "Armonix requiere NumeroContrato, CodigoProducto, CodigoRegion y NumeroPersonaPaciente. " +
                "Si solo dispone de la cédula, resuelva el contrato primero " +
                "con la tool 'resolver_contrato_por_cedula'.",
                nameof(filter));
        }
    }

    private static string NormalizarExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return ".bin";

        var ext = extension.Trim().TrimStart('.');
        return "." + ext.ToLowerInvariant();
    }
}
