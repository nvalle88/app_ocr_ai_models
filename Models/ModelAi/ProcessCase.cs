namespace app_tramites.Models.ModelAi;

public partial class ProcessCase
{
    public Guid CaseCode { get; set; }

    public string DefinitionCode { get; set; } = null!;

    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public string State { get; set; } = null!;

    public virtual ICollection<DataFile> DataFile { get; set; } = new List<DataFile>();

    public virtual ICollection<CaseReview> CaseReviews { get; set; } = new List<CaseReview>();
    public virtual ICollection<FinalResponseResult> FinalResponseResults { get; set; } = new List<FinalResponseResult>();

    public virtual ICollection<Note> Notes { get; set; } = new List<Note>();

    public virtual Process DefinitionCodeNavigation { get; set; } = null!;
}
