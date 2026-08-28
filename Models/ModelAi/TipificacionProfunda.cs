using System;
using System.Collections.Generic;

namespace app_tramites.Models.ModelAi;

// =============================================================================
// REQ-019m — Tipificación profunda del sobre.
//
// Tres piezas nuevas:
//   · DocumentoProcedimiento      — los CPT que trae cada documento, ya cruzados
//                                   contra el catálogo de códigos de liquidación.
//   · ClasificacionSobre          — a nivel de SOBRE: tipo de atención
//                                   (hospitalario / hospital del día / ambulatorio)
//                                   con los indicadores que lo sustentan.
//   · CatalogoCodigoLiquidacion   — el catálogo de Saludsa, administrable en tabla.
//
// El cruce CPT → código de liquidación es un JOIN, NO una pregunta al modelo:
// un lookup determinista no se le delega a una IA.
// =============================================================================

/// <summary>Un procedimiento (CPT) detectado en un documento del sobre.</summary>
public partial class DocumentoProcedimiento
{
    public long Id { get; set; }

    public int DataFileId { get; set; }

    /// <summary>Página donde aparece, si se pudo ubicar.</summary>
    public int? PageNumber { get; set; }

    /// <summary>Código tal como lo trae el documento (puede venir null si solo hay descripción).</summary>
    public string? CodigoCpt { get; set; }

    public string Descripcion { get; set; } = null!;

    /// <summary>Código de liquidación Saludsa que le corresponde (resultado del JOIN).</summary>
    public string? CodigoLiquidacion { get; set; }

    /// <summary>Rubro del catálogo, copiado para que el histórico no cambie si se edita el catálogo.</summary>
    public string? RubroLiquidacion { get; set; }

    /// <summary>
    /// De dónde salió el código: <c>CPT</c> (el documento traía el código y coincidió),
    /// <c>DESCRIPCION</c> (no había código; se emparejó por texto — es una SUGERENCIA que
    /// el analista debe validar) o null (no se pudo emparejar).
    /// </summary>
    public string? OrigenMatch { get; set; }

    // ── REQ-019r: homologación contra el catálogo real (Salud.dbo.Lr05) ────

    /// <summary>Lr05.NumeroProcedimiento — el código real del procedimiento.</summary>
    public int? NumeroProcedimiento { get; set; }

    /// <summary>Lr05.NombreEspanol, copiado para que el histórico no cambie.</summary>
    public string? NombreLr05 { get; set; }

    /// <summary>A010 = medicina, H001 honorarios, A004 imagen, A003 laboratorio…</summary>
    public string? CodigoBeneficio { get; set; }

    public bool? EsMedicina { get; set; }

    /// <summary>MARCA | GENERICA | null — A010 es marca, A011 genérica.</summary>
    public string? TipoMedicina { get; set; }

    /// <summary>F1 del emparejamiento de texto (0..1).</summary>
    public decimal? ScoreHomologacion { get; set; }

    /// <summary>Hubo empate con otro procedimiento: el analista debe mirarlo.</summary>
    public bool? HomologacionAmbigua { get; set; }

    // ── Correlación diagnóstico ↔ procedimiento (Salud.dbo.Lr46) ───────────

    /// <summary>CORRELACIONA | NO_CORRELACIONA | SIN_VALIDAR.</summary>
    public string? EstadoCorrelacion { get; set; }

    public int? CorrelacionProb { get; set; }
    public string? CorrelacionDx { get; set; }
    public bool? CorrelacionConfirmada { get; set; }

    /// <summary>Umbral que se aplicó (70 medicina / 1 procedimiento): auditabilidad.</summary>
    public int? UmbralAplicado { get; set; }

    public DateTime CreatedDate { get; set; }

    public virtual DataFile DataFileNavigation { get; set; } = null!;
}

/// <summary>
/// Clasificación a nivel de sobre: qué clase de atención es. Versionada:
/// solo una fila con IsCurrent = 1 por CaseCode.
/// </summary>
public partial class ClasificacionSobre
{
    public long Id { get; set; }

    public Guid CaseCode { get; set; }

    /// <summary>HOSPITALARIO | HOSPITAL_DIA | AMBULATORIO | DESCONOCIDO.</summary>
    public string TipoAtencion { get; set; } = null!;

    /// <summary>2-4 líneas explicando la decisión, citando los indicadores en true.</summary>
    public string? Justificacion { get; set; }

    /// <summary>Los 13 indicadores como JSON: [{ nombre, presente, docId }].</summary>
    public string? IndicadoresJson { get; set; }

    public string? TipoPredominante { get; set; }

    public decimal? TotalSobre { get; set; }

    public string? ModelCode { get; set; }

    public int VersionNumber { get; set; }

    public bool IsCurrent { get; set; }

    public DateTime CreatedDate { get; set; }
}

/// <summary>
/// Catálogo de códigos de liquidación de Saludsa. Vive en tabla justamente para
/// que Operaciones lo mantenga sin desplegar nada.
/// </summary>
public partial class CatalogoCodigoLiquidacion
{
    public string Codigo { get; set; } = null!;

    public string Rubro { get; set; } = null!;

    public string? Descripcion { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedDate { get; set; }
}


/// <summary>
/// Umbral de correlación por familia de beneficio de Lr05. Vive en tabla para
/// que Operaciones lo ajuste sin desplegar: hoy A010 (medicina/farmacia) exige
/// 70% y el resto (procedimientos) basta con que exista con probabilidad > 0.
/// </summary>
public partial class CatalogoBeneficioCorrelacion
{
    /// <summary>CodigoBeneficio de Lr05, o 'DEFAULT' para el resto.</summary>
    public string CodigoBeneficio { get; set; } = null!;

    public string? Descripcion { get; set; }

    public bool EsMedicina { get; set; }

    /// <summary>Probabilidad MÍNIMA (inclusive) de Lr46 para dar por correlacionado.</summary>
    public int UmbralProbabilidad { get; set; }

    /// <summary>MARCA | GENERICA | null (solo aplica a los beneficios de medicina).</summary>
    public string? TipoMedicina { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedDate { get; set; }
}
