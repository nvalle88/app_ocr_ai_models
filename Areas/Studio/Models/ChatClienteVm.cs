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
    public string? CodigoPlan { get; set; }
    public int? VersionPlan { get; set; }

    /// <summary>IND, COR, TRK… Lo que decide de que contrato se le habla.</summary>
    public string? CodigoProducto { get; set; }

    public string? NumeroContrato { get; set; }

    /// <summary>
    /// El plan sobre el que se esta consultando cuando NO es el suyo: sirve para
    /// simular «y si tuviera el plan X». Se dice en pantalla, siempre, porque una
    /// respuesta sobre otro plan que parezca la suya seria peor que no tenerla.
    /// </summary>
    public string? PlanConsultado { get; set; }

    /// <summary>
    /// A quien se puede atender, cuando se entra por la pildora de rol y no
    /// desde un caso. Sin esto la pantalla seria un error, y quien llega ahi lo
    /// que quiere es justamente elegir a la persona.
    /// </summary>
    public List<AfiliadoParaChatVm> Elegir { get; set; } = new();

    /// <summary>Lo que se escribio en el buscador: cedula, contrato o nombre.</summary>
    public string? Buscar { get; set; }

    /// <summary>
    /// La conversación anterior, para que al volver a la pantalla siga donde la
    /// dejó. Sale de StepExecution: no hay tabla de conversaciones.
    /// </summary>
    public List<TurnoChatVm> Hilo { get; set; } = new();
}

/// <summary>Una pregunta y su respuesta.</summary>
public sealed class TurnoChatVm
{
    public string Pregunta { get; set; } = string.Empty;
    public string Respuesta { get; set; } = string.Empty;
    public DateTime? Cuando { get; set; }
}

/// <summary>Un afiliado ya identificado, para poder abrirle la consulta.</summary>
public sealed class AfiliadoParaChatVm
{
    public Guid CaseCode { get; set; }
    public string? Nombre { get; set; }
    public string? Cedula { get; set; }

    /// <summary>Nombre comercial del plan. Viene vacío en parte de las filas.</summary>
    public string? Plan { get; set; }

    /// <summary>Código del plan: TRANKI, N4-D-C… Es el que SÍ está siempre.</summary>
    public string? CodigoPlan { get; set; }

    /// <summary>IND, COR, TRK… Junto con la región es lo que distingue dos contratos.</summary>
    public string? Producto { get; set; }
    public string? Region { get; set; }

    /// <summary>
    /// El NÚMERO de contrato, no el código. Verificado contra ContratoJson: de
    /// 30 filas, las 30 coinciden con $.Numero y ninguna con $.Codigo — que para
    /// el contrato 70200015 es 1642570, un número distinto que no se le enseña a
    /// nadie porque es la llave interna.
    /// </summary>
    public string? Contrato { get; set; }

    /// <summary>Cuantas solicitudes ha presentado. Una persona con ocho es otro caso que una con una.</summary>
    public int Solicitudes { get; set; }

    /// <summary>Cuando presento la ultima solicitud de reembolso.</summary>
    public DateTime? UltimaSolicitud { get; set; }

    /// <summary>
    /// Cuando se le consulto por ESTE chat por ultima vez. Null si nunca.
    ///
    /// No es lo mismo que UltimaSolicitud, y la columna de la pantalla decia
    /// «Consultado» mientras enseñaba la fecha de la solicitud: dos cosas
    /// distintas con la misma etiqueta.
    /// </summary>
    public DateTime? UltimaConsulta { get; set; }

    /// <summary>
    /// El plan como se puede enseñar. NombrePlan viene vacío en algunas filas
    /// —medido: 2 de 30— y era justo la columna que se pintaba, así que la
    /// pantalla salía sin plan. El código nunca falta.
    /// </summary>
    public string PlanVisible =>
        !string.IsNullOrWhiteSpace(Plan) ? Plan!
        : !string.IsNullOrWhiteSpace(CodigoPlan) ? CodigoPlan!
        : "—";
}
