using app_tramites.Models.Dto;
using app_tramites.Models.External;
using app_tramites.Models.ModelAi;
using app_tramites.Models.ViewModel;
using Microsoft.AspNetCore.Identity;

namespace app_tramites.Services.NexusProcess
{
    public interface INexusService
    {
        Task<ResponsePromptDto> EjecutarPrompt(PromptRequest req);
        AgentProcess? BuscarPromptPorAgenteProceso(PromptRequest req, Process process);
        Task<OpenAiResponseDto> CallOpenAiAsync(
            Agent agent,
            string systemContent,
            string userText,
            int dataFileId,
            int stepOrder,
            int maxTokens = 1000,
            double temperature = 0.2,
            double topP = 1.0);
        Task<ProcessCase?> ObtenerProcessCase(Guid caseCode);
        Task<ViewPagedProcessCases> ObtenerProcesos(
            IdentityUser? user,
            IList<string>? roles,
            int pageNumber = 1,
            int pageSize = 10,
            int windowDays = 0,
            string? search = null,
            string? status = null,
            string? type = null,
            string? process = null,
            string? period = null);
        Task<ViewProcessUser> GetProcessesByUser(IdentityUser? user, IList<string>? roles);
        Task<ViewCreateCase> CreateCaseProcess(QueryInput input);
        Task<ViewCaseDetails?> ObtenerDetailsProcessCase(Guid caseCode, IdentityUser? user);
        Task<List<DataFile>> AddDocumentsToCase(Guid caseCode, IReadOnlyCollection<OcrFile> files);
        Task<OcrDocumentArtifactDto?> ObtenerArtefactoDocumentoAsync(string fileUrl);
    }
}
