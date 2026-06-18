namespace app_ocr_ai_models.Areas.Studio.Models;

// ============================================================
// REQ-019 T7 — ViewModels del ChatController (streaming SSE).
// ============================================================

/// <summary>
/// Datos de la vista principal del chat de Studio.
/// </summary>
public sealed class ChatIndexViewModel
{
    /// <summary>CaseCode del caso a consultar (puede ser Guid.Empty si aún no se eligió).</summary>
    public Guid CaseCode { get; init; }

    /// <summary>Mensaje de error de inicialización, si aplica.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// Request del endpoint SSE de streaming de chat.
/// Se envía como JSON vía fetch/POST desde la vista.
/// </summary>
public sealed class ChatStreamRequest
{
    /// <summary>CaseCode del caso sobre el que se chatea.</summary>
    public Guid CaseCode { get; init; }

    /// <summary>Mensaje del usuario.</summary>
    public string Message { get; init; } = string.Empty;
}
