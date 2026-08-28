namespace app_ocr_ai_models.Areas.Studio.Models;

// ============================================================
// REQ-019 — Auditoría de Medicina (asistente del auditor médico).
//   El agente AGENTE_AUDITOR_MEDICINA devuelve el JSON de este DTO y
//   la vista lo renderiza con las MISMAS secciones del documento
//   funcional: RESUMEN · DATOS ESTRUCTURADOS · ALERTAS · RECOMENDACIÓN.
//   El agente SEÑALA banderas; NO dictamina ni aprueba cobertura.
// ============================================================

/// <summary>DTO del JSON que emite el agente auditor de medicina.</summary>
public sealed class AuditoriaMedicinaDto
{
    public string? ResumenCaso { get; set; }
    public List<string> DocumentosAnalizados { get; set; } = new();
    public DatosDto? Datos { get; set; }
    public List<AlertaDto> Alertas { get; set; } = new();
    public string? Recomendacion { get; set; }
    public string? NotaAuditoria { get; set; }
    public List<string> Faltantes { get; set; } = new();

    /// <summary>Datos estructurados del tratamiento (números nulos si no se pudieron calcular).</summary>
    public sealed class DatosDto
    {
        public string? Beneficiario { get; set; }
        public string? Diagnostico { get; set; }
        public string? Medicamento { get; set; }
        public string? Dosis { get; set; }
        public string? Posologia { get; set; }
        public string? Periodo { get; set; }
        public string? Unidad { get; set; }
        public decimal? CantidadCorrespondeMes { get; set; }
        public decimal? YaCubierto { get; set; }
        public decimal? SaldoPendiente { get; set; }
        public decimal? CantidadFacturada { get; set; }
        public decimal? ValorFacturado { get; set; }
    }

    /// <summary>Bandera para revisión humana, con nivel y acción sugerida.</summary>
    public sealed class AlertaDto
    {
        /// <summary>CRITICA | ADVERTENCIA | OK</summary>
        public string? Nivel { get; set; }
        public string? Titulo { get; set; }
        public string? Detalle { get; set; }
        public string? AccionSugerida { get; set; }

        private string N => (Nivel ?? "").ToUpperInvariant().Replace("Í", "I").Replace("Ó", "O");
        public bool EsCritica => N.StartsWith("CRIT");
        public string NivelCss => N switch
        {
            var x when x.StartsWith("CRIT") => "is-critica",
            var x when x.StartsWith("ADV")  => "is-advertencia",
            _                                => "is-ok"
        };
        public string NivelIcono => N switch
        {
            var x when x.StartsWith("CRIT") => "fa-exclamation-circle",
            var x when x.StartsWith("ADV")  => "fa-exclamation-triangle",
            _                                => "fa-check-circle"
        };
        public string NivelLabel => N switch
        {
            var x when x.StartsWith("CRIT") => "CRÍTICA",
            var x when x.StartsWith("ADV")  => "ADVERTENCIA",
            _                                => "OK"
        };
        /// <summary>Orden de severidad para presentación (críticas primero).</summary>
        public int Orden => N switch
        {
            var x when x.StartsWith("CRIT") => 0,
            var x when x.StartsWith("ADV")  => 1,
            _                                => 2
        };
    }
}

/// <summary>ViewModel de la pantalla e informe de Auditoría de Medicina.</summary>
public sealed class AuditoriaViewModel
{
    public const string DisclaimerAsistente =
        "ASISTENCIA IA PARA EL AUDITOR MÉDICO — señala banderas para revisión humana. " +
        "No constituye dictamen clínico ni decisión de cobertura.";

    public Guid CaseCode { get; init; }
    public string ShortCode => CaseCode.ToString().Split('-')[0];

    public bool Generada { get; init; }
    public AuditoriaMedicinaDto? Auditoria { get; init; }

    /// <summary>Texto crudo si el motor no devolvió JSON interpretable.</summary>
    public string? RawText { get; init; }
    public string? Error { get; init; }
    public string GeneradoPor { get; init; } = string.Empty;
    public DateTime? GeneradoEn { get; init; }

    /// <summary>Herramientas realmente ejecutadas (auditoría desde ToolInvocation).</summary>
    public IReadOnlyList<ToolInvocacionVM> ToolInvocaciones { get; init; } = Array.Empty<ToolInvocacionVM>();

    public string Disclaimer => DisclaimerAsistente;

    // Semáforo global: manda la alerta más grave
    public int NumCriticas => Auditoria?.Alertas.Count(a => a.EsCritica) ?? 0;
    public int NumAdvertencias => Auditoria?.Alertas.Count(a => a.NivelCss == "is-advertencia") ?? 0;

    public string EstadoCss => NumCriticas > 0 ? "is-critica" : NumAdvertencias > 0 ? "is-advertencia" : "is-ok";
    public string EstadoIcono => NumCriticas > 0 ? "fa-exclamation-circle" : NumAdvertencias > 0 ? "fa-exclamation-triangle" : "fa-check-circle";
    public string EstadoLabel => NumCriticas > 0
        ? $"{NumCriticas} alerta(s) crítica(s)"
        : NumAdvertencias > 0 ? $"{NumAdvertencias} advertencia(s)" : "Sin alertas críticas";
    public string EstadoFrase => NumCriticas > 0
        ? "Requiere revisión del auditor médico antes de aprobar."
        : NumAdvertencias > 0 ? "Hay puntos que requieren atención del auditor."
        : "Validaciones superadas; revisar el detalle antes de resolver.";
}
