namespace app_tramites.Services.Ai;

// ============================================================
// REQ-019 T7 — DTO de chunk de streaming para SSE.
// ============================================================

/// <summary>
/// Tipo de evento en el stream de respuesta IA.
/// </summary>
public enum AiStreamChunkType
{
    /// <summary>Delta de texto generado por el modelo.</summary>
    Text,

    /// <summary>Delta del bloque de razonamiento interno (Claude extended thinking).</summary>
    Thinking,

    /// <summary>Fin del stream: señal de cierre con métricas de tokens.</summary>
    Done
}

/// <summary>
/// Unidad de datos transmitida por <see cref="IAiCompletionService.StreamAsync"/>
/// hacia el endpoint SSE.
/// </summary>
/// <remarks>
/// Cada chunk representa un evento del stream del modelo:
/// <list type="bullet">
///   <item><see cref="AiStreamChunkType.Text"/> — delta de texto acumulable en la UI.</item>
///   <item><see cref="AiStreamChunkType.Thinking"/> — delta de razonamiento (<c>thinking</c>
///     de Claude extended thinking). Puede no llegar si el modelo no tiene thinking activo.</item>
///   <item><see cref="AiStreamChunkType.Done"/> — fin del stream; incluye totales de tokens.</item>
/// </list>
/// </remarks>
public sealed class AiStreamChunk
{
    /// <summary>Tipo de evento.</summary>
    public AiStreamChunkType Type { get; init; }

    /// <summary>
    /// Delta de texto (type = Text) o de razonamiento (type = Thinking).
    /// Vacío para el evento Done.
    /// </summary>
    public string Delta { get; init; } = string.Empty;

    // ── Campos de métricas: solo se rellenan en el chunk Done ──────────────

    /// <summary>Tokens de entrada (prompt). Solo en Done.</summary>
    public int PromptTokens { get; init; }

    /// <summary>Tokens de salida (completion). Solo en Done.</summary>
    public int CompletionTokens { get; init; }

    /// <summary>Tokens de thinking (Claude extended thinking). Solo en Done; null si no aplica.</summary>
    public int? ThinkingTokens { get; init; }

    // ── Factoría rápida ───────────────────────────────────────────────────

    /// <summary>Crea un chunk de tipo Text.</summary>
    /// <param name="delta">Fragmento de texto.</param>
    public static AiStreamChunk TextChunk(string delta) =>
        new() { Type = AiStreamChunkType.Text, Delta = delta };

    /// <summary>Crea un chunk de tipo Thinking.</summary>
    /// <param name="delta">Fragmento de razonamiento.</param>
    public static AiStreamChunk ThinkingChunk(string delta) =>
        new() { Type = AiStreamChunkType.Thinking, Delta = delta };

    /// <summary>Crea el chunk final Done con métricas de tokens.</summary>
    /// <param name="promptTokens">Tokens de entrada.</param>
    /// <param name="completionTokens">Tokens de salida.</param>
    /// <param name="thinkingTokens">Tokens de thinking (Claude); null si no aplica.</param>
    public static AiStreamChunk DoneChunk(int promptTokens, int completionTokens, int? thinkingTokens = null) =>
        new()
        {
            Type             = AiStreamChunkType.Done,
            PromptTokens     = promptTokens,
            CompletionTokens = completionTokens,
            ThinkingTokens   = thinkingTokens
        };
}
