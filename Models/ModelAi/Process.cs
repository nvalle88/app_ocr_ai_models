using app_tramites.Models.ViewModel;
using System;
using System.Collections.Generic;

namespace app_tramites.Models.ModelAi;

public partial class Process
{
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public virtual ICollection<ProcessCase> ProcessCase { get; set; } = new List<ProcessCase>();

    public virtual ICollection<ProcessStep> ProcessStep { get; set; } = new List<ProcessStep>();
}
