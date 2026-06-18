using app_tramites.Models.ViewModel;
using System;
using System.Collections.Generic;

namespace app_tramites.Models.ModelAi;

public partial class Agent
{
    public string Code { get; set; } = null!;

    public string ConfigCode { get; set; } = null!;

    public string Name { get; set; } = null!;

    public int VersionNumber { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime ModifiedDate { get; set; }

    public bool IsActive { get; set; }

    public virtual OPAIConfiguration AgentConfig { get; set; } = null!;

    public virtual ICollection<OPAIModelPrompt> OPAIModelPrompt { get; set; } = new List<OPAIModelPrompt>();

    //public virtual ICollection<ProcessStep> ProcessStep { get; set; } = new List<ProcessStep>();

    //public virtual ICollection<StepExecution> StepExecution { get; set; } = new List<StepExecution>();
    public virtual ICollection<AgentProcess> AgentProcesses { get; set; } = new List<AgentProcess>();

    // REQ-019 T1: columnas para soporte Claude AI
    public string? ModelId { get; set; }

    public string? SystemPrompt { get; set; }

    public int? MaxTokens { get; set; }

    /// <summary>Modo de thinking de Claude: 'adaptive' | 'disabled' | 'enabled'</summary>
    public string? ThinkingMode { get; set; }

    /// <summary>Nivel de esfuerzo: 'low' | 'medium' | 'high' | 'xhigh' | 'max'</summary>
    public string? Effort { get; set; }

    /// <summary>decimal(4,2) — evita imprecisión binaria de float para valores como 0.7, 1.0.</summary>
    public decimal? Temperature { get; set; }

    /// <summary>Política de selección de tool: 'auto' | 'any' | 'none' | 'tool'</summary>
    public string ToolChoice { get; set; } = "auto";

    public virtual ICollection<OPAIModelTool> OPAIModelTool { get; set; } = new List<OPAIModelTool>();

    public virtual ICollection<OPAIModelSkill> OPAIModelSkill { get; set; } = new List<OPAIModelSkill>();

    public virtual ICollection<ProcessStep> ProcessStep { get; set; } = new List<ProcessStep>();

    public virtual ICollection<StepExecution> StepExecution { get; set; } = new List<StepExecution>();
}
