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
}
