using System;
using System.Collections.Generic;

namespace app_tramites.Models.ModelAi;

public partial class OPAIPrompt
{
    public string Code { get; set; } = null!;

    public string Content { get; set; } = null!;

    public int VersionNumber { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime ModifiedDate { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<OPAIModelPrompt> OPAIModelPrompt { get; set; } = new List<OPAIModelPrompt>();
}
