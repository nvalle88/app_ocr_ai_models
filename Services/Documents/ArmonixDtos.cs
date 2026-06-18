using System.Text.Json.Serialization;

namespace app_ocr_ai_models.Services.Documents;

// ============================================================
// REQ-019 T22 — DTOs para la fuente documental Armonix.
// Mapean los endpoints /api/sobres/BuscarDocumentos y
// /api/sobres/BuscarDocumentosCompleto de api-armonix.
// ============================================================

/// <summary>
/// Cuerpo del POST a <c>/api/sobres/BuscarDocumentos</c>
/// y a <c>/api/sobres/BuscarDocumentosCompleto</c>.
/// Todos los campos son obligatorios en Armonix; NumeroSobre es el
/// identificador principal del sobre de reembolso en MFiles.
/// </summary>
public sealed class ArmonixSobreRequest
{
    /// <summary>Código de producto del contrato.</summary>
    [JsonPropertyName("CodigoProducto")]
    public string CodigoProducto { get; init; } = string.Empty;

    /// <summary>Código de región del contrato.</summary>
    [JsonPropertyName("CodigoRegion")]
    public string CodigoRegion { get; init; } = string.Empty;

    /// <summary>Número de contrato del afiliado.</summary>
    [JsonPropertyName("NumeroContrato")]
    public string NumeroContrato { get; init; } = string.Empty;

    /// <summary>Número del sobre de reembolso en MFiles.</summary>
    [JsonPropertyName("NumeroSobre")]
    public string NumeroSobre { get; init; } = string.Empty;

    /// <summary>Número de persona/paciente dentro del contrato.</summary>
    [JsonPropertyName("NumeroPersonaPaciente")]
    public string NumeroPersonaPaciente { get; init; } = string.Empty;
}

/// <summary>
/// Elemento de la respuesta de <c>/api/sobres/BuscarDocumentosCompleto</c>.
/// Corresponde a <c>RespuestaMFileShift</c> de api-armonix.
/// </summary>
public sealed class ArmonixDocumentoDto
{
    /// <summary>Nombre del documento en MFiles.</summary>
    [JsonPropertyName("Nombre")]
    public string Nombre { get; init; } = string.Empty;

    /// <summary>Extensión del archivo (p. ej. "pdf", "jpg").</summary>
    [JsonPropertyName("Extension")]
    public string Extension { get; init; } = string.Empty;

    /// <summary>Contenido binario del documento en Base64.</summary>
    [JsonPropertyName("Contenido")]
    public string Contenido { get; init; } = string.Empty;
}
