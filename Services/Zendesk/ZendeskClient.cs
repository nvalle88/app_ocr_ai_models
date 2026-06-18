using app_ocr_ai_models.Data;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Text.Json;

namespace app_ocr_ai_models.Services.Zendesk;

// ============================================================
// REQ-019 T3 — Implementación del cliente Zendesk multi-cuenta.
// Usa IHttpClientFactory.
// Los tokens se leen desde ZendeskConf.SecretRef; en entorno sin
// Key Vault el campo puede contener el token directamente
// (placeholder). No se hardcodea ningún secreto.
// ============================================================

/// <summary>
/// Custom field IDs usados en la Search API de Zendesk.
/// Referencia: api-comunicacion DataConfig (NumeroSobre=360029154411).
/// </summary>
file static class ZdFields
{
    internal const long NumeroSobre = 360029154411L;
    internal const long CedulaBeneficiario = 360022133372L;
}

/// <summary>
/// Brand IDs conocidos de Saludsa en Zendesk.
/// Referencia: memoria preexistencias-zendesk-misruteo.
/// </summary>
file static class ZdBrands
{
    internal const long Auxiliar = 1692030147L;
    internal const long Digital = 18509808293645L;
}

/// <summary>
/// Implementación de <see cref="IZendeskClient"/> para el área nueva de Nexus.
/// </summary>
public sealed class ZendeskClient : IZendeskClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ZendeskClient> _logger;
    private readonly IReadOnlyList<ZendeskCuentaConfig> _cuentas;

    // Map de brand_id → enum para el selector de cuenta
    private readonly Dictionary<long, ZendeskCuenta> _brandMap;

    // ----------------------------------------------------------------
    // Construcción: se inyecta la BD y se materializan las cuentas activas
    // ----------------------------------------------------------------

    /// <summary>
    /// Inicializa el cliente resolviendo las cuentas activas desde <see cref="OCRDbContext"/>.
    /// Si la BD no está disponible o la tabla está vacía, el cliente se construye
    /// con lista de cuentas vacía (permite arrancar sin tokens configurados).
    /// </summary>
    public ZendeskClient(
        IHttpClientFactory httpClientFactory,
        ILogger<ZendeskClient> logger,
        OCRDbContext db)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Carga sincrónica en constructor: es seguro porque se ejecuta
        // una única vez durante la resolución del scope DI.
        // Si la tabla no existe aún (entorno sin migrar), lista vacía.
        try
        {
            _cuentas = db.ZendeskConf
                .Where(z => z.IsActive)
                .AsNoTracking()
                .Select(z => new ZendeskCuentaConfig
                {
                    Code = z.Code,
                    Subdomain = z.Subdomain,
                    // SecretRef actúa como placeholder: en entornos sin Key Vault
                    // contiene el token directamente; en producción apuntará al
                    // nombre del secret en Key Vault y se resolverá externamente.
                    Token = z.SecretRef ?? string.Empty
                })
                .ToList()
                .AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo cargar la configuración de cuentas Zendesk desde BD. El cliente operará sin cuentas.");
            _cuentas = Array.Empty<ZendeskCuentaConfig>();
        }

        _brandMap = new Dictionary<long, ZendeskCuenta>
        {
            { ZdBrands.Auxiliar, ZendeskCuenta.Auxiliar },
            { ZdBrands.Digital, ZendeskCuenta.Digital }
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<ZendeskCuentaConfig> CuentasActivas => _cuentas;

    // ----------------------------------------------------------------
    // Selector de cuenta
    // ----------------------------------------------------------------

    /// <inheritdoc />
    public ZendeskCuenta? ResolverCuentaPorBrandId(long brandId)
        => _brandMap.TryGetValue(brandId, out var cuenta) ? cuenta : null;

    private ZendeskCuentaConfig? ObtenerConfig(ZendeskCuenta cuenta)
    {
        var nombre = cuenta.ToString();
        return _cuentas.FirstOrDefault(c =>
            string.Equals(c.Code, nombre, StringComparison.OrdinalIgnoreCase));
    }

    // ----------------------------------------------------------------
    // Búsqueda
    // ----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IReadOnlyList<ZendeskBusquedaResultDto>> BuscarPorNumeroSobreAsync(
        string numeroSobre,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(numeroSobre))
            throw new ArgumentException("El número de sobre no puede estar vacío.", nameof(numeroSobre));

        // Busca en todas las cuentas activas en paralelo
        var tareas = _cuentas.Select(cfg =>
            BuscarEnCuentaAsync(
                cfg,
                $"type:ticket custom_field_{ZdFields.NumeroSobre}:{Uri.EscapeDataString(numeroSobre)}",
                cancellationToken));

        var todos = await Task.WhenAll(tareas).ConfigureAwait(false);
        return todos.Where(r => r != null).Cast<ZendeskBusquedaResultDto>().ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ZendeskBusquedaResultDto>> BuscarPorPersonaAsync(
        string cedula,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cedula))
            throw new ArgumentException("La cédula no puede estar vacía.", nameof(cedula));

        var tareas = _cuentas.Select(cfg =>
            BuscarEnCuentaAsync(
                cfg,
                $"type:ticket custom_field_{ZdFields.CedulaBeneficiario}:{Uri.EscapeDataString(cedula)}",
                cancellationToken));

        var todos = await Task.WhenAll(tareas).ConfigureAwait(false);
        return todos.Where(r => r != null).Cast<ZendeskBusquedaResultDto>().ToList().AsReadOnly();
    }

    private async Task<ZendeskBusquedaResultDto?> BuscarEnCuentaAsync(
        ZendeskCuentaConfig cfg,
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var http = CrearHttpClient(cfg);
            var url = $"api/v2/search.json?query={query}&per_page=25";

            var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var resultado = JsonSerializer.Deserialize<ZdApiSearchResponse>(json,
                ZdJsonOptions.Default);

            if (resultado?.Results == null)
                return null;

            var cuentaEnum = Enum.TryParse<ZendeskCuenta>(cfg.Code, ignoreCase: true, out var ce)
                ? ce
                : ZendeskCuenta.Auxiliar;

            var sobres = resultado.Results.Select(t => MapToSobre(t, cuentaEnum)).ToList();

            return new ZendeskBusquedaResultDto
            {
                Cuenta = cuentaEnum,
                TotalCount = resultado.Count,
                Items = sobres.AsReadOnly()
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Error buscando en cuenta Zendesk {Code}.", cfg.Code);
            return null;
        }
    }

    // ----------------------------------------------------------------
    // Lectura de ticket
    // ----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<ZendeskTicketDto?> LeerTicketAsync(
        long ticketId,
        ZendeskCuenta cuenta,
        CancellationToken cancellationToken = default)
    {
        var cfg = ObtenerConfig(cuenta);
        if (cfg is null)
        {
            _logger.LogWarning("No hay configuración activa para la cuenta Zendesk {Cuenta}.", cuenta);
            return null;
        }

        try
        {
            var http = CrearHttpClient(cfg);
            var response = await http.GetAsync(
                $"api/v2/tickets/{ticketId}.json", cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<ZdApiTicketEnvelope>(json, ZdJsonOptions.Default);

            return envelope?.Ticket is null ? null : MapToTicket(envelope.Ticket, cuenta);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error leyendo ticket {TicketId} en cuenta {Cuenta}.",
                ticketId, cuenta);
            throw;
        }
    }

    // ----------------------------------------------------------------
    // Comentarios / auditoría
    // ----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IReadOnlyList<ZendeskComentarioDto>> ObtenerComentariosAsync(
        long ticketId,
        ZendeskCuenta cuenta,
        CancellationToken cancellationToken = default)
    {
        var cfg = ObtenerConfig(cuenta);
        if (cfg is null)
        {
            _logger.LogWarning("No hay configuración activa para la cuenta Zendesk {Cuenta}.", cuenta);
            return Array.Empty<ZendeskComentarioDto>();
        }

        try
        {
            var http = CrearHttpClient(cfg);
            var response = await http.GetAsync(
                $"api/v2/tickets/{ticketId}/comments.json", cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<ZdApiCommentsEnvelope>(json, ZdJsonOptions.Default);

            if (envelope?.Comments is null)
                return Array.Empty<ZendeskComentarioDto>();

            return envelope.Comments.Select(MapToComentario).ToList().AsReadOnly();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error obteniendo comentarios del ticket {TicketId} en {Cuenta}.",
                ticketId, cuenta);
            throw;
        }
    }

    // ----------------------------------------------------------------
    // Descarga de adjuntos (capacidad NUEVA)
    // ----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<Stream> DescargarAdjuntoAsync(
        string contentUrl,
        ZendeskCuenta cuenta,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentUrl))
            throw new ArgumentException("La URL del adjunto no puede estar vacía.", nameof(contentUrl));

        var cfg = ObtenerConfig(cuenta);
        if (cfg is null)
            throw new InvalidOperationException(
                $"No hay configuración activa para la cuenta Zendesk '{cuenta}'.");

        // Usa el HttpClient de la cuenta correcta para enviar el Bearer
        var http = CrearHttpClient(cfg);

        // La descarga del adjunto usa el content_url como URL absoluta
        var request = new HttpRequestMessage(HttpMethod.Get, contentUrl);
        var response = await http.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        // Devuelve un stream que el consumidor puede copiar a memoria o a Blob.
        // El consumidor es responsable de disponer el stream.
        return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }

    // ----------------------------------------------------------------
    // Helpers privados
    // ----------------------------------------------------------------

    /// <summary>
    /// Crea un <see cref="HttpClient"/> con BaseAddress y cabecera Authorization Bearer
    /// correctas para la cuenta indicada.
    /// </summary>
    private HttpClient CrearHttpClient(ZendeskCuentaConfig cfg)
    {
        var http = _httpClientFactory.CreateClient($"Zendesk_{cfg.Code}");
        http.BaseAddress = new Uri(cfg.BaseUrl);
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", cfg.Token);
        http.DefaultRequestHeaders.Accept.Clear();
        http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    private ZendeskSobreDto MapToSobre(ZdApiTicket t, ZendeskCuenta cuenta)
    {
        var fields = t.CustomFields?.ToDictionary(f => f.Id, f => f.Value)
                     ?? new Dictionary<long, string?>();

        return new ZendeskSobreDto
        {
            Id = t.Id,
            Subject = t.Subject,
            Status = t.Status,
            BrandId = t.BrandId,
            Cuenta = ResolverCuentaPorBrandId(t.BrandId) ?? cuenta,
            NumeroSobre = fields.GetValueOrDefault(ZdFields.NumeroSobre),
            CedulaBeneficiario = fields.GetValueOrDefault(ZdFields.CedulaBeneficiario),
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt
        };
    }

    private ZendeskTicketDto MapToTicket(ZdApiTicket t, ZendeskCuenta cuenta)
    {
        var fields = t.CustomFields?.ToDictionary(f => f.Id, f => f.Value)
                     ?? new Dictionary<long, string?>();

        return new ZendeskTicketDto
        {
            Id = t.Id,
            Subject = t.Subject,
            Status = t.Status,
            Priority = t.Priority,
            BrandId = t.BrandId,
            Cuenta = ResolverCuentaPorBrandId(t.BrandId) ?? cuenta,
            NumeroSobre = fields.GetValueOrDefault(ZdFields.NumeroSobre),
            CedulaBeneficiario = fields.GetValueOrDefault(ZdFields.CedulaBeneficiario),
            CustomFields = new Dictionary<long, string?>(fields),
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt
        };
    }

    private static ZendeskComentarioDto MapToComentario(ZdApiComment c)
        => new()
        {
            Id = c.Id,
            Body = c.Body,
            IsPublic = c.IsPublic,
            AuthorId = c.AuthorId,
            CreatedAt = c.CreatedAt,
            Attachments = c.Attachments?.Select(a => new ZendeskAdjuntoDto
            {
                Id = a.Id,
                FileName = a.FileName,
                ContentType = a.ContentType,
                Size = a.Size,
                ContentUrl = a.ContentUrl
            }).ToList().AsReadOnly()
            ?? (IReadOnlyList<ZendeskAdjuntoDto>)Array.Empty<ZendeskAdjuntoDto>()
        };
}

/// <summary>Opciones de deserialización JSON compartidas (camelCase permisivo).</summary>
internal static class ZdJsonOptions
{
    internal static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
