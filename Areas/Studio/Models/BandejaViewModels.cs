namespace app_ocr_ai_models.Areas.Studio.Models;

// ============================================================
// REQ-019 — Bandeja de casos del Área Studio (análisis ya realizados visibles).
// ============================================================

/// <summary>Fila de la bandeja: un caso OCR con su estado, documentos y resolución.</summary>
public sealed class CasoListItem
{
    public Guid CaseCode { get; set; }
    public string ShortCode => CaseCode.ToString().Split('-')[0];

    public string? NumeroSobre { get; set; }
    public string? Cliente { get; set; }
    public string? Origen { get; set; }

    public string DefinitionCode { get; set; } = string.Empty;
    public string? ProcesoNombre { get; set; }
    public string Estado { get; set; } = string.Empty;

    public int NumDocumentos { get; set; }
    public DateTime StartDate { get; set; }

    /// <summary>Etiqueta de la resolución (si existe): "Liquida", "Negativa", "Control humano"…</summary>
    public string? ResolucionLabel { get; set; }
    /// <summary>Clase Bootstrap del label de resolución: label-success/warning/danger/default/info.</summary>
    public string ResolucionCss { get; set; } = "label-default";
    public bool TieneResolucion { get; set; }
}

/// <summary>ViewModel de la bandeja con filtros y paginado simple.</summary>
public sealed class BandejaViewModel
{
    public IReadOnlyList<CasoListItem> Casos { get; init; } = Array.Empty<CasoListItem>();

    public string? Search { get; init; }
    public string? Estado { get; init; }
    public int Page { get; init; } = 1;
    public int TotalPages { get; init; } = 1;
    public int Total { get; init; }

    // KPIs de cabecera
    public int TotalCasos { get; init; }
    public int ConResolucion { get; init; }

    /// <summary>Estados disponibles para el filtro (de los casos existentes).</summary>
    public IReadOnlyList<string> EstadosDisponibles { get; init; } = Array.Empty<string>();
}
