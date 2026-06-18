namespace app_tramites.Services.Ai;

/// <summary>
/// Abstracción de proveedor IA para completado de texto (system + user → texto + tokens).
/// La implementación concreta se selecciona en tiempo de ejecución según
/// <see cref="app_tramites.Models.ModelAi.OPAIConfiguration.Provider"/>.
/// Preparado para extenderse a tool-calling en T5 (se añadirá sobrecarga con tools).
/// </summary>
public interface IAiCompletionService
{
    /// <summary>
    /// Ejecuta una llamada de completado al proveedor IA configurado.
    /// </summary>
    /// <param name="request">Parámetros de la llamada (system, user, maxTokens, opciones).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Texto generado y métricas de uso de tokens.</returns>
    Task<AiCompletionResult> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken cancellationToken = default);
}
