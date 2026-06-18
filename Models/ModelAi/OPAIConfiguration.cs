namespace app_tramites.Models.ModelAi;

public partial class OPAIConfiguration
{
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string EndpointUrl { get; set; } = null!;

    public string ApiKey { get; set; } = null!;

    public string ConfigType { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public DateTime ModifiedDate { get; set; }

    public bool IsActive { get; set; }

    public string? Notes { get; set; }

    public virtual ICollection<Agent> Agent { get; set; } = new List<Agent>();

    // REQ-019 T1: proveedor controlado y referencia a secreto en Key Vault
    /// <summary>Proveedor del modelo: 'AzureOpenAI' | 'Anthropic'. DEFAULT 'AzureOpenAI'.</summary>
    public string Provider { get; set; } = "AzureOpenAI";

    /// <summary>Nombre del secreto en Azure Key Vault que almacena la ApiKey real (nullable).</summary>
    public string? SecretRef { get; set; }
}
