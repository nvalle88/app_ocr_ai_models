using System.Text.Json.Serialization;

namespace app_tramites.Services.Graph;

// ============================================================
// REQ-019 T20 — Modelos de structured outputs para extracción
// de entidades del grafo a partir de documentos/respuestas IA.
// ============================================================

/// <summary>
/// Entidades extraídas de un documento o respuesta por structured outputs de Claude.
/// El JSON extraído se deserializa en este tipo antes de escribirlo al grafo.
/// </summary>
public sealed class GraphExtractionResult
{
    /// <summary>Cédula del afiliado identificada en el documento. Null si no se encontró.</summary>
    [JsonPropertyName("cedula")]
    public string? Cedula { get; init; }

    /// <summary>Nombre del afiliado identificado en el documento. Null si no se encontró.</summary>
    [JsonPropertyName("nombreAfiliado")]
    public string? NombreAfiliado { get; init; }

    /// <summary>Número del sobre de reembolso referenciado. Null si no aplica.</summary>
    [JsonPropertyName("numeroSobre")]
    public string? NumeroSobre { get; init; }

    /// <summary>Lista de diagnósticos CIE-10 mencionados en el documento.</summary>
    [JsonPropertyName("diagnosticos")]
    public List<CodigoDescripcion> Diagnosticos { get; init; } = new();

    /// <summary>Lista de procedimientos mencionados en el documento.</summary>
    [JsonPropertyName("procedimientos")]
    public List<CodigoDescripcion> Procedimientos { get; init; } = new();

    /// <summary>Lista de preexistencias referenciadas en el documento.</summary>
    [JsonPropertyName("preexistencias")]
    public List<CodigoDescripcion> Preexistencias { get; init; } = new();

    /// <summary>Hallazgos o afirmaciones relevantes extraídas del texto.</summary>
    [JsonPropertyName("hallazgos")]
    public List<HallazgoItem> Hallazgos { get; init; } = new();
}

/// <summary>Par código/descripción para diagnósticos, procedimientos y preexistencias.</summary>
public sealed class CodigoDescripcion
{
    /// <summary>Código CIE-10 u otro código estándar.</summary>
    [JsonPropertyName("codigo")]
    public string Codigo { get; init; } = string.Empty;

    /// <summary>Descripción textual. Puede ser null si el documento solo menciona el código.</summary>
    [JsonPropertyName("descripcion")]
    public string? Descripcion { get; init; }
}

/// <summary>Hallazgo o afirmación relevante extraída del texto.</summary>
public sealed class HallazgoItem
{
    /// <summary>Texto del hallazgo o afirmación.</summary>
    [JsonPropertyName("texto")]
    public string Texto { get; init; } = string.Empty;

    /// <summary>Origen del hallazgo: 'documento', 'respuesta_ia', 'mensaje_usuario'.</summary>
    [JsonPropertyName("origen")]
    public string? Origen { get; init; }
}
