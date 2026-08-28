using System;
namespace app_ocr_ai_models.Areas.Studio.Models;

// ============================================================
// REQ-019 — Tipificación de documentos de reembolso.
//   DTO de la salida del clasificador (spec_clasificacion_documentos_reembolso.md)
//   + ViewModel de la pantalla de Tipificación.
//   Un sobre puede contener VARIOS tipos de reembolso: por eso hay items por
//   rubro, tags por página y split por tipo.
// ============================================================

/// <summary>Salida del clasificador/tipificador de documentos de reembolso.</summary>
public sealed class ClasificacionSobreDto
{
    public List<ClasificacionFicheroDto> Ficheros { get; set; } = new();
    public List<ResumenDiagnosticoDto> ResumenDiagnosticos { get; set; } = new();
    public List<SplitPorTipoDto> SplitPorTipo { get; set; } = new();
    public string? TipoPredominante { get; set; }
    public decimal TotalSobre { get; set; }
    public List<AlertaClasificacionDto> Alertas { get; set; } = new();

    // ── REQ-019m: tipo de atención del sobre ──────────────────────────────
    /// <summary>HOSPITALARIO | HOSPITAL_DIA | AMBULATORIO | DESCONOCIDO.</summary>
    public string? TipoAtencion { get; set; }
    public List<IndicadorAtencionDto> IndicadoresAtencion { get; set; } = new();
    public string? JustificacionAtencion { get; set; }
}

/// <summary>Un indicador que sustenta el tipo de atención (sección R del prompt).</summary>
public sealed class IndicadorAtencionDto
{
    public string? Nombre { get; set; }
    public bool Presente { get; set; }
    public int? DocId { get; set; }
}

/// <summary>Quién emite la factura. Es el PRESTADOR, no el paciente.</summary>
public sealed class EmisorDto
{
    public string? Ruc { get; set; }
    public string? Nombre { get; set; }
    public string? NombreComercial { get; set; }
    public string? TipoEstablecimiento { get; set; }
    public string? Ciudad { get; set; }
    public string? Pais { get; set; }
}

/// <summary>Datos fiscales de la factura.</summary>
public sealed class FacturaDatosDto
{
    public string? Numero { get; set; }
    public string? NumeroAutorizacion { get; set; }
    public string? ClaveAcceso { get; set; }
    public string? FechaEmision { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? Iva { get; set; }
    public decimal? Total { get; set; }
    public string? Moneda { get; set; }

    // ── Pie fiscal completo (REQ-019y) ────────────────────────────────────
    // Cada linea del pie significa algo distinto al liquidar: los servicios de
    // salud caen en "No objeto de IVA", los descuentos se restan ANTES de
    // cubrir y el servicio/propina no es gasto medico. Con solo Subtotal e IVA
    // habia que suponer la base de calculo.
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
    public decimal? ServicioValor { get; set; }
    public decimal? ServicioPorcentaje { get; set; }
    public decimal? Propina { get; set; }

    /// <summary>
    /// Base cubrible: lo facturado menos lo que no es gasto medico (descuentos
    /// y servicio). Se calcula aqui y no en la vista para que la resolucion y
    /// la pantalla usen exactamente el mismo numero.
    /// </summary>
    public decimal? BaseCubrible
    {
        get
        {
            var bruto = SubtotalSinImpuestos ?? Subtotal;
            if (bruto is null) return null;
            return bruto - (Descuentos ?? 0m) - (ServicioValor ?? 0m);
        }
    }

    /// <summary>
    /// El pie cuadra si la suma de las partes coincide con el total declarado.
    /// null cuando no hay datos suficientes para juzgarlo.
    /// </summary>
    public bool? PieCuadra
    {
        get
        {
            if (Total is null) return null;
            var b = SubtotalSinImpuestos ?? Subtotal;
            if (b is null) return null;
            var suma = b.Value - (Descuentos ?? 0m) + (Ice ?? 0m)
                     + (Iva15 ?? Iva ?? 0m) + (Iva5 ?? 0m)
                     + (ServicioValor ?? 0m) + (Propina ?? 0m);
            return Math.Abs(suma - Total.Value) <= 0.02m;
        }
    }
}

/// <summary>
/// Datos del paciente. NO hay campo de nombre a propósito: el requisito es no
/// escribirlo en ninguna parte de la salida.
/// </summary>
public sealed class PacienteDto
{
    public int? Edad { get; set; }
    public string? Sexo { get; set; }
    public bool? EsTitular { get; set; }
}

/// <summary>Médico tratante. Aquí sí va el nombre: es prestador.</summary>
public sealed class MedicoTratanteDto
{
    public string? Nombre { get; set; }
    public string? Especialidad { get; set; }
    public string? Registro { get; set; }
}

/// <summary>Procedimiento detectado, con el código de liquidación cruzado por JOIN.</summary>
public sealed class ProcedimientoDto
{
    public string? CodigoCpt { get; set; }
    public string? Descripcion { get; set; }
    public int? Pagina { get; set; }
    /// <summary>Se rellena en el controlador desde CatalogoCodigoLiquidacion (no lo dice la IA).</summary>
    public string? CodigoLiquidacion { get; set; }
    public string? RubroLiquidacion { get; set; }
    /// <summary>CPT | DESCRIPCION | null — cómo se llegó al código (ver entidad).</summary>
    public string? OrigenMatch { get; set; }

    // ── REQ-019r: procedimiento real (Lr05) y correlación (Lr46) ──────────
    public int? NumeroProcedimiento { get; set; }
    public string? NombreLr05 { get; set; }
    public string? CodigoBeneficio { get; set; }
    public bool? EsMedicina { get; set; }
    /// <summary>MARCA | GENERICA — A010 marca, A011 genérica.</summary>
    public string? TipoMedicina { get; set; }
    public decimal? ScoreHomologacion { get; set; }
    public bool? HomologacionAmbigua { get; set; }
    /// <summary>CORRELACIONA | NO_CORRELACIONA | SIN_VALIDAR.</summary>
    public string? EstadoCorrelacion { get; set; }
    public int? CorrelacionProb { get; set; }
    public string? CorrelacionDx { get; set; }
    public bool? CorrelacionConfirmada { get; set; }
    public int? UmbralAplicado { get; set; }
}

public sealed class ClasificacionFicheroDto
{
    public int DocId { get; set; }
    public string? Nombre { get; set; }
    public string? TipoArchivo { get; set; }
    public List<string> ListaTipoArchivo { get; set; } = new();
    public bool EsFacturaValida { get; set; }
    public string? NumeroFactura { get; set; }
    public string? ClaveAcceso { get; set; }
    public decimal ValorTotal { get; set; }
    public List<DiagnosticoDto> Diagnosticos { get; set; } = new();
    public List<ItemFacturaDto> Items { get; set; } = new();
    public List<PaginaClasificadaDto> Paginas { get; set; } = new();
    public List<string> Observaciones { get; set; } = new();

    // ── REQ-019m: de quién es la factura y qué es el soporte ──────────────
    /// <summary>Tipo clínico del documento (EPICRISIS, KARDEX, HOJA_008…).</summary>
    public string? TipoSoporte { get; set; }
    public string? ResumenSoporte { get; set; }
    public EmisorDto? Emisor { get; set; }
    public FacturaDatosDto? Factura { get; set; }
    public PacienteDto? Paciente { get; set; }
    public MedicoTratanteDto? MedicoTratante { get; set; }
    public string? FechaAtencion { get; set; }
    public List<ProcedimientoDto> Procedimientos { get; set; } = new();

    // ── El PORQUE de la decision (REQ-019y) ───────────────────────────────
    // Decir "CON_MED-FACTURA" sin decir por que no es auditable: no se puede
    // saber si el clasificador acerto por la razon correcta o de casualidad.
    /// <summary>Por que se decidio ESE tipo y no otro.</summary>
    public string? JustificacionTipo { get; set; }
    /// <summary>Las marcas concretas del documento que sostienen la decision.</summary>
    public List<string> SenalesTipo { get; set; } = new();
    /// <summary>El tipo que estuvo mas cerca de ganar.</summary>
    public string? TipoDescartado { get; set; }
    /// <summary>Por que se descarto esa alternativa.</summary>
    public string? JustificacionDescarte { get; set; }
    /// <summary>Por que es (o no) factura valida.</summary>
    public string? JustificacionFactura { get; set; }
    /// <summary>Por que ese tipo clinico de soporte.</summary>
    public string? JustificacionSoporte { get; set; }

    // ── De quien es el documento (REQ-020j) ───────────────────────────────
    /// <summary>NumeroPersona del contrato al que corresponde; nunca un nombre.</summary>
    public int? PacienteNumeroPersona { get; set; }
    /// <summary>COINCIDE | OTRO_BENEFICIARIO | FUERA_DEL_PLAN | NO_SE_PUDO.</summary>
    public string? PacienteCoincide { get; set; }
    /// <summary>El porque, sin transcribir ningun nombre.</summary>
    public string? PacienteJustificacion { get; set; }
}

public sealed class DiagnosticoDto { public string? Codigo { get; set; } public string? Descripcion { get; set; } }

/// <summary>
/// Diagnostico agrupado para la vista. Conserva de que documentos salio: sin
/// eso la tabla no podia anclarse al PDF y era el unico bloque de la pantalla
/// sin su segmento de evidencia.
/// </summary>
public sealed class ResumenDiagnosticoDto
{
    public string? Codigo { get; set; }
    public string? Descripcion { get; set; }
    public decimal ValorTotal { get; set; }

    /// <summary>Documentos donde aparece este codigo, para llevar al PDF.</summary>
    public List<int> DocIds { get; set; } = new();

    /// <summary>Nombre del primer documento, para rotular el enlace.</summary>
    public string? DocNombre { get; set; }
}

public sealed class ItemFacturaDto
{
    public string? Descripcion { get; set; }
    public string? TipoRubro { get; set; }
    public decimal? Cantidad { get; set; }
    public decimal? ValorUnitario { get; set; }
    public decimal? ValorTotal { get; set; }
    public int? Pagina { get; set; }
}

public sealed class PaginaClasificadaDto
{
    public int Pagina { get; set; }
    public string? Tipo { get; set; }
    /// <summary>Tipo clínico de ESA hoja (REQ-019m).</summary>
    public string? TipoSoporte { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool TieneFacturaValida { get; set; }
    public decimal? ValorDetectado { get; set; }
}

public sealed class SplitPorTipoDto { public string? Tipo { get; set; } public decimal Valor { get; set; } public List<int> Documentos { get; set; } = new(); }

public sealed class AlertaClasificacionDto { public string? Codigo { get; set; } public string? Mensaje { get; set; } public int? DocId { get; set; } public int? Pagina { get; set; } }

/// <summary>Presentación de un tipo (familia + factura/soporte) para chips y KPIs.</summary>
public static class TipoReembolsoUi
{
    /// <summary>Familia (MED, ATE_HOS…) desde una etiqueta 'MED-FACTURA'.</summary>
    public static string Familia(string? tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo)) return "GENERAL";
        var i = tipo.IndexOf('-');
        return (i > 0 ? tipo[..i] : tipo).ToUpperInvariant();
    }

    /// <summary>true si la etiqueta es de factura (sólido) y no de soporte (contorno).</summary>
    public static bool EsFactura(string? tipo) =>
        (tipo ?? "").EndsWith("FACTURA", StringComparison.OrdinalIgnoreCase);

    /// <summary>Clase CSS del chip: familia + variante.</summary>
    public static string Css(string? tipo) =>
        $"fam-{Familia(tipo).ToLowerInvariant().Replace('_', '-')} {(EsFactura(tipo) ? "is-factura" : "is-soporte")}";

    /// <summary>Nombre legible de la familia.</summary>
    public static string Nombre(string? familiaOTipo) => Familia(familiaOTipo) switch
    {
        "MED"     => "Medicina",
        "ATE_HOS" => "Hospitalización",
        "PRO"     => "Procedimientos",
        "CON_MED" => "Consulta médica",
        "LAB_CLI" => "Laboratorio clínico",
        "LAB_IMA" => "Imagen",
        "TER"     => "Terapias",
        "BEN_ADI" => "Beneficio adicional",
        _         => "General"
    };

    /// <summary>Icono FontAwesome por familia.</summary>
    public static string Icono(string? familiaOTipo) => Familia(familiaOTipo) switch
    {
        "MED"     => "fa-medkit",
        "ATE_HOS" => "fa-hospital-o",
        "PRO"     => "fa-scissors",
        "CON_MED" => "fa-user-md",
        "LAB_CLI" => "fa-flask",
        "LAB_IMA" => "fa-photo",
        "TER"     => "fa-heartbeat",
        "BEN_ADI" => "fa-gift",
        _         => "fa-file-o"
    };
}

/// <summary>ViewModel de la pantalla de Tipificación del caso.</summary>
public sealed class ClasificacionViewModel
{
    public Guid CaseCode { get; init; }
    public string ShortCode => CaseCode.ToString().Split('-')[0];

    public bool Generada { get; init; }
    public ClasificacionSobreDto? Clasificacion { get; init; }
    public string? RawText { get; init; }
    public string? Error { get; init; }
    public DateTime? GeneradoEn { get; init; }
    public string GeneradoPor { get; init; } = string.Empty;

    /// <summary>Nº de documentos del caso y cuántos tienen OCR por página.</summary>
    public int TotalDocumentos { get; init; }
    public int DocumentosConPaginas { get; init; }

    public string TipoPredominanteNombre => TipoReembolsoUi.Nombre(Clasificacion?.TipoPredominante);
    public string TipoPredominanteCss => TipoReembolsoUi.Css(Clasificacion?.TipoPredominante);
    public string TipoPredominanteIcono => TipoReembolsoUi.Icono(Clasificacion?.TipoPredominante);

    /// <summary>Split ordenado por valor descendente con su % del total.</summary>
    public IEnumerable<(SplitPorTipoDto Split, decimal Pct)> SplitConPorcentaje()
    {
        var total = Clasificacion?.TotalSobre ?? 0m;
        foreach (var s in (Clasificacion?.SplitPorTipo ?? new()).OrderByDescending(x => x.Valor))
            yield return (s, total > 0 ? Math.Round(s.Valor / total * 100m, 1) : 0m);
    }
}
