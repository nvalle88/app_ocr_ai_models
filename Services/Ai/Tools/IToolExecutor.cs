namespace app_tramites.Services.Ai.Tools;

// ============================================================
// REQ-019 T5 — Executor genérico de tools.
// ============================================================

/// <summary>
/// Ejecuta una tool del catálogo: lee el <c>BindingConfig</c>, arma el request HTTP,
/// inyecta el token Saludsa, y persiste el <see cref="app_tramites.Models.ModelAi.ToolInvocation"/>.
/// </summary>
public interface IToolExecutor
{
    /// <summary>
    /// Invoca la tool indicada con los parámetros dados, aplica los guardas D2 y D4,
    /// y persiste la auditoría en <c>ToolInvocation</c>.
    /// </summary>
    /// <param name="toolCode">Código de la tool a invocar (PK de <c>OPAITool</c>).</param>
    /// <param name="agentCode">Código del agente que solicita la invocación (para D2).</param>
    /// <param name="toolInput">
    /// Diccionario de parámetros tal como los envió el modelo (del bloque <c>tool_use</c>).
    /// </param>
    /// <param name="executionId">
    /// FK al <c>StepExecution</c> padre (para persistir <c>ToolInvocation</c>).
    /// </param>
    /// <param name="caseIdentity">
    /// Cédula/id del titular del Caso. Se usa por el guardián anti-IDOR (D4).
    /// Puede ser <see langword="null"/> si la identidad aún no fue resuelta.
    /// </param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>
    /// JSON string con la respuesta de la API, listo para enviar como <c>tool_result</c>.
    /// </returns>
    /// <exception cref="UnauthorizedAccessException">Si el guardián D2 o D4 deniega la invocación.</exception>
    /// <exception cref="NotImplementedException">Si el <c>BindingType</c> es <c>Mcp</c> (futuro).</exception>
    Task<string> ExecuteAsync(
        string toolCode,
        string agentCode,
        IReadOnlyDictionary<string, object?> toolInput,
        long executionId,
        string? caseIdentity = null,
        CancellationToken ct = default);
}
