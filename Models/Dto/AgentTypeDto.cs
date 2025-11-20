using app_tramites.Models.ModelAi;

namespace app_tramites.Models.Dto;

public class AgentTypeDto
{
    /// <summary>
    /// Codigo del Catalogo
    /// </summary>
    public string Code { get; set; } = string.Empty;
    
    /// <summary>
    /// Id del Catalog
    /// </summary>
    public int CatalogId { get; set; }

    /// <summary>
    /// Code del Proccess
    /// </summary>
    public string DefinitionCode { get; set; } = string.Empty;

    /// <summary>
    /// Campo Code del Agent
    /// </summary>
    public string AgentCode { get; set; } = string.Empty;

    /// <summary>
    /// Datos del prompt para el modelo de OpenAI
    /// </summary>
    public List<OPAIModelPrompt> OPAIModelPrompt { get; set; } = [];

}
