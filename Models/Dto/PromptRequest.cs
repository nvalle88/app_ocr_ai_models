namespace app_tramites.Models.Dto;

public class PromptRequest
{
    public Guid CaseCode { get; set; }
    public string Message { get; set; } = "";
    public List<string> FileUrls { get; set; } = new();
    public int Origin { get; set; }
}
