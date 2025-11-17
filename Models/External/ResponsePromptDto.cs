namespace app_tramites.Models.External;

public class ResponsePromptDto
{
    public string ResponseText { get; set; } = null!;

    public string? MetadataJson { get; set; }

    public Guid CaseCode { get; set; }
    public string ProccessName { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string RequestText { get; set; } = null!;
    public DateTime CreatedDate { get; set; }
}
