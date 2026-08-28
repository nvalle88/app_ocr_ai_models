using app_tramites.Services.Ai;

namespace app_ocr_ai_models.Areas.Studio.Models;

// ============================================================
// REQ-019 T6 — ViewModels del controlador AnalizarController.
// ============================================================

/// <summary>
/// Formulario para disparar el análisis IA de un caso ya importado.
/// </summary>
public sealed class AnalizarCasoRequest
{
    /// <summary>Código del caso a analizar.</summary>
    public Guid CaseCode { get; set; }

    /// <summary>
    /// Código de proceso a aplicar. Si se deja vacío, el clasificador lo determina.
    /// </summary>
    public string? ProcessCodeOverride { get; set; }
}

/// <summary>
/// Resultado de la orquestación presentado en la vista de análisis.
/// </summary>
public sealed class AnalizarResultadoViewModel
{
    /// <summary>Request original (para repintar el formulario).</summary>
    public AnalizarCasoRequest Request { get; init; } = new();

    /// <summary>Resultado completo del orquestador. Null si aún no se analizó.</summary>
    public OrchestrationResult? Resultado { get; init; }

    /// <summary>Mensaje de error global, si aplica.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// Datos del INFORME imprimible (PDF vía imprimir) de un caso analizado:
/// contexto del sobre, documentos, ejecuciones del análisis y respuestas del chat.
/// </summary>
public sealed class InformeCasoViewModel
{
    /// <summary>Caso OCR.</summary>
    public app_tramites.Models.ModelAi.ProcessCase Caso { get; init; } = null!;

    /// <summary>Contexto estructurado del sobre (JSON legible), si existe.</summary>
    public string ContextoSobre { get; init; } = string.Empty;

    /// <summary>Documentos del caso.</summary>
    public IReadOnlyList<app_tramites.Models.ModelAi.DataFile> Archivos { get; init; }
        = Array.Empty<app_tramites.Models.ModelAi.DataFile>();

    /// <summary>Ejecuciones del análisis (pasos), más recientes primero.</summary>
    public IReadOnlyList<app_tramites.Models.ModelAi.StepExecution> Ejecuciones { get; init; }
        = Array.Empty<app_tramites.Models.ModelAi.StepExecution>();

    /// <summary>Respuestas del chat persistidas, más recientes primero.</summary>
    public IReadOnlyList<app_tramites.Models.ModelAi.FinalResponseResult> RespuestasChat { get; init; }
        = Array.Empty<app_tramites.Models.ModelAi.FinalResponseResult>();

    /// <summary>Usuario que genera el informe.</summary>
    public string GeneradoPor { get; init; } = string.Empty;
}

// ============================================================
// REQ-019 — Resolución de reembolso (pre-liquidación propuesta por IA)
// ============================================================

/// <summary>Ruta terminal de la matriz de triaje de reembolso.</summary>
public enum ResolucionEstado { Liquida, Semi, Ch, Negativa, Indeterminado }

/// <summary>
/// DTO que deserializa el JSON <c>PropuestaPreLiquidacionReembolso</c> que emite el agente.
/// Se usa <c>PropertyNameCaseInsensitive</c> al deserializar (camelCase → PascalCase).
/// </summary>
public sealed class PropuestaReembolsoDto
{
    public string? Disclaimer { get; set; }
    public CabeceraDto? Cabecera { get; set; }
    public string? EstadoPropuesto { get; set; }   // LIQUIDA_AUTO/SEMI/CONTROL_HUMANO/NEGATIVA
    public double? Confianza { get; set; }
    public List<ItemDto> Items { get; set; } = new();
    public TotalesDto? Totales { get; set; }
    public List<string> Observaciones { get; set; } = new();
    public List<ReglaEvaluadaDto> ReglasEvaluadas { get; set; } = new();
    public MacroDto? MacroSugerida { get; set; }

    /// <summary>Una regla evaluada por el agente (audit trail de la resolución).</summary>
    public sealed class ReglaEvaluadaDto
    {
        public string? Familia { get; set; }
        public string? Regla { get; set; }
        /// <summary>CUMPLE | NO_CUMPLE | NO_APLICA | REQUIERE_CH</summary>
        public string? Resultado { get; set; }
        public string? Detalle { get; set; }
        public string? Evidencia { get; set; }

        public string ResultadoCss => (Resultado ?? "").ToUpperInvariant() switch
        {
            "CUMPLE"      => "is-cumple",
            "NO_CUMPLE"   => "is-nocumple",
            "REQUIERE_CH" => "is-ch",
            _             => "is-noaplica"
        };
    }

    public sealed class CabeceraDto
    {
        public string? NumeroSobre { get; set; }
        public string? Titular { get; set; }
        public string? Beneficiario { get; set; }
        public long? Contrato { get; set; }
        public string? Producto { get; set; }
        public string? Region { get; set; }
        public string? Prestador { get; set; }
        public string? FechaIncurrencia { get; set; }
    }

    public sealed class ItemDto
    {
        public string? Descripcion { get; set; }
        public string? Procedimiento { get; set; }
        public decimal ValorPresentado { get; set; }
        public decimal ValorCubierto { get; set; }
        public decimal ValorCopago { get; set; }
        public decimal ValorDeducible { get; set; }
        public decimal ValorNoCubierto { get; set; }
        public string? Cubierto { get; set; }        // TOTAL / PARCIAL / NO_CUBIERTO
        public string? Motivo { get; set; }
        public string? ReglaAplicada { get; set; }
        public List<string> Evidencia { get; set; } = new();

        /// <summary>Clase CSS del badge de regla según el efecto del ítem.</summary>
        public string ReglaCssClass =>
            string.Equals(Cubierto, "NO_CUBIERTO", StringComparison.OrdinalIgnoreCase) ? "is-neg"
            : string.Equals(Cubierto, "PARCIAL", StringComparison.OrdinalIgnoreCase) ? "is-ch"
            : string.Empty;

        /// <summary>Primera evidencia (archivo:línea) para el tooltip.</summary>
        public string ReglaFuente => Evidencia.FirstOrDefault() ?? string.Empty;
    }

    public sealed class TotalesDto
    {
        public decimal Presentado { get; set; }
        public decimal Cubierto { get; set; }
        public decimal Copago { get; set; }
        public decimal Deducible { get; set; }
        public decimal NoCubierto { get; set; }
        public decimal EstimadoPagar { get; set; }
        public decimal Pendiente { get; set; }
    }

    public sealed class MacroDto
    {
        public string? TipoMacro { get; set; }
        public string? RazonMacro { get; set; }
        public string? NombreMacro { get; set; }
    }
}

/// <summary>ViewModel de la pantalla e informe de Resolución de reembolso.</summary>
public sealed class ResolucionViewModel
{
    public const string DisclaimerFijo =
        "PROPUESTA / ESTIMACIÓN PRE-LIQUIDACIÓN GENERADA POR IA — NO OFICIAL. " +
        "Sujeta a validación y liquidación formal en Saludsa.";

    public Guid CaseCode { get; init; }
    public string ShortCode => CaseCode.ToString().Split('-')[0];

    /// <summary>false → aún no se generó (mostrar formulario/botón).</summary>
    public bool Generada { get; init; }

    public ResolucionEstado Estado { get; init; } = ResolucionEstado.Indeterminado;
    public PropuestaReembolsoDto? Propuesta { get; init; }

    /// <summary>Texto crudo del agente cuando no se pudo parsear el JSON (fallback CH).</summary>
    public string? RawText { get; init; }

    /// <summary>Carta al cliente armada por plantilla (borrador).</summary>
    public string CartaTexto { get; init; } = string.Empty;

    public string? CorreoCliente { get; init; }
    public string? Error { get; init; }
    public string GeneradoPor { get; init; } = string.Empty;

    /// <summary>Herramientas realmente ejecutadas (tabla ToolInvocation) — audit trail con datos de BD.</summary>
    public IReadOnlyList<ToolInvocacionVM> ToolInvocaciones { get; init; } = Array.Empty<ToolInvocacionVM>();

    // ── Presentación del estado (fuente única de color/icono/frase) ──
    public string EstadoLabel => Estado switch
    {
        ResolucionEstado.Liquida  => "Liquidación automática",
        ResolucionEstado.Semi     => "Liquidación parcial (revisión asistida)",
        ResolucionEstado.Ch       => "Requiere control humano",
        ResolucionEstado.Negativa => "Reembolso no procedente",
        _                         => "Indeterminado"
    };
    public string EstadoFrase => Estado switch
    {
        ResolucionEstado.Liquida  => "Cumple las reglas: se puede liquidar sin intervención.",
        ResolucionEstado.Semi     => "Cobertura parcial: parte se reconoce, parte requiere revisión.",
        ResolucionEstado.Ch       => "Hay motivos que un analista debe revisar antes de resolver.",
        ResolucionEstado.Negativa => "Según las reglas, la solicitud no procede para reembolso.",
        _                         => "No fue posible determinar la resolución automáticamente."
    };
    public string EstadoCss => Estado switch
    {
        ResolucionEstado.Liquida  => "is-liquida",
        ResolucionEstado.Semi     => "is-semi",
        ResolucionEstado.Ch       => "is-ch",
        ResolucionEstado.Negativa => "is-negativa",
        _                         => "is-ch"
    };
    public string EstadoIcono => Estado switch
    {
        ResolucionEstado.Liquida  => "fa-check-circle",
        ResolucionEstado.Semi     => "fa-adjust",
        ResolucionEstado.Ch       => "fa-user-md",
        ResolucionEstado.Negativa => "fa-times-circle",
        _                         => "fa-question-circle"
    };

    public string Disclaimer => DisclaimerFijo;
}
