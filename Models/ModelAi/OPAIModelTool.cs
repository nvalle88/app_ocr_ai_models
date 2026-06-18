namespace app_tramites.Models.ModelAi;

// REQ-019 T1: N:M entre Agent y OPAITool (PK compuesta ModelCode+ToolCode)
public partial class OPAIModelTool
{
    public string ModelCode { get; set; } = null!;

    public string ToolCode { get; set; } = null!;

    /// <summary>Orden de la tool dentro del agente. Columna SQL: [Order] (reservada en T-SQL).</summary>
    public int SortOrder { get; set; }

    public bool IsEnabled { get; set; }

    public virtual Agent ModelCodeNavigation { get; set; } = null!;

    public virtual OPAITool ToolCodeNavigation { get; set; } = null!;
}
