using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace app_tramites.Services.Ai.Tools;

// ============================================================
// REQ-019 T5 — Implementación OAuth2 Saludsa (password grant).
// PUNTO B2/T0b: credenciales se inyectan aquí desde Key Vault / config segura.
// ============================================================

/// <summary>
/// Implementación de <see cref="ISaludsaTokenProvider"/> que obtiene un token
/// OAuth2 mediante flujo <c>password grant</c> del endpoint de Saludsa y construye
/// las cabeceras requeridas por las APIs internas.
/// </summary>
/// <remarks>
/// Convención fija: <c>CodigoAplicacion=3</c>, <c>CodigoPlataforma=7</c>.
/// <para>
/// <b>B2/T0b — Credenciales de producción:</b> en producción la sección
/// <c>Saludsa:Auth</c> se inyecta desde Azure Key Vault references en el App Service.
/// En dev/test se leen de <c>appsettings.json</c> o variables de entorno.
/// NUNCA se hardcodean valores en este archivo.
/// </para>
/// <para>
/// <b>Claves de configuración esperadas (sección <c>Saludsa:Auth</c>):</b>
/// <list type="bullet">
///   <item><c>Saludsa:Auth:TokenUrl</c> — URL completa del endpoint OAuth2 token
///     (ej. <c>https://servicios.saludsa.com.ec/ServicioAutorizacion/oauth2/token</c>).</item>
///   <item><c>Saludsa:Auth:ClientId</c> — client_id del password grant.</item>
///   <item><c>Saludsa:Auth:Username</c> — usuario del password grant.</item>
///   <item><c>Saludsa:Auth:Password</c> — contraseña del password grant.</item>
///   <item><c>Saludsa:Auth:SistemaOperativo</c> — valor de la cabecera
///     <c>SistemaOperativo</c> (default: "Windows").</item>
///   <item><c>Saludsa:Auth:DispositivoNavegador</c> — valor de la cabecera
///     <c>DispositivoNavegador</c>, mínimo 6 caracteres (default: "DeveloperAI-Nexus").</item>
///   <item><c>Saludsa:Auth:DireccionIP</c> — valor de la cabecera
///     <c>DireccionIP</c> (default: "10.12.10.142").</item>
/// </list>
/// </para>
/// </remarks>
public sealed class SaludsaTokenProvider : ISaludsaTokenProvider
{
    // Convención Saludsa para el motor Claude (REQ-019)
    private const string CodigoAplicacion = "3";
    private const string CodigoPlataforma = "7";

    // Defaults para cabeceras opcionales
    private const string DefaultSistemaOperativo     = "Windows";
    private const string DefaultDispositivoNavegador = "DeveloperAI-Nexus";
    private const string DefaultDireccionIP          = "10.12.10.142";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;

    // Cache simple en memoria: token + expiración
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Crea el proveedor con las dependencias de infraestructura.
    /// </summary>
    /// <param name="httpClientFactory">Factory de HttpClient para llamar al endpoint OAuth2.</param>
    /// <param name="config">Configuración de la aplicación (lee <c>Saludsa:Auth</c>).</param>
    public SaludsaTokenProvider(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Devuelve las 7 cabeceras requeridas por las APIs internas de Saludsa:
    /// <c>Authorization</c>, <c>Accept</c>, <c>CodigoAplicacion</c>,
    /// <c>CodigoPlataforma</c>, <c>SistemaOperativo</c>,
    /// <c>DispositivoNavegador</c> y <c>DireccionIP</c>.
    /// El token se cachea en memoria hasta <c>expires_in</c> menos un margen de 60 s.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, string>> GetAuthHeadersAsync(CancellationToken ct = default)
    {
        var token = await GetOrRefreshTokenAsync(ct).ConfigureAwait(false);

        var sistemaOperativo     = _config["Saludsa:Auth:SistemaOperativo"]     ?? DefaultSistemaOperativo;
        var dispositivoNavegador = _config["Saludsa:Auth:DispositivoNavegador"] ?? DefaultDispositivoNavegador;
        var direccionIp          = _config["Saludsa:Auth:DireccionIP"]          ?? DefaultDireccionIP;

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Cabecera 1: portador del token (Saludsa usa "bearer" en minúsculas)
            ["Authorization"]        = $"bearer {token}",
            // Cabecera 2: tipos de respuesta aceptados
            ["Accept"]               = "application/json, text/json, application/xml, text/xml",
            // Cabecera 3: identificador de la aplicación consumidora
            ["CodigoAplicacion"]     = CodigoAplicacion,
            // Cabecera 4: identificador de la plataforma (OJO: CodigoPlataforma, no Plataforma)
            ["CodigoPlataforma"]     = CodigoPlataforma,
            // Cabeceras 5-7: contexto del dispositivo (configurables, con defaults)
            ["SistemaOperativo"]     = sistemaOperativo,
            ["DispositivoNavegador"] = dispositivoNavegador,
            ["DireccionIP"]          = direccionIp
        };
    }

    // ── Lógica de token ─────────────────────────────────────────────────

    private async Task<string> GetOrRefreshTokenAsync(CancellationToken ct)
    {
        // Ruta feliz: token válido (con margen de 60 s)
        if (_cachedToken != null && DateTimeOffset.UtcNow < _tokenExpiry.AddSeconds(-60))
            return _cachedToken;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Doble check: pudo haberse refrescado mientras esperábamos el lock
            if (_cachedToken != null && DateTimeOffset.UtcNow < _tokenExpiry.AddSeconds(-60))
                return _cachedToken;

            (_cachedToken, _tokenExpiry) = await FetchTokenAsync(ct).ConfigureAwait(false);
            return _cachedToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<(string Token, DateTimeOffset Expiry)> FetchTokenAsync(CancellationToken ct)
    {
        // ── PUNTO B2/T0b: leer credenciales de config segura ──────────────
        // En producción: Key Vault references en appsettings del App Service.
        // En dev/test:   appsettings.Development.json o variables de entorno.
        // NUNCA hardcodear aquí.
        var tokenUrl = _config["Saludsa:Auth:TokenUrl"];
        var clientId = _config["Saludsa:Auth:ClientId"];
        var username = _config["Saludsa:Auth:Username"];
        var password = _config["Saludsa:Auth:Password"];

        if (string.IsNullOrWhiteSpace(tokenUrl)
            || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
        {
            // B2/T0b: credenciales no disponibles → falla clara en runtime
            throw new InvalidOperationException(
                "[T5 B2] Credenciales OAuth2 Saludsa no configuradas. " +
                "Configure 'Saludsa:Auth:TokenUrl', 'Saludsa:Auth:ClientId', " +
                "'Saludsa:Auth:Username' y 'Saludsa:Auth:Password' " +
                "en appsettings / Key Vault (B2/T0b).");
        }

        var httpClient = _httpClientFactory.CreateClient("SaludsaOAuth2");

        // Flujo password grant (validado en vivo con respuesta 200)
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "password"),
            new KeyValuePair<string, string>("username",   username),
            new KeyValuePair<string, string>("password",   password),
            new KeyValuePair<string, string>("client_id",  clientId)
        });

        using var response = await httpClient.PostAsync(tokenUrl, formData, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content
            .ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("[T5 B2] Respuesta de token OAuth2 vacía o malformada.");

        if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            throw new InvalidOperationException("[T5 B2] Token OAuth2 recibido está vacío.");

        var expiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn > 0
            ? tokenResponse.ExpiresIn
            : 3600); // fallback 1 hora si expires_in no viene

        return (tokenResponse.AccessToken, expiry);
    }

    // ── DTO para deserializar la respuesta del endpoint de token ──────────

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; init; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; init; } = string.Empty;
    }
}
