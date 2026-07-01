using System.Text.Json.Serialization;

namespace app_ocr_ai_models.Services.Documents;

// ============================================================
// REQ-019 T22 — DTOs para la fuente documental Armonix.
// Mapean los endpoints /api/sobres/BuscarSobre,
// /api/sobres/BuscarDocumentos y
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

// ──────────────────────────────────────────────────────────────
// REQ-019 T22 RW — DTOs para BuscarSobre (auto-resolución)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Cuerpo del POST a <c>/api/sobres/BuscarSobre</c>.
/// Al menos uno de los campos debe estar informado.
/// </summary>
public sealed class ArmonixBuscarSobreRequest
{
    /// <summary>Número del sobre de reembolso (búsqueda directa).</summary>
    [JsonPropertyName("NumeroSobre")]
    public string? NumeroSobre { get; init; }

    /// <summary>Cédula del afiliado/paciente (búsqueda alternativa).</summary>
    [JsonPropertyName("NumeroCedula")]
    public string? NumeroCedula { get; init; }
}

/// <summary>
/// Sobre resuelto con los identificadores necesarios para llamar a
/// <c>BuscarDocumentos</c> / <c>BuscarDocumentosCompleto</c>.
/// Mapeado desde los campos exactos de <c>SobreEntity</c> de api-armonix.
/// </summary>
public sealed class ArmonixSobreResueltoDto
{
    /// <summary>Número del sobre (SobreEntity.NumeroSobre).</summary>
    public string NumeroSobre { get; init; } = string.Empty;

    /// <summary>Código de región del contrato (SobreEntity.CodigoRegion).</summary>
    public string CodigoRegion { get; init; } = string.Empty;

    /// <summary>Código de producto del contrato (SobreEntity.CodigoProducto).</summary>
    public string CodigoProducto { get; init; } = string.Empty;

    /// <summary>Número de contrato (SobreEntity.NumeroContrato casteado a string).</summary>
    public string NumeroContrato { get; init; } = string.Empty;

    /// <summary>Número de persona/paciente (SobreEntity.NumeroPersonaPaciente).</summary>
    public string NumeroPersonaPaciente { get; init; } = string.Empty;

    /// <summary>Nombre del titular para mostrar en la selección (SobreEntity.NombresTitular).</summary>
    public string NombreTitular { get; init; } = string.Empty;

    /// <summary>Estado del sobre para mostrar en la selección (SobreEntity.NombreEstadoSobre).</summary>
    public string EstadoSobre { get; init; } = string.Empty;

    /// <summary>Fecha de recepción del sobre (SobreEntity.FechaRecepcion).</summary>
    public DateTime? FechaRecepcion { get; init; }
}

/// <summary>
/// Envoltura genérica de respuesta de api-armonix para listas paginadas.
/// Mapea <c>RespuestaGenericaServicio&lt;RespuestaPaginado&lt;SobreEntity&gt;&gt;</c>.
/// </summary>
/// <typeparam name="T">Tipo del dato de la respuesta.</typeparam>
public sealed class ArmonixRespuestaGenerica<T>
{
    /// <summary>Indica si la operación fue exitosa.</summary>
    [JsonPropertyName("Exitoso")]
    public bool Exitoso { get; init; }

    /// <summary>Mensaje de error o información.</summary>
    [JsonPropertyName("Mensaje")]
    public string? Mensaje { get; init; }

    /// <summary>Datos de la respuesta.</summary>
    [JsonPropertyName("Datos")]
    public T? Datos { get; init; }
}

/// <summary>
/// Respuesta paginada de api-armonix.
/// </summary>
/// <typeparam name="T">Tipo del elemento de la lista.</typeparam>
public sealed class ArmonixRespuestaPaginada<T>
{
    /// <summary>Lista de elementos.</summary>
    [JsonPropertyName("Lista")]
    public List<T> Lista { get; init; } = new();

    /// <summary>Total de registros.</summary>
    [JsonPropertyName("Total")]
    public int Total { get; init; }
}

/// <summary>
/// SobreEntity mínimo para deserializar la respuesta de BuscarSobre.
/// Solo los campos necesarios para la auto-resolución de identificadores.
/// </summary>
public sealed class ArmonixSobreEntityDto
{
    [JsonPropertyName("NumeroSobre")]
    public string? NumeroSobre { get; init; }

    [JsonPropertyName("CodigoRegion")]
    public string? CodigoRegion { get; init; }

    [JsonPropertyName("CodigoProducto")]
    public string? CodigoProducto { get; init; }

    [JsonPropertyName("NumeroContrato")]
    public int? NumeroContrato { get; init; }

    [JsonPropertyName("NumeroPersonaPaciente")]
    public int NumeroPersonaPaciente { get; init; }

    [JsonPropertyName("NombresTitular")]
    public string? NombresTitular { get; init; }

    [JsonPropertyName("NombreEstadoSobre")]
    public string? NombreEstadoSobre { get; init; }

    [JsonPropertyName("FechaRecepcion")]
    public DateTime? FechaRecepcion { get; init; }
}
