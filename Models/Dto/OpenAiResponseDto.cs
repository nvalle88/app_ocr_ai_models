namespace app_tramites.Models.Dto;

public class OpenAiResponseDto
{
    public int DataFileId { get; set; }
    public int StepOrder { get; set; }
    public string RequestJson { get; set; } = null!;
    public string ResultText { get; set; } = null!;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
}
