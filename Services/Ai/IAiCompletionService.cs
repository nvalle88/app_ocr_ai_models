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

    /// <summary>
    /// Ejecuta una llamada de completado en modo streaming, emitiendo <see cref="AiStreamChunk"/>
    /// a medida que el modelo genera tokens.
    /// </summary>
    /// <remarks>
    /// La secuencia de chunks sigue el orden:
    /// <list type="number">
    ///   <item>Cero o más chunks <see cref="AiStreamChunkType.Thinking"/> (solo Claude con thinking activo).</item>
    ///   <item>Uno o más chunks <see cref="AiStreamChunkType.Text"/>.</item>
    ///   <item>Un chunk <see cref="AiStreamChunkType.Done"/> con los totales de tokens.</item>
    /// </list>
    /// Garantía: el último chunk emitido siempre es <c>Done</c>.
    /// </remarks>
    /// <param name="request">Parámetros de la llamada (system, user, maxTokens, opciones).</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Secuencia asíncrona de chunks del stream.</returns>
    IAsyncEnumerable<AiStreamChunk> StreamAsync(
        AiCompletionRequest request,
        CancellationToken ct = default);
}
