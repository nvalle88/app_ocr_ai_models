using app_tramites.Models.ModelAi;

namespace app_tramites.Models.ViewModel;

public class ViewCaseDetails
{
    public bool HasChat { get; set; } = false;
    public bool HasButton { get; set; } = false;
    public ProcessCase ProcessCase { get; set; } = new ProcessCase();
}
