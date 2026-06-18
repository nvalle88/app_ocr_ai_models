namespace app_tramites.Services.Ai.Tools;

// ============================================================
// REQ-019 T5 — Auth Saludsa OAuth2 para invocación de tools.
// ============================================================

/// <summary>
/// Obtiene el token OAuth2 del ecosistema Saludsa y construye las cabeceras
/// que requieren las APIs internas (<c>CabeceraServicioRest</c> / <c>AuthorizeSalud</c>).
/// </summary>
/// <remarks>
/// Convención fija: <c>CodigoAplicacion=3</c>, <c>Plataforma=7</c>.
/// Las credenciales (client_id, client_secret, token_url) se leen de config segura
/// (Azure Key Vault → appsettings → variables de entorno). Nunca se hardcodean.
/// Si no hay credencial al iniciar, el proveedor compila; falla en <see cref="GetAuthHeadersAsync"/>
/// con un mensaje claro (no en startup).
/// <para>
/// <b>Punto de inyección B2/T0b:</b> las claves de producción se inyectan en este punto
/// mediante Key Vault. La sección de configuración esperada es <c>Saludsa:Auth</c>:
/// <code>
/// {
///   "Saludsa": {
///     "Auth": {
///       "TokenUrl":      "https://...",
///       "ClientId":      "...",   &lt;-- referencia a Key Vault en producción
///       "ClientSecret":  "..."    &lt;-- referencia a Key Vault en producción
///     }
///   }
/// }
/// </code>
/// </para>
/// </remarks>
public interface ISaludsaTokenProvider
{
    /// <summary>
    /// Obtiene el conjunto de cabeceras HTTP necesarias para invocar las APIs internas
    /// Saludsa: <c>Authorization: Bearer &lt;token&gt;</c> +
    /// <c>CabeceraServicioRest</c> / <c>AuthorizeSalud</c>.
    /// </summary>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Diccionario de nombre → valor listo para agregar al <see cref="System.Net.Http.HttpRequestMessage"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Si las credenciales no están configuradas (falla en runtime, no en startup).
    /// </exception>
    Task<IReadOnlyDictionary<string, string>> GetAuthHeadersAsync(CancellationToken ct = default);
}
