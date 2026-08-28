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

    // Lo que el agente necesita para poder trabajar. Sin esto la pantalla
    // guardaba agentes que fallaban al primer uso.
    public string? ModelId { get; set; }
    public int? MaxTokens { get; set; }
    public string? SystemPrompt { get; set; }
    public string? ThinkingMode { get; set; }
    public string? ToolChoice { get; set; }

    /// <summary>Las tools que puede usar, en orden.</summary>
    public List<AgentToolItemDto> Tools { get; set; } = new();

    /// <summary>Las skills que lleva encima.</summary>
    public List<AgentToolItemDto> Skills { get; set; } = new();
}

/// <summary>Un enlace agente -> tool/skill, con su orden y si esta habilitado.</summary>
public class AgentToolItemDto
{
    public string Code { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>Una tool del catalogo del motor.</summary>
public class SaveToolFullDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? InputSchema { get; set; }
    public string BindingType { get; set; } = "Sql";
    public string? BindingConfig { get; set; }
    public bool Strict { get; set; }
    public bool IsActive { get; set; } = true;
    public int VersionNumber { get; set; } = 1;

    /// <summary>Agentes que la tendran disponible.</summary>
    public List<string> Agentes { get; set; } = new();
}

/// <summary>Una skill del catalogo.</summary>
public class SaveSkillFullDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string SkillType { get; set; } = "";
    public string SkillId { get; set; } = "";
    public string? SkillVersion { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int VersionNumber { get; set; } = 1;
    public List<string> Agentes { get; set; } = new();
}

/// <summary>Los pasos de un proceso, en orden.</summary>
public class SavePasosDto
{
    public string ProcessCode { get; set; } = "";
    public List<PasoItemDto> Pasos { get; set; } = new();
}

public class PasoItemDto
{
    public int StepOrder { get; set; }
    public string ModelCode { get; set; } = "";
    public string? StepName { get; set; }
    public int StepsToInclude { get; set; }
    public int SourceType { get; set; }
    public bool AggregateExecution { get; set; }
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
