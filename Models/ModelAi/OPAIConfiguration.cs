using System;
using System.Collections.Generic;

namespace app_tramites.Models.ModelAi;

public partial class OPAIConfiguration
{
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string EndpointUrl { get; set; } = null!;

    public string ApiKey { get; set; } = null!;

    public string ConfigType { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public DateTime ModifiedDate { get; set; }

    public bool IsActive { get; set; }

    public string? Notes { get; set; }

    public virtual ICollection<Agent> Agent { get; set; } = new List<Agent>();
}
