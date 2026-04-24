namespace app_ocr_ai_models.Models.ViewModel;

public class AgentPromptItemDto
{
    public string PromptCode { get; set; } = "";
    public int Order { get; set; } = 1;
    public bool IsDefault { get; set; }
}

public class SaveAgenteFullDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string ConfigCode { get; set; } = "";
    public int VersionNumber { get; set; } = 1;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public List<AgentPromptItemDto> Prompts { get; set; } = new();
    public List<string> Procesos { get; set; } = new();
}

public class SaveProcesoFullDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<string> Agentes { get; set; } = new();
}

public class SavePoliciaFullDto
{
    public string Code { get; set; } = "";
    public string PolicyName { get; set; } = "";
    public List<int> AgentProcessIds { get; set; } = new();
}

public class SaveUsuarioFullDto
{
    public string? Id { get; set; }
    public string Email { get; set; } = "";
    public string? Password { get; set; }
    public List<string> PolicyCodes { get; set; } = new();
}
