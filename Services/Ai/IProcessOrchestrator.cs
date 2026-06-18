using app_tramites.Models.ModelAi;
using Microsoft.AspNetCore.Identity;

namespace app_tramites.Services.Ai;

/// <summary>
/// Orquestador del motor IA multi-paso para el Área Studio.
/// Dado un <see cref="ProcessCase"/> ya importado, ejecuta:
/// <list type="number">
///   <item><description>Un paso CLASIFICADOR que selecciona el <see cref="Process"/> adecuado.</description></item>
///   <item><description>El runner de <see cref="ProcessStep"/> del proceso seleccionado, en orden.</description></item>
/// </list>
/// Cada paso persiste un <see cref="StepExecution"/> y su <see cref="Usage"/> en BD.
/// </summary>
public interface IProcessOrchestrator
{
    /// <summary>
    /// Ejecuta la orquestación completa sobre el caso indicado.
    /// </summary>
    /// <param name="caseCode">Identificador del caso a analizar.</param>
    /// <param name="user">Usuario que solicita el análisis (para filtrar agentes por autorización).</param>
    /// <param name="processCodeOverride">
    /// Si se especifica, omite el clasificador y usa directamente este proceso.
    /// </param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Resultado completo de la orquestación con pasos ejecutados y tokens.</returns>
    Task<OrchestrationResult> RunAsync(
        Guid caseCode,
        IdentityUser? user,
        string? processCodeOverride = null,
        CancellationToken cancellationToken = default);
}
