namespace app_tramites.Models.ViewModel;

public class ViewCreateCase
{
    public Guid CaseCode { get; set; }
    public string DefinitionCode { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string NameProccess { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
}
