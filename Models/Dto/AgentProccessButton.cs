namespace app_tramites.Models.Dto;

public class AgentProccessButton
{
    public string? ButtonId { get; set; }
    public string? ClassName { get; set; }
    public string? Tittle { get; set; }
    public string? Icon { get; set; }
    public string? IconMenu { get; set; }
    public string? AriaLabel { get; set; }
    public string? Name { get; set; }
    public int? AgentProcessId { get; set; }
    public string ModelCode { get; set; } = string.Empty;
    public string PromptCode { get; set; } = string.Empty;
}
