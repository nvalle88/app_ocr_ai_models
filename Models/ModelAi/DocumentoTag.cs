using System;

namespace app_tramites.Models.ModelAi;

// REQ-019: tags por documento Y por página (spec §9.1).
// PageNumber NULL ⇒ el tag aplica al documento completo.
public partial class DocumentoTag
{
    public long Id { get; set; }

    public int DataFileId { get; set; }

    public int? PageNumber { get; set; }

    public string Tag { get; set; } = null!;

    /// <summary>TIPO | MARCA | RUBRO | DIAGNOSTICO | TOTAL.</summary>
    public string? Categoria { get; set; }

    /// <summary>'IA' (clasificador) o 'Regla' (keywords de spec §5).</summary>
    public string Origen { get; set; } = null!;

    public decimal? Confianza { get; set; }

    public string? Valor { get; set; }

    public DateTime CreatedDate { get; set; }

    public virtual DataFile DataFileNavigation { get; set; } = null!;
}
