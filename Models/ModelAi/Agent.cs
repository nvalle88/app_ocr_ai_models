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

    public virtual ICollection<ProcessStep> ProcessStep { get; set; } = new List<ProcessStep>();

    public virtual ICollection<StepExecution> StepExecution { get; set; } = new List<StepExecution>();
    public virtual ICollection<AgentProcess> AgentProcesses { get; set; } = new List<AgentProcess>();
}
