using System.Text.Json.Serialization;

namespace app_tramites.Services.Ai.Tools;

// ============================================================
// REQ-019 T5 — DTO de deserialización de OPAITool.BindingConfig.
// ============================================================

/// <summary>
/// Configuración de binding de una tool (deserializada de <c>OPAITool.BindingConfig</c> JSON).
/// Define cómo el executor arma y despacha la llamada HTTP a la API interna.
/// </summary>
/// <remarks>
/// Campos clave:
/// <list type="bullet">
///   <item><see cref="BaseUrl"/>: placeholder <c>{api-contrato}</c> / <c>{api-armonix}</c> / <c>{api-prestador}</c>
///     resuelto por configuración (B1/T0a).</item>
///   <item><see cref="ParamMap"/>: parámetros que van como query string (key = nombre en tool input,
///     value = nombre en la URL).</item>
///   <item><see cref="BodyMap"/>: campos del input de la tool que se agrupan en el body JSON.</item>
///   <item><see cref="AuthMode"/>: solo <c>"saludsa-oauth"</c> es soportado actualmente.</item>
/// </list>
/// </remarks>
public sealed class ToolBindingConfig
{
    /// <summary>Base URL del servicio, con placeholder. Ej: <c>{api-contrato}</c>.</summary>
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Método HTTP: <c>GET</c> | <c>POST</c>.</summary>
    [JsonPropertyName("method")]
    public string Method { get; init; } = "GET";

    /// <summary>Ruta relativa del endpoint. Ej: <c>/api/contrato/ObtenerContratosPorDocumentoChatBot</c>.</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Mapeo de parámetros de query string: key = nombre en input de la tool,
    /// value = nombre en la query string de la URL.
    /// </summary>
    [JsonPropertyName("paramMap")]
    public Dictionary<string, string> ParamMap { get; init; } = new();

    /// <summary>
    /// Mapeo de campos para el body JSON: lista de nombres de campos del input de la tool
    /// que se incluyen directamente en el body (como JSON plano).
    /// Soporta notación dot para objetos anidados: <c>filter.region</c>.
    /// </summary>
    [JsonPropertyName("bodyMap")]
    public List<string> BodyMap { get; init; } = new();

    /// <summary>Modo de autenticación. Solo <c>"saludsa-oauth"</c> es soportado.</summary>
    [JsonPropertyName("authMode")]
    public string AuthMode { get; init; } = "saludsa-oauth";

    /// <summary>Campo de respuesta que contiene datos binarios en base64 (ej: <c>Contenido</c>).</summary>
    [JsonPropertyName("binaryField")]
    public string? BinaryField { get; init; }

    // ── REQ-019: BindingType "Sql" — consulta read-only a BD de negocio ──

    /// <summary>
    /// Nombre de la cadena de conexión (sección <c>ConnectionStrings</c>) que usa
    /// la tool cuando <c>BindingType = "Sql"</c>. Ej: <c>SaludConsultas</c>.
    /// </summary>
    [JsonPropertyName("connection")]
    public string? Connection { get; init; }

    /// <summary>
    /// Consulta SELECT parametrizada (solo lectura) para <c>BindingType = "Sql"</c>.
    /// Los parámetros <c>@nombre</c> se toman del input de la tool por nombre.
    /// </summary>
    [JsonPropertyName("query")]
    public string? Query { get; init; }

    /// <summary>Máximo de filas devueltas por una tool SQL (default 50).</summary>
    [JsonPropertyName("maxRows")]
    public int MaxRows { get; init; } = 50;

    /// <summary>
    /// El código de OTRA tool que hace lo mismo por otro camino, para usarla
    /// cuando ésta no responde.
    ///
    /// Nace de un problema real: dos tools del chat van por el gateway de
    /// Saludsa, y desde el App Service de Azure ese gateway no se alcanza
    /// —medido: el puerto 443 no abre, 8 segundos sin respuesta— mientras las
    /// bases SQL sí. Desde la red interna pasa al revés en otros casos.
    ///
    /// La tentación era sustituir la tool de API por una de SQL. Néstor lo
    /// paró: <i>"no debemos borrar los anteriores, una forma de enganchar los
    /// apis o los de sql"</i>. Y tiene razón — sustituir obliga a elegir un
    /// ambiente y a mantener dos catálogos. Encadenar no: el mismo despliegue
    /// sirve dentro y fuera, y el día que abran el cortafuegos vuelve a usarse
    /// el API sin tocar nada.
    ///
    /// El API manda: es la fuente de verdad y aplica las reglas de negocio del
    /// servicio. El SQL es la red de seguridad, y cuando responde él se dice,
    /// para no dar por equivalente lo que no lo es.
    /// </summary>
    public string? FallbackTool { get; init; }
}
