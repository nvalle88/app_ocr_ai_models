using System.Text;
using System.Text.Json;
using app_ocr_ai_models.Data;
using app_tramites.Models.ModelAi;
using app_tramites.Models.ViewModel;
using app_tramites.Services.Ai.Tools;
using Microsoft.Data.SqlClient;

namespace app_ocr_ai_models.Services.Documents;

// ============================================================
// REQ-019 T22 — Proveedor documental Armonix (REESCRITO al flujo real y PROBADO).
//
// Antes llamaba a endpoints /api/sobres/* de api-armonix apuntando a un
// ambiente donde el sobre no existía → "no busca en nada".
//
// Flujo correcto (validado en vivo con el sobre NA-2612551):
//   1) Sobre         → SQL directo a bdd_Salud_Consultas.dbo.Sobre (por NumeroSobre).
//   2) Documentos    → M-Files (ServicioGestionDocumentos):
//        buscar    POST {base}/Objetos/Busqueda?idClase=60  body [{Codigo:1095, Valor:NumeroSobre}]
//        descargar POST {base}/Archivos/Descarga?idClase=60 body [{Codigo:0,   Valor:Nombre}]
//   Auth vía ISaludsaTokenProvider (OAuth2 password grant + cabeceras Saludsa).
//
// Config:
//   ConnectionStrings:SaludConsultas         → cadena a bdd_Salud_Consultas (salud37 pruebas / salud34 prod)
//   Saludsa:BaseUrls:GestionDocumentos       → base del ServicioGestionDocumentos (incluye /api)
//   Saludsa:MFiles:IdClaseDocumentos         → idClase de búsqueda (default 60)
// ============================================================

/// <summary>
/// Implementación de <see cref="IDocumentSourceProvider"/> que resuelve el sobre
/// consultando directamente <c>bdd_Salud_Consultas.dbo.Sobre</c> y obtiene sus
/// documentos escaneados desde M-Files (ServicioGestionDocumentos).
/// </summary>
public sealed class ArmonixDocumentProvider : IDocumentSourceProvider
{
    // Códigos de metadato M-Files (de MFilesConstants de api-armonix)
    private const int CODIGO_NUMERO_SOBRE   = 1095; // filtro de búsqueda por sobre
    private const int CODIGO_BUSQUEDA_ARCHIVO = 0;   // filtro de descarga por nombre de archivo
    private const int DEFAULT_ID_CLASE_DOCS = 60;    // clase "Sobres-Reembolso-Electronico" (pruebas y prod)

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISaludsaTokenProvider _tokenProvider;
    private readonly IOcrIngestService _ingest;
    private readonly IConfiguration _config;
    private readonly ILogger<ArmonixDocumentProvider> _logger;

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>Inicializa el proveedor con sus dependencias.</summary>
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

    // ── BuscarSobres — SQL directo a bdd_Salud_Consultas ──────────────────

    /// <summary>
    /// Resuelve el/los sobre(s) consultando <c>bdd_Salud_Consultas.dbo.Sobre</c>.
    /// La búsqueda por número de sobre es exacta; devuelve los identificadores
    /// de contrato para la trazabilidad (M-Files solo requiere el número de sobre).
    /// </summary>
    /// <param name="numeroSobre">Número del sobre (p. ej. "NA-2612551").</param>
    /// <param name="cedula">Cédula del afiliado (pendiente: por ahora se busca por número de sobre).</param>
    /// <param name="ct">Token de cancelación.</param>
    public async Task<IReadOnlyList<ArmonixSobreResueltoDto>> BuscarSobresAsync(
        string? numeroSobre,
        string? cedula,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(numeroSobre) && string.IsNullOrWhiteSpace(cedula))
            throw new ArgumentException("Debe informar al menos el número de sobre o la cédula.", nameof(numeroSobre));

        // La búsqueda por cédula requiere resolver persona→contrato→sobres en la BD
        // Progress (fuera de bdd_Salud_Consultas). Se implementará en una iteración
        // posterior; por ahora se guía al operador a usar el número de sobre.
        if (string.IsNullOrWhiteSpace(numeroSobre))
            throw new ArgumentException(
                "La búsqueda por cédula estará disponible próximamente. " +
                "Por ahora ingrese el número de sobre (p. ej. NA-2612551).");

        var connStr = ResolveSaludConsultasConnectionString();
        var sobre   = numeroSobre.Trim();

        var resultados = new List<ArmonixSobreResueltoDto>();

        const string sql = @"
            SELECT TOP 20
                   s.IdSobre, s.NumeroSobre, s.IdEstadoSobre, s.NumeroContrato,
                   s.CodigoRegion, s.CodigoProducto, s.ValorPresentado,
                   s.PersonaContacto, s.FechaRecepcion
            FROM   dbo.Sobre s WITH (NOLOCK)
            WHERE  s.NumeroSobre = @numeroSobre
            ORDER BY s.IdSobre DESC;";

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@numeroSobre", System.Data.SqlDbType.VarChar, 50) { Value = sobre });

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var numContrato   = reader["NumeroContrato"]  == DBNull.Value ? (int?)null : Convert.ToInt32(reader["NumeroContrato"]);
            var idEstado      = reader["IdEstadoSobre"]   == DBNull.Value ? 0          : Convert.ToInt32(reader["IdEstadoSobre"]);
            var fechaRecep    = reader["FechaRecepcion"]  == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["FechaRecepcion"]);
            var valor         = reader["ValorPresentado"] == DBNull.Value ? 0m         : Convert.ToDecimal(reader["ValorPresentado"]);

            resultados.Add(new ArmonixSobreResueltoDto
            {
                NumeroSobre           = reader["NumeroSobre"]?.ToString()    ?? sobre,
                CodigoRegion          = reader["CodigoRegion"]?.ToString()   ?? string.Empty,
                CodigoProducto        = reader["CodigoProducto"]?.ToString() ?? string.Empty,
                NumeroContrato        = numContrato?.ToString()             ?? string.Empty,
                NumeroPersonaPaciente = string.Empty, // M-Files no lo requiere para el filtro por sobre
                NombreTitular         = reader["PersonaContacto"]?.ToString() ?? string.Empty,
                EstadoSobre           = $"Estado {idEstado} · ${valor:N2}",
                FechaRecepcion        = fechaRecep
            });
        }

        return resultados;
    }

    // ── ListarDocumentos — M-Files /Objetos/Busqueda ──────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// Busca en M-Files los objetos del sobre (idClase=60, filtrando SOLO por
    /// número de sobre — agregar región/producto/contrato hace fallar la búsqueda)
    /// y devuelve los nombres de los documentos, sin descargar el binario.
    /// </remarks>
    public async Task<IReadOnlyList<string>> ListarDocumentosAsync(
        SobreDocumentosFilter filter,
        CancellationToken ct = default)
    {
        ValidarNumeroSobre(filter);
        var objetos = await BuscarObjetosMFilesAsync(filter.NumeroSobre.Trim(), ct).ConfigureAwait(false);
        return objetos
            .Select(o => o.Nombre ?? string.Empty)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();
    }

    // ── ImportarDocumentos — M-Files /Archivos/Descarga → OCR → DataFile ──

    /// <inheritdoc />
    /// <remarks>
    /// Descarga cada documento del sobre desde M-Files (Base64), ejecuta OCR con
    /// <see cref="IOcrIngestService"/> y crea los <see cref="DataFile"/> en el caso.
    /// </remarks>
    public async Task<ImportarDocumentosResult> ImportarDocumentosAsync(
        SobreDocumentosFilter filter,
        ProcessCase caso,
        OCRDbContext db,
        CancellationToken ct = default)
    {
        ValidarNumeroSobre(filter);
        var numeroSobre = filter.NumeroSobre.Trim();

        // 1) Listar objetos (nombre + extensión) del sobre en M-Files
        var objetos = await BuscarObjetosMFilesAsync(numeroSobre, ct).ConfigureAwait(false);

        var dataFileIds  = new List<int>();
        var advertencias = new List<string>();

        if (objetos.Count == 0)
            advertencias.Add($"No se encontraron documentos en M-Files para el sobre '{numeroSobre}'.");

        // 2) Descargar → OCR → DataFile por cada documento
        foreach (var obj in objetos)
        {
            var nombre = obj.Nombre;
            if (string.IsNullOrWhiteSpace(nombre))
                continue;

            try
            {
                var extension = NormalizarExtension(obj.Archivos?.FirstOrDefault()?.Extension);

                var contenidoB64 = await DescargarDocumentoMFilesAsync(nombre, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(contenidoB64))
                {
                    advertencias.Add($"El documento '{nombre}' llegó sin contenido desde M-Files.");
                    continue;
                }

                var fileName = nombre.Contains('.') ? nombre : nombre + extension;

                string fileUrl;
                string ocrText;
                try
                {
                    var ocrFile = new OcrFile
                    {
                        FileName  = fileName,
                        Content   = contenidoB64,
                        Extension = extension
                    };
                    (fileUrl, ocrText) = await _ingest.ProcessFileAsync(ocrFile).ConfigureAwait(false);
                }
                catch (Exception ocrEx)
                {
                    _logger.LogWarning(ocrEx, "[T22] OCR falló para documento M-Files {NombreDoc}; se sube solo el blob.", nombre);
                    advertencias.Add($"OCR no disponible para '{nombre}': {ocrEx.Message}");

                    var bytes = Convert.FromBase64String(contenidoB64);
                    using var ms = new MemoryStream(bytes);
                    fileUrl = await _ingest.UploadFileAsync(ms, extension).ConfigureAwait(false);
                    ocrText = string.Empty;
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
                _logger.LogWarning(ex, "[T22] Error al procesar documento M-Files {NombreDoc}.", nombre);
                advertencias.Add($"No se pudo procesar '{nombre}': {ex.Message}");
            }
        }

        return new ImportarDocumentosResult
        {
            DataFileIds  = dataFileIds,
            Advertencias = advertencias
        };
    }

    // ── Helpers M-Files ───────────────────────────────────────────────────

    /// <summary>
    /// Llama a <c>POST {base}/Objetos/Busqueda?idClase={idClase}</c> filtrando SOLO por
    /// número de sobre (Codigo 1095). Devuelve los objetos documentales encontrados.
    /// </summary>
    private async Task<List<MFilesObjeto>> BuscarObjetosMFilesAsync(string numeroSobre, CancellationToken ct)
    {
        var (baseUrl, idClase) = ResolveMFilesConfig();
        var criterios = new[]
        {
            new MFilesValor { Codigo = CODIGO_NUMERO_SOBRE, Valor = numeroSobre }
        };

        var envelope = await PostMFilesAsync<List<MFilesObjeto>>(
            $"{baseUrl}/Objetos/Busqueda?idClase={idClase}", criterios, ct).ConfigureAwait(false);

        if (!EsOk(envelope))
        {
            // "No existen resultados de búsqueda" es un caso normal (0 documentos), no un error fatal.
            _logger.LogInformation("[T22] M-Files Busqueda sobre {Sobre}: {Mensaje}",
                numeroSobre, DescribirMensajes(envelope));
            return new List<MFilesObjeto>();
        }

        return envelope!.Datos ?? new List<MFilesObjeto>();
    }

    /// <summary>
    /// Llama a <c>POST {base}/Archivos/Descarga?idClase={idClase}</c> con el nombre del
    /// archivo (Codigo 0) y devuelve el contenido en Base64.
    /// </summary>
    private async Task<string?> DescargarDocumentoMFilesAsync(string nombreArchivo, CancellationToken ct)
    {
        var (baseUrl, idClase) = ResolveMFilesConfig();
        var criterios = new[]
        {
            new MFilesValor { Codigo = CODIGO_BUSQUEDA_ARCHIVO, Valor = nombreArchivo }
        };

        var envelope = await PostMFilesAsync<MFilesContenido>(
            $"{baseUrl}/Archivos/Descarga?idClase={idClase}", criterios, ct).ConfigureAwait(false);

        if (!EsOk(envelope))
            throw new HttpRequestException($"[T22] M-Files Descarga de '{nombreArchivo}' falló: {DescribirMensajes(envelope)}");

        return envelope!.Datos?.Contenido;
    }

    /// <summary>
    /// POST genérico al ServicioGestionDocumentos con las cabeceras de autenticación Saludsa
    /// y el cuerpo JSON de criterios. Deserializa la envoltura <see cref="MFilesEnvelope{T}"/>.
    /// </summary>
    private async Task<MFilesEnvelope<T>?> PostMFilesAsync<T>(
        string url, IReadOnlyList<MFilesValor> criterios, CancellationToken ct)
    {
        var authHeaders = await _tokenProvider.GetAuthHeadersAsync(ct).ConfigureAwait(false);

        var json    = JsonSerializer.Serialize(criterios);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var http = _httpClientFactory.CreateClient("SaludsaInternalApi");
        using var msg  = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        foreach (var (name, value) in authHeaders)
            msg.Headers.TryAddWithoutValidation(name, value);

        using var response = await http.SendAsync(msg, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"[T22] ServicioGestionDocumentos respondió {(int)response.StatusCode} en {url}: {body}",
                null, response.StatusCode);

        return JsonSerializer.Deserialize<MFilesEnvelope<T>>(body, JsonOptions);
    }

    private static bool EsOk<T>(MFilesEnvelope<T>? e) =>
        e != null && string.Equals(e.Estado, "OK", StringComparison.OrdinalIgnoreCase);

    private static string DescribirMensajes<T>(MFilesEnvelope<T>? e) =>
        e?.Mensajes is { Count: > 0 } m ? string.Join("; ", m) : (e?.Estado ?? "sin respuesta");

    // ── Helpers de configuración / validación ─────────────────────────────

    /// <summary>Resuelve la cadena de conexión a bdd_Salud_Consultas (B1).</summary>
    private string ResolveSaludConsultasConnectionString()
    {
        var connStr = _config.GetConnectionString("SaludConsultas");
        if (string.IsNullOrWhiteSpace(connStr))
            throw new InvalidOperationException(
                "[T22 B1] La cadena de conexión 'ConnectionStrings:SaludConsultas' no está configurada " +
                "(bdd_Salud_Consultas). Configúrela en appsettings / Key Vault.");
        return connStr;
    }

    /// <summary>Resuelve la base del ServicioGestionDocumentos (M-Files) y el idClase (B1).</summary>
    private (string BaseUrl, int IdClase) ResolveMFilesConfig()
    {
        const string configKey = "Saludsa:BaseUrls:GestionDocumentos";
        var baseUrl = _config[configKey];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException(
                $"[T22 B1] La base del ServicioGestionDocumentos no está configurada. " +
                $"Configure '{configKey}' en appsettings / Key Vault.");

        var idClase = int.TryParse(_config["Saludsa:MFiles:IdClaseDocumentos"], out var v) && v > 0
            ? v
            : DEFAULT_ID_CLASE_DOCS;

        return (baseUrl.TrimEnd('/'), idClase);
    }

    private static void ValidarNumeroSobre(SobreDocumentosFilter filter)
    {
        if (string.IsNullOrWhiteSpace(filter.NumeroSobre))
            throw new ArgumentException("NumeroSobre es obligatorio para consultar M-Files.", nameof(filter));
    }

    private static string NormalizarExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return ".pdf"; // los sobres de reembolso electrónico son PDF
        var ext = extension.Trim().TrimStart('.');
        return "." + ext.ToLowerInvariant();
    }
}
