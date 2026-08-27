using app_ocr_ai_models.Data;
using app_tramites.Services.Ai;
using app_tramites.Models.ModelAi;

namespace app_ocr_ai_models.Areas.Studio.Controllers;

/// <summary>
/// Apuntar lo que costo una llamada al modelo.
///
/// Existe porque la contabilidad de tokens SOLO la escribia ProcessOrchestrator,
/// y el pipeline del Studio no pasa por ahi: corre desde los controladores
/// (Clasificacion, Expediente, Auditoria, Resolucion). Resultado medido:
/// AGENTE_CLAUDE tenia 27 filas en dbo.Usage frente a 51 en StepExecution —
/// casi la mitad de las llamadas no se contaban— y las columnas de cache
/// estaban en cero incluso donde habria habido cache.
///
/// Sin esto no se puede responder "¿cuanto cuesta un caso?" ni comprobar si un
/// cambio de prompt o de cache mejoro algo.
/// </summary>
internal static class UsoDelModelo
{
    public static async Task ApuntarAsync(OCRDbContext db, long executionId, AiCompletionResult? res)
    {
        if (res == null) return;

        db.Usage.Add(new Usage
        {
            ExecutionId         = executionId,
            PromptTokens        = res.PromptTokens,
            CompletionTokens    = res.CompletionTokens,
            ThinkingTokens      = res.ThinkingTokens,
            CacheReadTokens     = res.CacheReadTokens,
            CacheCreationTokens = res.CacheCreationTokens,
            CreatedDate         = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
