namespace app_tramites.Models.ModelAi;

// REQ-019 T1: N:M entre Agent y OPAISkill (PK compuesta ModelCode+SkillCode)
public partial class OPAIModelSkill
{
    public string ModelCode { get; set; } = null!;

    public string SkillCode { get; set; } = null!;

    /// <summary>Orden de la skill dentro del agente. Columna SQL: [Order] (reservada en T-SQL).</summary>
    public int SortOrder { get; set; }

    public bool IsEnabled { get; set; }

    public virtual Agent ModelCodeNavigation { get; set; } = null!;

    public virtual OPAISkill SkillCodeNavigation { get; set; } = null!;
}
