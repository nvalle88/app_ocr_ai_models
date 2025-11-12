namespace app_tramites.Models.ViewModel;

public class ViewAgentProcess
{
    public string ProcessId { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string? Description { get; set; } = string.Empty;
    public int? ProcessAgentId { get; set; }
}
