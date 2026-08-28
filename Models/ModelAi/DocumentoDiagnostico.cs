using System;

namespace app_tramites.Models.ModelAi;

// REQ-019: CIE10 extraídos del documento (spec §2 y §7).
public partial class DocumentoDiagnostico
{
    public long Id { get; set; }

    public int DataFileId { get; set; }

    public int? PageNumber { get; set; }

    /// <summary>CIE10 NORMALIZADO, sin puntos: 'M51.9' ⇒ 'M519' (spec §2).</summary>
    public string Codigo { get; set; } = null!;

    /// <summary>Código tal como lo devolvió el OCR/clasificador, antes de normalizar.</summary>
    public string? CodigoOriginal { get; set; }

    public string? Descripcion { get; set; }

    /// <summary>ValorTotal COMPLETO del archivo asignado a este dx (spec §7).</summary>
    public decimal? ValorAsignado { get; set; }

    public DateTime CreatedDate { get; set; }

    public virtual DataFile DataFileNavigation { get; set; } = null!;
}
