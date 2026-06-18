using System;

namespace app_tramites.Models.ModelAi;

// REQ-019 T1: registro de cada invocación de tool durante una ejecución de paso
public partial class ToolInvocation
{
    public long InvocationId { get; set; }

    /// <summary>FK a StepExecution.ExecutionId (bigint). Ata la invocación al paso que la originó.</summary>
    public long ExecutionId { get; set; }

    /// <summary>Referencia blanda a OPAITool.Code (sin FK formal para permitir auditoría tras desactivación).</summary>
    public string ToolCode { get; set; } = null!;

    public string? RequestJson { get; set; }

    public string? ResponseJson { get; set; }

    public bool IsError { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public virtual StepExecution Execution { get; set; } = null!;
}
