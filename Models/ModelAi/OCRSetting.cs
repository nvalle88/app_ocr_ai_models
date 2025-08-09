using System;
using System.Collections.Generic;

namespace app_tramites.Models.ModelAi;

public partial class OCRSetting
{
    public string SettingCode { get; set; } = null!;

    public string PlatformCode { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? ModelId { get; set; }

    public string Endpoint { get; set; } = null!;

    public string? ApiKey { get; set; }

    public DateTime CreatedAt { get; set; }

    public bool IsActive { get; set; }

    public virtual OCRPlatform PlatformCodeNavigation { get; set; } = null!;
}
