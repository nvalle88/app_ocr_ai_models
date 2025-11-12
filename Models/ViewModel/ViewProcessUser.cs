namespace app_tramites.Models.ViewModel;

public class ViewProcessUser
{
    public bool Success { get; set; } = false;
    public List<ViewAgentProcess> Processes { get; set; } = [];
}
