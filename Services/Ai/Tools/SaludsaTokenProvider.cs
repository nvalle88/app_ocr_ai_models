using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace app_tramites.Services.Ai.Tools;

// ============================================================
// REQ-019 T5 — Implementación OAuth2 Saludsa.
// PUNTO B2/T0b: credenciales se inyectan aquí desde Key Vault / config segura.
// ============================================================

/// <summary>
/// Implementación de <see cref="ISaludsaTokenProvider"/> que obtiene un token
/// OAuth2 client_credentials del endpoint de Saludsa y construye las cabeceras
/// <c>CabeceraServicioRest</c> / <c>AuthorizeSalud</c>.
/// </summary>
/// <remarks>
/// Convención fija: <c>CodigoAplicacion=3</c>, <c>Plataforma=7</c>.
/// <para>
/// <b>B2/T0b — Credenciales de producción:</b> en producción la sección
/// <c>Saludsa:Auth:ClientId</c> y <c>Saludsa:Auth:ClientSecret</c> apuntan
/// a referencias de Azure Key Vault (configuradas en el App Service).
/// En dev/test se leen de <c>appsettings.json</c> o variables de entorno.
/// NUNCA se hardcodean valores en este archivo.
/// </para>
/// </remarks>
public sealed class SaludsaTokenProvider : ISaludsaTokenProvider
{
    // Convención Saludsa para el motor Claude (REQ-019)
    private const int CodigoAplicacion = 3;
    private const int Plataforma = 7;

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
    public async Task<IReadOnlyDictionary<string, string>> GetAuthHeadersAsync(CancellationToken ct = default)
    {
        var token = await GetOrRefreshTokenAsync(ct).ConfigureAwait(false);

        // CabeceraServicioRest: cabecera propietaria Saludsa con metadata de la aplicación
        var cabeceraServicioRest = BuildCabeceraServicioRest();

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"]        = $"Bearer {token}",
            // ---- Cabeceras propietarias Saludsa ----
            // CabeceraServicioRest: payload JSON con CodigoAplicacion, Plataforma y timestamp
            ["CabeceraServicioRest"] = cabeceraServicioRest,
            // AuthorizeSalud: alias alternativo que algunas APIs validan
            ["AuthorizeSalud"]       = $"Bearer {token}"
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
        // En producción: Key Vault references en appsettings.json del App Service.
        // En dev/test:   appsettings.Development.json o variables de entorno.
        // NUNCA hardcodear aquí.
        var tokenUrl     = _config["Saludsa:Auth:TokenUrl"];
        var clientId     = _config["Saludsa:Auth:ClientId"];
        var clientSecret = _config["Saludsa:Auth:ClientSecret"];

        if (string.IsNullOrWhiteSpace(tokenUrl)
            || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(clientSecret))
        {
            // B2/T0b: credenciales no disponibles → falla clara en runtime
            throw new InvalidOperationException(
                "[T5 B2] Credenciales OAuth2 Saludsa no configuradas. " +
                "Configure 'Saludsa:Auth:TokenUrl', 'Saludsa:Auth:ClientId' y " +
                "'Saludsa:Auth:ClientSecret' en appsettings / Key Vault (B2/T0b).");
        }

        var httpClient = _httpClientFactory.CreateClient("SaludsaOAuth2");

        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type",    "client_credentials"),
            new KeyValuePair<string, string>("client_id",     clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret)
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
            : 3600); // fallback 1 hora

        return (tokenResponse.AccessToken, expiry);
    }

    private static string BuildCabeceraServicioRest()
    {
        // Serialización manual para evitar dependencias de serialización en este tipo
        var timestamp = DateTimeOffset.UtcNow.ToString("o");
        return $"{{\"CodigoAplicacion\":{CodigoAplicacion},\"Plataforma\":{Plataforma},\"Timestamp\":\"{timestamp}\"}}";
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
    }
}
