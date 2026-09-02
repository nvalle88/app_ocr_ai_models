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

    /// <summary>
    /// A quien se puede atender, cuando se entra por la pildora de rol y no
    /// desde un caso. Sin esto la pantalla seria un error, y quien llega ahi lo
    /// que quiere es justamente elegir a la persona.
    /// </summary>
    public List<AfiliadoParaChatVm> Elegir { get; set; } = new();
}

/// <summary>Un afiliado ya identificado, para poder abrirle la consulta.</summary>
public sealed class AfiliadoParaChatVm
{
    public Guid CaseCode { get; set; }
    public string? Nombre { get; set; }
    public string? Cedula { get; set; }
    public string? Plan { get; set; }
    public string? Contrato { get; set; }
    public DateTime? Desde { get; set; }
}
