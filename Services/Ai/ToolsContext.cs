using app_tramites.Models.ModelAi;

namespace app_tramites.Services.Ai;

// ============================================================
// REQ-019 T5 — Contexto de tools para el motor Claude.
// ============================================================

/// <summary>
/// Contexto de tools que el motor Claude usa durante el tool-use loop.
/// Se pasa a <see cref="IAiCompletionService.CompleteWithToolsAsync"/> para
/// que el modelo pueda invocar tools en la respuesta.
/// </summary>
public sealed class ToolsContext
{
    /// <summary>
    /// Lista de tools disponibles para el agente (filtradas por <c>OPAIModelTool</c>
    /// con <c>IsEnabled=true</c>).
    /// </summary>
    public IReadOnlyList<OPAITool> AvailableTools { get; init; } =
        Array.Empty<OPAITool>();

    /// <summary>
    /// Código del agente en curso (para verificación D2 en el executor).
    /// </summary>
    public string AgentCode { get; init; } = string.Empty;

    /// <summary>
    /// ID del <c>StepExecution</c> padre para persistir <c>ToolInvocation</c>.
    /// </summary>
    public long ExecutionId { get; init; }

    /// <summary>
    /// Cédula/identidad del titular del Caso (para el guardián anti-IDOR D4).
    /// Puede ser <see langword="null"/> si aún no fue resuelta.
    /// </summary>
    public string? CaseIdentity { get; init; }

    /// <summary>
    /// Política de selección de tool del agente: 'auto' | 'any' | 'none'.
    /// Se mapea a <c>ToolChoice</c> del SDK Anthropic.
    /// </summary>
    public string ToolChoice { get; init; } = "auto";

    /// <summary>
    /// Callback para emitir eventos de tool_use/tool_result al stream SSE.
    /// Si es <see langword="null"/>, no se emiten eventos al stream.
    /// </summary>
    public Func<ToolStreamEvent, Task>? StreamEventCallback { get; init; }
}

/// <summary>
/// Evento de tool emitido durante el tool-use loop para el stream SSE.
/// </summary>
public sealed class ToolStreamEvent
{
    /// <summary>Tipo de evento: <c>"tool_use"</c> | <c>"tool_result"</c>.</summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>Nombre de la tool invocada.</summary>
    public string ToolName { get; init; } = string.Empty;

    /// <summary>
    /// Input resumido de la tool (para <c>tool_use</c>): JSON truncado para la UI.
    /// </summary>
    public string? InputSummary { get; init; }

    /// <summary>
    /// Resultado de la tool (para <c>tool_result</c>):
    /// <c>"ok"</c> si fue exitoso, <c>"error"</c> si falló.
    /// </summary>
    public string? ResultStatus { get; init; }
}
