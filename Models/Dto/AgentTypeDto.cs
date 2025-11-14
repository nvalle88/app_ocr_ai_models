namespace app_tramites.Models.Dto;

public class AgentTypeDto
{
    /// <summary>
    /// Codigo del Catalogo
    /// </summary>
    public string Code { get; set; }
    
    /// <summary>
    /// Id del Catalog
    /// </summary>
    public int CatalogId { get; set; }
    
    /// <summary>
    /// Code del Proccess
    /// </summary>
    public string DefinitionCode { get; set; }

    /// <summary>
    /// Campo Code del Agent
    /// </summary>
    public string AgentCode { get; set; }

}
