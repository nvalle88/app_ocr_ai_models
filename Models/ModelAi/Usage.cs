using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace app_tramites.Models.ModelAi;

public partial class Usage
{
    [Key]
    public long Id { get; set; }

    public long? FinalResponseResultId { get; set; }

    public int CompletionTokens { get; set; }

    public int PromptTokens { get; set; }

    public DateTime CreatedDate { get; set; }

    [ForeignKey("FinalResponseResultId")]
    public virtual FinalResponseResult FinalResponseResult { get; set; } = null!;
}
