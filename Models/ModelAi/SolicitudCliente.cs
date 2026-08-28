using System;

namespace app_tramites.Models.ModelAi;

/// <summary>
/// REQ-020 — La sesión del afiliado en el portal.
///
/// Guarda a qué contrato dijo pertenecer y si confirmó lo que se leyó de sus
/// documentos. La confirmación no es un trámite: es el momento en que el
/// afiliado se hace responsable de que el prestador y el valor son los suyos,
/// y queda fechado por si después alguien discute qué se presentó.
/// </summary>
public partial class SolicitudCliente
{
    public int Id { get; set; }

    /// <summary>El caso del Studio que se abrió para esta solicitud.</summary>
    public Guid CaseCode { get; set; }

    public string Cedula { get; set; } = null!;

    // ── El contrato elegido ────────────────────────────────────────────────
    public string? NumeroContrato { get; set; }
    public string? CodigoProducto { get; set; }
    public string? CodigoRegion { get; set; }
    public string? CodigoPlan { get; set; }
    public string? NombrePlan { get; set; }
    public string? NombreTitular { get; set; }
    public int? NumeroPersona { get; set; }

    /// <summary>
    /// La foto del contrato tal como la devolvió la API en el momento de
    /// elegirlo. Si mañana cambia el plan o el estado, se sigue sabiendo con
    /// qué datos se decidió.
    /// </summary>
    public string? ContratoJson { get; set; }

    /// <summary>
    /// Lo que el afiliado dice que gastó. Puede no coincidir con la factura y
    /// eso es un aviso, no un rechazo.
    /// </summary>
    public decimal? ValorPresentado { get; set; }

    // ── A quien va dirigido el reembolso (REQ-020c) ───────────────────────
    // Un contrato cubre al titular y a sus dependientes, y el gasto puede ser
    // de cualquiera. No es un dato de formulario: el deducible consumido, la
    // carencia y las preexistencias son de la PERSONA, asi que elegir mal
    // liquida contra las condiciones de otro.
    public string? NombreBeneficiario { get; set; }
    public string? CedulaBeneficiario { get; set; }

    /// <summary>Titular | Conyuge | Hijo | …</summary>
    public string? RelacionBeneficiario { get; set; }

    public int? EdadBeneficiario { get; set; }
    public string? GeneroBeneficiario { get; set; }

    /// <summary>Sus condiciones en el momento de presentar, para poder
    /// explicar despues por que se resolvio como se resolvio.</summary>
    public decimal? DeducibleCubierto { get; set; }
    public bool? EnCarencia { get; set; }
    public int? DiasFinCarencia { get; set; }
    public bool? TienePreexistencias { get; set; }

    /// <summary>La lista completa tal como la devolvio la API, para repintar
    /// el selector sin volver a llamar.</summary>
    public string? BeneficiariosJson { get; set; }

    /// <summary>BORRADOR | ADJUNTANDO | ANALIZADA | CONFIRMADA | ENVIADA.</summary>
    public string Estado { get; set; } = "BORRADOR";

    public bool DatosConfirmados { get; set; }
    public DateTime? FechaConfirmacion { get; set; }

    /// <summary>
    /// La última explicación generada para el afiliado. Se guarda para no
    /// volver a llamar al modelo cada vez que recarga la página.
    /// </summary>
    public string? ExplicacionJson { get; set; }

    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
}
