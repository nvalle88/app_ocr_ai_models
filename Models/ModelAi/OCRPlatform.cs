namespace app_tramites.Models.ModelAi;

public partial class OCRPlatform
{
    public string PlatformCode { get; set; } = null!;

    public string Name { get; set; } = null!;

    public decimal PricePerPage { get; set; }

    public int? MaxFileSizeMB { get; set; }

    public string? LanguageSupport { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<OCRSetting> OCRSetting { get; set; } = new List<OCRSetting>();
}
