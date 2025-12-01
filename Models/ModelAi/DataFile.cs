namespace app_tramites.Models.ModelAi;

public partial class DataFile
{
    public int Id { get; set; }

    public bool IsFileUri { get; set; }

    public string FileUri { get; set; } = null!;

    public string Text { get; set; } = null!;

    public Guid CaseCode { get; set; }

    public DateTime CreatedDate { get; set; }

    public string OriginalName { get; set; } = string.Empty;

    public virtual ProcessCase CaseCodeNavigation { get; set; } = null!;
}
