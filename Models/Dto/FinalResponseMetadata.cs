namespace app_tramites.Models.Dto;

public class FinalResponseMetadata
{
    public int? MaxTokens { get; set; }
    public double? Temperature { get; set; }
    public string? Language { get; set; }
    public string? CustomInstructions { get; set; }
}
