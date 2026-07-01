namespace app_tramites.Services.Ai.Tools;

// ============================================================
// REQ-019 T5 — Auth Saludsa OAuth2 para invocación de tools.
// ============================================================

/// <summary>
/// Obtiene el token OAuth2 del ecosistema Saludsa (flujo <c>password grant</c>)
/// y construye las cabeceras que requieren las APIs internas.
/// </summary>
/// <remarks>
/// Convención fija: <c>CodigoAplicacion=3</c>, <c>CodigoPlataforma=7</c>.
/// Las credenciales (<c>TokenUrl</c>, <c>ClientId</c>, <c>Username</c>, <c>Password</c>)
/// se leen de config segura (Azure Key Vault → appsettings → variables de entorno).
/// Nunca se hardcodean. Si no hay credencial al iniciar, el proveedor compila;
/// falla en <see cref="GetAuthHeadersAsync"/> con un mensaje claro (no en startup).
/// <para>
/// <b>Punto de inyección B2/T0b:</b> las claves de producción se inyectan
/// mediante Key Vault. La sección de configuración esperada es <c>Saludsa:Auth</c>:
/// <code>
/// {
///   "Saludsa": {
///     "Auth": {
///       "TokenUrl":            "https://.../ServicioAutorizacion/oauth2/token",
///       "ClientId":            "...",
///       "Username":            "...",
///       "Password":            "...",
///       "SistemaOperativo":    "Windows",
///       "DispositivoNavegador":"DeveloperAI-Nexus",
///       "DireccionIP":         "10.12.10.142"
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
    /// de Saludsa. Devuelve las 7 cabeceras requeridas:
    /// <list type="bullet">
    ///   <item><c>Authorization: bearer &lt;token&gt;</c></item>
    ///   <item><c>Accept: application/json, text/json, application/xml, text/xml</c></item>
    ///   <item><c>CodigoAplicacion: 3</c></item>
    ///   <item><c>CodigoPlataforma: 7</c></item>
    ///   <item><c>SistemaOperativo: &lt;valor configurado&gt;</c></item>
    ///   <item><c>DispositivoNavegador: &lt;valor configurado, mín 6 chars&gt;</c></item>
    ///   <item><c>DireccionIP: &lt;valor configurado&gt;</c></item>
    /// </list>
    /// El token se cachea en memoria y se renueva automáticamente antes de su expiración.
    /// </summary>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Diccionario de nombre → valor listo para agregar al <see cref="System.Net.Http.HttpRequestMessage"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Si las credenciales no están configuradas (falla en runtime, no en startup).
    /// </exception>
    Task<IReadOnlyDictionary<string, string>> GetAuthHeadersAsync(CancellationToken ct = default);
}
