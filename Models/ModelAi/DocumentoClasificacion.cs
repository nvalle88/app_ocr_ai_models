using System;

namespace app_tramites.Models.ModelAi;

// REQ-019: clasificación de un documento del sobre (spec §4, §1.1, §3).
// Versionada: solo una fila con IsCurrent = 1 por DataFileId.
public partial class DocumentoClasificacion
{
    public int Id { get; set; }

    public int DataFileId { get; set; }

    /// <summary>Tipo predominante, EXACTAMENTE uno (spec §4.1 / §6).</summary>
    public string TipoArchivo { get; set; } = null!;

    /// <summary>CSV de todos los tipos detectados, aquí sí con *-SOPORTE (spec §4.2).</summary>
    public string? ListaTipoArchivo { get; set; }

    public decimal ValorTotal { get; set; }

    public bool EsFacturaValida { get; set; }

    public string? NumeroFactura { get; set; }

    /// <summary>Clave de acceso SRI o número de autorización (spec §1.1).</summary>
    public string? ClaveAcceso { get; set; }

    public decimal? Confianza { get; set; }

    /// <summary>Referencia blanda a Agent.Code (sin FK, para no bloquear desactivaciones).</summary>
    public string? ModelCode { get; set; }

    /// <summary>Referencia blanda a StepExecution.ExecutionId.</summary>
    public long? ExecutionId { get; set; }

    public string? RawJson { get; set; }

    public int VersionNumber { get; set; }

    public bool IsCurrent { get; set; }

    public DateTime CreatedDate { get; set; }

    // ── REQ-019m: tipificación profunda ────────────────────────────────────

    /// <summary>Tipo CLÍNICO del documento (EPICRISIS, KARDEX, HOJA_008…, taxonomía E4).</summary>
    public string? TipoSoporte { get; set; }

    /// <summary>Una frase: qué aporta este soporte y a qué gasto respalda.</summary>
    public string? ResumenSoporte { get; set; }

    // Emisor = el PRESTADOR que factura (no el paciente)
    public string? EmisorRuc { get; set; }
    public string? EmisorNombre { get; set; }
    public string? EmisorNombreComercial { get; set; }

    /// <summary>HOSPITAL | CLINICA | FARMACIA | LABORATORIO | CONSULTORIO… (taxonomía E5).</summary>
    public string? EmisorTipo { get; set; }
    public string? EmisorCiudad { get; set; }
    public string? EmisorPais { get; set; }

    // Datos fiscales de la factura
    /// <summary>Número rotulado "NÚMERO DE AUTORIZACIÓN" (puede coincidir con ClaveAcceso).</summary>
    public string? NumeroAutorizacion { get; set; }
    public DateTime? FechaEmision { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? Iva { get; set; }
    public string? Moneda { get; set; }

    // ── Desglose fiscal completo del pie de la factura (REQ-019y) ──────────
    // Una factura electrónica ecuatoriana trae el pie desglosado y cada línea
    // significa algo distinto para la liquidación: los servicios de salud caen
    // en "No objeto de IVA", los descuentos se restan ANTES de cubrir y el
    // servicio/propina NO es gasto médico. Guardar solo Subtotal e IVA obligaba
    // a suponer la base de cálculo.
    public decimal? SubtotalSinImpuestos { get; set; }
    public decimal? Subtotal15 { get; set; }
    public decimal? Subtotal5 { get; set; }
    public decimal? Subtotal0 { get; set; }
    public decimal? SubtotalNoObjetoIva { get; set; }
    public decimal? SubtotalExentoIva { get; set; }
    public decimal? Descuentos { get; set; }
    public decimal? Ice { get; set; }
    public decimal? Iva15 { get; set; }
    public decimal? Iva5 { get; set; }

    /// <summary>Monto de la línea "Servicio %"; no es gasto médico cubrible.</summary>
    public decimal? ServicioValor { get; set; }

    /// <summary>El porcentaje declarado de servicio, si la factura lo rotula.</summary>
    public decimal? ServicioPorcentaje { get; set; }

    public decimal? Propina { get; set; }

    // ── El PORQUÉ de la decisión (REQ-019y) ───────────────────────────────
    // Que el clasificador diga "CON_MED-FACTURA" sin decir por qué no es
    // auditable: no se puede saber si acertó por la razón correcta. Se guarda
    // el razonamiento, las señales concretas del texto y la alternativa que
    // estuvo más cerca de ganar.

    /// <summary>Por qué se decidió ESE TipoArchivo y no otro.</summary>
    public string? JustificacionTipo { get; set; }

    /// <summary>Las marcas del documento que sostienen la decisión, separadas por |.</summary>
    public string? SenalesTipo { get; set; }

    /// <summary>El tipo alternativo que estuvo más cerca de ganar.</summary>
    public string? TipoDescartado { get; set; }

    /// <summary>Por qué se descartó esa alternativa.</summary>
    public string? JustificacionDescarte { get; set; }

    /// <summary>Por qué es (o no) una factura válida.</summary>
    public string? JustificacionFactura { get; set; }

    /// <summary>Por qué ese tipo clínico de soporte.</summary>
    public string? JustificacionSoporte { get; set; }

    // ── ¿De quién es el documento? (REQ-020j) ─────────────────────────────
    // Un contrato cubre a varias personas. Si la factura no es de quien dice,
    // se liquida contra el deducible y la carencia de otro.

    /// <summary>NumeroPersona del contrato al que corresponde. Nunca un nombre.</summary>
    public int? PacienteNumeroPersona { get; set; }

    /// <summary>COINCIDE | OTRO_BENEFICIARIO | FUERA_DEL_PLAN | NO_SE_PUDO.</summary>
    public string? PacienteCoincide { get; set; }

    /// <summary>Por qué se decidió así, sin transcribir ningún nombre.</summary>
    public string? PacienteJustificacion { get; set; }

    // Paciente ANÓNIMO: no hay campo de nombre a propósito.
    public int? PacienteEdad { get; set; }
    public string? PacienteSexo { get; set; }
    public bool? PacienteEsTitular { get; set; }

    // Médico tratante: aquí sí va el nombre (es prestador)
    public string? MedicoNombre { get; set; }
    public string? MedicoEspecialidad { get; set; }
    public string? MedicoRegistro { get; set; }

    /// <summary>Fecha de la atención clínica; puede diferir de la emisión de la factura.</summary>
    public DateTime? FechaAtencion { get; set; }

    public virtual DataFile DataFileNavigation { get; set; } = null!;
}
