namespace app_tramites.Services.Ai;

/// <summary>
/// Opciones de completado comunes a todos los proveedores IA.
/// Los campos opcionales se ignoran si el proveedor no los soporta.
/// </summary>
public sealed class AiCompletionRequest
{
    /// <summary>Prompt del sistema (instrucción global del agente).</summary>
    public string SystemPrompt { get; init; } = string.Empty;

    /// <summary>Texto del usuario (contexto + documentos OCR).</summary>
    public string UserMessage { get; init; } = string.Empty;

    /// <summary>Máximo de tokens en la respuesta.</summary>
    public int MaxTokens { get; init; } = 1024;

    /// <summary>Temperatura de muestreo (null = usar defecto del proveedor).</summary>
    public decimal? Temperature { get; init; }

    /// <summary>
    /// Modo de thinking de Claude: 'adaptive' | 'disabled' | 'enabled'.
    /// Ignorado en AzureOpenAI.
    /// </summary>
    public string? ThinkingMode { get; init; }
}
