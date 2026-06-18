namespace app_tramites.Services.Ai;

/// <summary>
/// Resultado de la orquestación completa de un caso.
/// Incluye el resultado del clasificador y los resultados por paso.
/// </summary>
public sealed class OrchestrationResult
{
    /// <summary>Código del caso analizado.</summary>
    public Guid CaseCode { get; init; }

    /// <summary>Código del proceso seleccionado por el clasificador.</summary>
    public string ProcessCode { get; init; } = string.Empty;

    /// <summary>Texto de clasificación producido por el primer paso.</summary>
    public string ClassificationText { get; init; } = string.Empty;

    /// <summary>Resultados de cada paso ejecutado, en orden.</summary>
    public IReadOnlyList<StepResult> Steps { get; init; } = Array.Empty<StepResult>();

    /// <summary>Texto final del último paso ejecutado.</summary>
    public string FinalText => Steps.LastOrDefault()?.ResponseText ?? ClassificationText;

    /// <summary>Indica si la orquestación terminó sin errores.</summary>
    public bool Success { get; init; }

    /// <summary>Mensaje de error, si aplica.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Resultado de un paso de ejecución individual.
/// </summary>
public sealed class StepResult
{
    /// <summary>Orden del paso.</summary>
    public int StepOrder { get; init; }

    /// <summary>Nombre del paso.</summary>
    public string? StepName { get; init; }

    /// <summary>Código del agente usado en el paso.</summary>
    public string ModelCode { get; init; } = string.Empty;

    /// <summary>Texto de la petición enviada al modelo.</summary>
    public string RequestText { get; init; } = string.Empty;

    /// <summary>Texto de la respuesta del modelo.</summary>
    public string ResponseText { get; init; } = string.Empty;

    /// <summary>ID del StepExecution persistido en BD.</summary>
    public long ExecutionId { get; init; }

    /// <summary>Estado del paso.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Tokens de prompt usados.</summary>
    public int PromptTokens { get; init; }

    /// <summary>Tokens de completado usados.</summary>
    public int CompletionTokens { get; init; }
}
