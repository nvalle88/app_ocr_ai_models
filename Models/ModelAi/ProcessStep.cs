using System;
using System.Collections.Generic;

namespace app_tramites.Models.ModelAi;

public enum InputSourceType
{
    Original = 0,
    PreviousSteps = 1,
    Both = 2
}

public partial class ProcessStep
{
    public string ProcessCode { get; set; } = null!;

    public int StepOrder { get; set; }

    public string ModelCode { get; set; } = null!;

    public string? StepName { get; set; }

    public int StepsToInclude { get; set; }

    public InputSourceType SourceType { get; set; } = InputSourceType.PreviousSteps;

    public bool AggregateExecution { get; set; }

    public virtual Agent ModelCodeNavigation { get; set; } = null!;

    public virtual Process ProcessCodeNavigation { get; set; } = null!;
}
