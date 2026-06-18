namespace app_tramites.Services.Ai;

/// <summary>
/// Resultado de una llamada al proveedor IA: texto + conteo de tokens.
/// Los campos de tokens Claude son nulos cuando el proveedor es AzureOpenAI.
/// </summary>
public sealed class AiCompletionResult
{
    /// <summary>Texto generado por el modelo.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Tokens de entrada (prompt).</summary>
    public int PromptTokens { get; init; }

    /// <summary>Tokens de salida (completion).</summary>
    public int CompletionTokens { get; init; }

    /// <summary>Tokens de thinking (Claude extended thinking). Null en AzureOpenAI.</summary>
    public int? ThinkingTokens { get; init; }

    /// <summary>Tokens leídos de caché (Claude prompt caching). Null en AzureOpenAI.</summary>
    public int? CacheReadTokens { get; init; }

    /// <summary>Tokens escritos a caché (Claude prompt caching). Null en AzureOpenAI.</summary>
    public int? CacheCreationTokens { get; init; }
}
