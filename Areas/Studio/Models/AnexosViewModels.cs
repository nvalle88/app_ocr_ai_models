using app_tramites.Models.ModelAi;

namespace app_ocr_ai_models.Areas.Studio.Models;

// ============================================================
// REQ-046 — ViewModels de la biblioteca de anexos (Auditoría de Casos).
// ============================================================

/// <summary>Pantalla principal de la biblioteca: contratos base, anexos y el formulario de carga.</summary>
public sealed class AnexosIndexViewModel
{
    public List<AnexoContrato> Contratos { get; set; } = new();
    public List<AnexoResumenViewModel> Anexos { get; set; } = new();
    public List<AnexoIngesta> UltimasIngestas { get; set; } = new();

    /// <summary>Mensaje de resultado de la última carga (TempData).</summary>
    public string? Mensaje { get; set; }
    public string? Error { get; set; }
}

/// <summary>Fila de la tabla de anexos con sus conteos.</summary>
public sealed class AnexoResumenViewModel
{
    public int Id { get; set; }
    public string CodigoPlan { get; set; } = string.Empty;
    public string? NombrePlan { get; set; }
    public string? CodigoProducto { get; set; }
    public string? Version { get; set; }
    public string? TipoContrato { get; set; }
    public string Estado { get; set; } = string.Empty;
    public int Coberturas { get; set; }
    public int Carencias { get; set; }
    public int Exclusiones { get; set; }
    public DateTime CreatedDate { get; set; }
}

/// <summary>Detalle de un anexo: sus condiciones estructuradas.</summary>
public sealed class AnexoDetalleViewModel
{
    public Anexo Anexo { get; set; } = null!;
    public AnexoContrato? Contrato { get; set; }
    public List<AnexoCobertura> Coberturas { get; set; } = new();
    public List<AnexoCarencia> Carencias { get; set; } = new();
    public List<AnexoExclusion> Exclusiones { get; set; } = new();
    public List<AnexoClausula> Clausulas { get; set; } = new();
}
