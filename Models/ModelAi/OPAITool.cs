using System;
using System.Collections.Generic;

namespace app_tramites.Models.ModelAi;

// REQ-019 T1: catálogo de herramientas (tools) disponibles para los agentes IA
public partial class OPAITool
{
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>JSON Schema de los parámetros de entrada de la tool.</summary>
    public string? InputSchema { get; set; }

    public bool Strict { get; set; }

    /// <summary>Tipo de binding: 'InternalApi' | 'Sql' | 'Zendesk' | 'Static'</summary>
    public string BindingType { get; set; } = null!;

    /// <summary>JSON de configuración del binding específico.</summary>
    public string? BindingConfig { get; set; }

    public bool IsActive { get; set; }

    public int VersionNumber { get; set; }

    public DateTime CreatedDate { get; set; }

    public virtual ICollection<OPAIModelTool> OPAIModelTool { get; set; } = new List<OPAIModelTool>();
}
