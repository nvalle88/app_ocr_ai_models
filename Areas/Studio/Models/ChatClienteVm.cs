namespace app_ocr_ai_models.Areas.Studio.Models;

/// <summary>
/// La ficha del chat del afiliado. Poco estado a propósito: la conversación la
/// mantiene el navegador, no el servidor.
/// </summary>
public sealed class ChatClienteVm
{
    public Guid CaseCode { get; set; }

    /// <summary>Cuando falta algo para poder abrir el chat, se dice qué.</summary>
    public string? Error { get; set; }

    /// <summary>Hay contrato identificado y se puede preguntar.</summary>
    public bool Listo { get; set; }

    public string? NombreTitular { get; set; }

    /// <summary>De quién es el gasto: el titular o el dependiente elegido.</summary>
    public string? NombrePaciente { get; set; }

    public string? NombrePlan { get; set; }
    public string? NumeroContrato { get; set; }
}
