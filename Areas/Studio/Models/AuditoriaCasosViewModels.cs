using app_ocr_ai_models.Services.Documents;

namespace app_ocr_ai_models.Areas.Studio.Models;

// ============================================================
// REQ-046 — ViewModels de la pantalla "Auditoría Casos".
// ============================================================

/// <summary>Pantalla del auditor: busca el sobre en Armonix y arranca la auditoría.</summary>
public sealed class AuditoriaCasosViewModel
{
    public string? Criterio { get; set; }
    public bool Buscado { get; set; }
    public string? Error { get; set; }

    /// <summary>Sobres encontrados en Armonix para el criterio.</summary>
    public IReadOnlyList<ArmonixSobreResueltoDto> Resultados { get; set; } = new List<ArmonixSobreResueltoDto>();

    /// <summary>Casos de auditoría recientes, para retomar.</summary>
    public List<CasoAuditoriaReciente> Recientes { get; set; } = new();
}

/// <summary>Fila de la lista de casos de auditoría recientes.</summary>
public sealed class CasoAuditoriaReciente
{
    public Guid CaseCode { get; set; }
    public string? NumeroSobre { get; set; }
    public string? NombreTitular { get; set; }
    public string Estado { get; set; } = string.Empty;
    public bool TieneAuditoria { get; set; }
    public DateTime StartDate { get; set; }
    public int Documentos { get; set; }
}
