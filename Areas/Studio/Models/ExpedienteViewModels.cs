namespace app_ocr_ai_models.Areas.Studio.Models;

// ============================================================
// REQ-019 — Expediente documental del caso (document intelligence):
//   clasificación por documento + entidades extraídas + árbol de
//   evidencia (factura → justificantes) + ficha del cliente.
//   Lo genera el agente IA y se persiste como Note "ExpedienteDocumental".
// ============================================================

/// <summary>DTO que deserializa el JSON del expediente que emite el agente.</summary>
public sealed class ExpedienteDto
{
    public FichaClienteDto? FichaCliente { get; set; }
    public List<DocumentoDto> Documentos { get; set; } = new();
    public List<VinculoDto> Vinculos { get; set; } = new();
    public List<string> Alertas { get; set; } = new();
    public string? ResumenCaso { get; set; }

    /// <summary>Ficha consolidada del cliente/beneficiario extraída de los documentos.</summary>
    public sealed class FichaClienteDto
    {
        public string? Nombre { get; set; }
        public string? Cedula { get; set; }
        public string? Contrato { get; set; }
        public string? Producto { get; set; }
        public string? Prestador { get; set; }
        public string? Diagnosticos { get; set; }
        public string? FechaAtencion { get; set; }
        public decimal? TotalFacturado { get; set; }
    }

    /// <summary>Un documento clasificado con lo extraído.</summary>
    public sealed class DocumentoDto
    {
        /// <summary>Id del DataFile (para enlazar con el visor).</summary>
        public int DocId { get; set; }
        public string? Nombre { get; set; }
        /// <summary>FACTURA | RECETA | PEDIDO_MEDICO | RESULTADO_EXAMEN | INFORME_MEDICO | CEDULA | OTRO</summary>
        public string Tipo { get; set; } = "OTRO";
        /// <summary>Resumen en una frase: "Factura de consulta pediátrica del Dr. X por $123".</summary>
        public string? Resumen { get; set; }
        /// <summary>Entidades clave extraídas (emisor, RUC, fecha, valor, medicamentos, diagnóstico...).</summary>
        public Dictionary<string, string> Entidades { get; set; } = new();
        /// <summary>Faltantes u observaciones del documento (ilegible, sin fecha, sin RUC...).</summary>
        public List<string> Observaciones { get; set; } = new();

        public string TipoIcono => Tipo?.ToUpperInvariant() switch
        {
            "FACTURA"          => "fa-file-text-o",
            "RECETA"           => "fa-medkit",
            "PEDIDO_MEDICO"    => "fa-stethoscope",
            "RESULTADO_EXAMEN" => "fa-flask",
            "INFORME_MEDICO"   => "fa-user-md",
            "CEDULA"           => "fa-id-card-o",
            _                  => "fa-file-o"
        };
        public string TipoCss => Tipo?.ToUpperInvariant() switch
        {
            "FACTURA"          => "is-factura",
            "RECETA"           => "is-receta",
            "PEDIDO_MEDICO"    => "is-pedido",
            "RESULTADO_EXAMEN" => "is-examen",
            "INFORME_MEDICO"   => "is-informe",
            _                  => "is-otro"
        };
    }

    /// <summary>Vínculo de evidencia entre documentos (árbol factura → justificantes).</summary>
    public sealed class VinculoDto
    {
        /// <summary>DocId del documento principal (p. ej. la factura).</summary>
        public int DePrincipal { get; set; }
        /// <summary>DocId del documento que lo justifica (p. ej. la receta).</summary>
        public int AJustificante { get; set; }
        /// <summary>JUSTIFICA | RESPALDA | DUPLICADO | CONTRADICE</summary>
        public string Relacion { get; set; } = "JUSTIFICA";
        public string? Detalle { get; set; }
    }
}

/// <summary>ViewModel de la pestaña Expediente.</summary>
public sealed class ExpedienteViewModel
{
    public Guid CaseCode { get; init; }
    public bool Generado { get; init; }
    public ExpedienteDto? Expediente { get; init; }
    public string? Error { get; init; }
    public DateTime? GeneradoEn { get; init; }
}

/// <summary>Invocación de herramienta real (de la tabla ToolInvocation) para el audit trail.</summary>
public sealed class ToolInvocacionVM
{
    public string ToolCode { get; init; } = string.Empty;
    public DateTime StartDate { get; init; }
    public double? DuracionSeg { get; init; }
    public bool IsError { get; init; }
    public string? InputResumen { get; init; }
}
