using app_tramites.Services.Ai;

namespace app_ocr_ai_models.Areas.Studio.Models;

// ============================================================
// REQ-019 T6 — ViewModels del controlador AnalizarController.
// ============================================================

/// <summary>
/// Formulario para disparar el análisis IA de un caso ya importado.
/// </summary>
public sealed class AnalizarCasoRequest
{
    /// <summary>Código del caso a analizar.</summary>
    public Guid CaseCode { get; set; }

    /// <summary>
    /// Código de proceso a aplicar. Si se deja vacío, el clasificador lo determina.
    /// </summary>
    public string? ProcessCodeOverride { get; set; }
}

/// <summary>
/// Resultado de la orquestación presentado en la vista de análisis.
/// </summary>
public sealed class AnalizarResultadoViewModel
{
    /// <summary>Request original (para repintar el formulario).</summary>
    public AnalizarCasoRequest Request { get; init; } = new();

    /// <summary>Resultado completo del orquestador. Null si aún no se analizó.</summary>
    public OrchestrationResult? Resultado { get; init; }

    /// <summary>Mensaje de error global, si aplica.</summary>
    public string? Error { get; init; }
}
