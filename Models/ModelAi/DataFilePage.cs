using System;

namespace app_tramites.Models.ModelAi;

// REQ-019 / clasificación de documentos de reembolso:
// OCR POR PÁGINA. Hoy AnalyzeResult.Pages se descarta en OcrIngestService.
public partial class DataFilePage
{
    public int Id { get; set; }

    /// <summary>FK a DataFile.Id (int identity).</summary>
    public int DataFileId { get; set; }

    /// <summary>Número de página 1-based (DocumentPage.PageNumber del SDK).</summary>
    public int PageNumber { get; set; }

    /// <summary>Texto OCR reconstruido de ESTA página (varchar(max), igual que DataFile.Text).</summary>
    public string? Text { get; set; }

    public float? Width { get; set; }

    public float? Height { get; set; }

    /// <summary>'pixel' (imagen) o 'inch' (PDF) — DocumentPage.Unit.ToString().</summary>
    public string? Unit { get; set; }

    /// <summary>Orientación en grados, (-180, 180] — DocumentPage.Angle.</summary>
    public float? Angle { get; set; }

    public int? LineCount { get; set; }

    public int? WordCount { get; set; }

    public int? CharCount { get; set; }

    public DateTime CreatedDate { get; set; }

    public virtual DataFile DataFileNavigation { get; set; } = null!;
}
