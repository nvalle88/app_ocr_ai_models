namespace app_tramites.Models.ModelAi;

public partial class DataFile
{
    public int Id { get; set; }

    public bool IsFileUri { get; set; }

    public string FileUri { get; set; } = null!;

    public string Text { get; set; } = null!;

    public Guid CaseCode { get; set; }

    public DateTime CreatedDate { get; set; }

    public string OriginalName { get; set; } = string.Empty;

    public virtual ProcessCase CaseCodeNavigation { get; set; } = null!;

    // REQ-019 T1/T19: ID de archivo en la Files API de Claude (nullable — solo para archivos subidos a Anthropic)
    public string? ClaudeFileId { get; set; }

    // REQ-019 / clasificación de documentos de reembolso: hijos del archivo.
    public virtual ICollection<DataFilePage> DataFilePage { get; set; } = new List<DataFilePage>();

    public virtual ICollection<DocumentoClasificacion> DocumentoClasificacion { get; set; } = new List<DocumentoClasificacion>();

    public virtual ICollection<DocumentoItem> DocumentoItem { get; set; } = new List<DocumentoItem>();

    public virtual ICollection<DocumentoTag> DocumentoTag { get; set; } = new List<DocumentoTag>();

    public virtual ICollection<DocumentoDiagnostico> DocumentoDiagnostico { get; set; } = new List<DocumentoDiagnostico>();
}
