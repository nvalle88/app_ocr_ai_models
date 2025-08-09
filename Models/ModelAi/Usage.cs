using System;
using System.Collections.Generic;

namespace app_tramites.Models.ModelAi;

public partial class Usage
{
    public long Id { get; set; }

    public long ExecutionId { get; set; }

    public int CompletionTokens { get; set; }

    public int PromptTokens { get; set; }

    public DateTime CreatedDate { get; set; }

    public virtual StepExecution Execution { get; set; } = null!;
}
