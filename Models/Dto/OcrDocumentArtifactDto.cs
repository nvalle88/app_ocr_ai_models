namespace app_tramites.Models.Dto;

public class OcrDocumentArtifactDto
{
    public string SourceFileName { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public DateTime ProcessedAtUtc { get; set; }
    public string Content { get; set; } = string.Empty;
    public OcrArtifactSummaryDto Summary { get; set; } = new();
    public List<OcrArtifactPageDto> Pages { get; set; } = [];
    public List<OcrArtifactParagraphDto> Paragraphs { get; set; } = [];
    public List<OcrArtifactKeyValueDto> KeyValuePairs { get; set; } = [];
    public List<OcrArtifactTableDto> Tables { get; set; } = [];
    public List<OcrArtifactDocumentDto> Documents { get; set; } = [];
}

public class OcrArtifactSummaryDto
{
    public string DocumentKind { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string SuggestedLabel { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public List<OcrArtifactHighlightDto> Highlights { get; set; } = [];
}

public class OcrArtifactHighlightDto
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int? PageNumber { get; set; }
    public double? Confidence { get; set; }
    public List<OcrArtifactPointDto> Polygon { get; set; } = [];
}

public class OcrArtifactPageDto
{
    public int PageNumber { get; set; }
    public float? Width { get; set; }
    public float? Height { get; set; }
    public string Unit { get; set; } = string.Empty;
    public List<OcrArtifactLineDto> Lines { get; set; } = [];
    public List<OcrArtifactWordDto> Words { get; set; } = [];
}

public class OcrArtifactLineDto
{
    public string Content { get; set; } = string.Empty;
    public int Offset { get; set; }
    public int Length { get; set; }
    public List<OcrArtifactPointDto> Polygon { get; set; } = [];
}

public class OcrArtifactWordDto
{
    public string Content { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public int Offset { get; set; }
    public int Length { get; set; }
    public List<OcrArtifactPointDto> Polygon { get; set; } = [];
}

public class OcrArtifactParagraphDto
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<OcrArtifactRegionDto> Regions { get; set; } = [];
}

public class OcrArtifactKeyValueDto
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public List<OcrArtifactRegionDto> KeyRegions { get; set; } = [];
    public List<OcrArtifactRegionDto> ValueRegions { get; set; } = [];
}

public class OcrArtifactTableDto
{
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public List<OcrArtifactRegionDto> Regions { get; set; } = [];
    public List<OcrArtifactTableCellDto> Cells { get; set; } = [];
}

public class OcrArtifactTableCellDto
{
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
    public int RowSpan { get; set; }
    public int ColumnSpan { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<OcrArtifactRegionDto> Regions { get; set; } = [];
}

public class OcrArtifactDocumentDto
{
    public string DocumentType { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public List<OcrArtifactRegionDto> Regions { get; set; } = [];
    public List<OcrArtifactDocumentFieldDto> Fields { get; set; } = [];
}

public class OcrArtifactDocumentFieldDto
{
    public string Name { get; set; } = string.Empty;
    public string FieldType { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public List<OcrArtifactRegionDto> Regions { get; set; } = [];
}

public class OcrArtifactRegionDto
{
    public int PageNumber { get; set; }
    public List<OcrArtifactPointDto> Polygon { get; set; } = [];
}

public class OcrArtifactPointDto
{
    public float X { get; set; }
    public float Y { get; set; }
}
