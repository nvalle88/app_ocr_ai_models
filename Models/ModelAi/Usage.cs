using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace app_tramites.Models.ModelAi;

public partial class Usage
{
    [Key]
    public long Id { get; set; }

    // RÉGIMEN DOBLE (D1/D5): FinalResponseResultId se conserva para el flujo A-HOSP legacy.
    // ExecutionId es el FK canónico para el motor nuevo (pipeline multi-paso).
    public long? FinalResponseResultId { get; set; }

    /// <summary>
    /// REQ-019 T1: FK a StepExecution.ExecutionId (long/bigint). Nullable para compatibilidad
    /// con registros de Usage del flujo legacy que no tienen paso asociado.
    /// </summary>
    public long? ExecutionId { get; set; }

    public int CompletionTokens { get; set; }

    public int PromptTokens { get; set; }

    public DateTime CreatedDate { get; set; }

    // REQ-019 T1: tokens específicos de Claude (nullable — compatibles con Azure OpenAI)
    public int? ThinkingTokens { get; set; }

    public int? CacheReadTokens { get; set; }

    public int? CacheCreationTokens { get; set; }

    [ForeignKey("FinalResponseResultId")]
    public virtual FinalResponseResult? FinalResponseResult { get; set; }

    /// <summary>Navegación al paso de ejecución (motor nuevo). Null en registros legacy.</summary>
    [ForeignKey("ExecutionId")]
    public virtual StepExecution? Execution { get; set; }
}
