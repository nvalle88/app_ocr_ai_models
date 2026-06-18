namespace app_tramites.Models.ModelAi;

// ============================================================
// REQ-019 T3 — Entidad de configuración multi-cuenta Zendesk.
// Tabla aditiva; no modifica ninguna tabla existente.
// ============================================================

/// <summary>
/// Configuración de una cuenta Zendesk (Auxiliar / Digital / Experience).
/// Almacena el sub-dominio y la referencia al secret del token Bearer
/// en Key Vault. No almacena el token en claro.
/// </summary>
public sealed class ZendeskConf
{
    /// <summary>
    /// Código de la cuenta (PK, varchar(50)).
    /// Valores esperados: "Auxiliar", "Digital", "Experience".
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Nombre de la cuenta para display (varchar(30)).
    /// Valores esperados: "Auxiliar" | "Digital" | "Experience".
    /// </summary>
    public string Cuenta { get; set; } = string.Empty;

    /// <summary>
    /// Sub-dominio de Zendesk, ej. "saludsa" → https://saludsa.zendesk.com/
    /// (varchar(100)).
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Referencia al secret del token Bearer en Azure Key Vault,
    /// ej. "zendesk-token-auxiliar". En entornos locales puede
    /// contener el token directamente como placeholder
    /// (varchar(250)).
    /// </summary>
    public string? SecretRef { get; set; }

    /// <summary>Indica si la cuenta está activa.</summary>
    public bool IsActive { get; set; } = true;
}
