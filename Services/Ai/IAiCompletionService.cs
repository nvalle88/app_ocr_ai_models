using app_tramites.Services.Ai.Tools;

namespace app_tramites.Services.Ai;

/// <summary>
/// Abstracción de proveedor IA para completado de texto (system + user → texto + tokens).
/// La implementación concreta se selecciona en tiempo de ejecución según
/// <see cref="app_tramites.Models.ModelAi.OPAIConfiguration.Provider"/>.
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

    /// <summary>
    /// Ejecuta el tool-use loop: pasa las tools al modelo, procesa los bloques
    /// <c>tool_use</c> llamando al <see cref="IToolExecutor"/>, devuelve
    /// <c>tool_result</c> y repite hasta <c>end_turn</c>.
    /// </summary>
    /// <remarks>
    /// Solo es funcional en <see cref="ClaudeCompletionService"/>; la implementación
    /// Azure OpenAI puede lanzar <see cref="NotSupportedException"/> si no lo soporta.
    /// </remarks>
    /// <param name="request">Parámetros de la llamada (system, user, maxTokens).</param>
    /// <param name="toolsContext">Contexto de tools: catálogo, executor, guards y callback SSE.</param>
    /// <param name="toolExecutor">Executor que despacha cada invocación de tool.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Texto final del modelo y métricas acumuladas de tokens.</returns>
    Task<AiCompletionResult> CompleteWithToolsAsync(
        AiCompletionRequest request,
        ToolsContext toolsContext,
        IToolExecutor toolExecutor,
        CancellationToken ct = default);
}
