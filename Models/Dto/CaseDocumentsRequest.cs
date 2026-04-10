using app_tramites.Models.ViewModel;

namespace app_tramites.Models.Dto;

public class CaseDocumentsRequest
{
    public Guid CaseCode { get; set; }
    public List<OcrFile> Files { get; set; } = [];
}
