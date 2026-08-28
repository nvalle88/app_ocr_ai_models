using System;

namespace app_tramites.Models.ModelAi;

// REQ-019: desglose ítem por ítem de la factura (spec §9.2).
public partial class DocumentoItem
{
    public long Id { get; set; }

    public int DataFileId { get; set; }

    /// <summary>Página donde aparece el rubro (opcional).</summary>
    public int? PageNumber { get; set; }

    public int? Orden { get; set; }

    /// <summary>Separa rubros cuando el archivo trae varias facturas (spec §3.3).</summary>
    public string? NumeroFactura { get; set; }

    public string Descripcion { get; set; } = null!;

    /// <summary>MED | ATE_HOS | PRO | CON_MED | LAB_CLI | LAB_IMA | TER | BEN_ADI | GENERAL.</summary>
    public string TipoRubro { get; set; } = null!;

    public decimal? Cantidad { get; set; }

    public decimal? ValorUnitario { get; set; }

    public decimal? ValorTotal { get; set; }

    public decimal? Confianza { get; set; }

    public DateTime CreatedDate { get; set; }

    public virtual DataFile DataFileNavigation { get; set; } = null!;
}
