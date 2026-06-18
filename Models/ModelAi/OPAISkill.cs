using System;
using System.Collections.Generic;

namespace app_tramites.Models.ModelAi;

// REQ-019 T1: catálogo de skills (capacidades) disponibles para los agentes IA
public partial class OPAISkill
{
    public string Code { get; set; } = null!;

    /// <summary>Tipo de skill: 'anthropic' | 'custom'</summary>
    public string SkillType { get; set; } = null!;

    /// <summary>ID externo de la skill (ej. ID en Anthropic).</summary>
    public string SkillId { get; set; } = null!;

    public string? SkillVersion { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public int VersionNumber { get; set; }

    public DateTime CreatedDate { get; set; }

    public virtual ICollection<OPAIModelSkill> OPAIModelSkill { get; set; } = new List<OPAIModelSkill>();
}
