using System.ComponentModel.DataAnnotations.Schema;

namespace app_tramites.Models.ModelAi;

public partial class OPAIModelPrompt
{
    public string ModelCode { get; set; } = null!;

    public string PromptCode { get; set; } = null!;

    public int Order { get; set; }

    public bool IsDefault { get; set; }

    public int TypeAgent { get; set; }

    public string? MetadataJson { get; set; }

    public string? ButtonId { get; set; }
    public string? ClassName { get; set; }
    public string? Tittle { get; set; }
    public string? Icon { get; set; }
    public string? IconMenu { get; set; }
    public string? AriaLabel { get; set; }
    public string? NameButton { get; set; }

    public virtual Agent ModelCodeNavigation { get; set; } = null!;

    public virtual OPAIPrompt PromptCodeNavigation { get; set; } = null!;

    [ForeignKey("TypeAgent")]
    public virtual Catalog TypeAgentNavigation { get; set; } = null!;
}
