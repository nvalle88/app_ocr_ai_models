using app_tramites.Models.Dto;
using app_tramites.Models.ModelAi;
using app_tramites.Models.ViewModel;
using Microsoft.AspNetCore.Identity;

namespace app_tramites.Services.NexusProcess
{
    public interface INexusService
    {
        Task<FinalResponseResult> EjecutarPrompt(PromptRequest req);
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
        Task<List<ProcessCase>?> ObtenerProcesos();
        Task<ViewProcessUser> GetProcessesByUser(IdentityUser? user, IList<string>? roles);
        Task<ViewCreateCase> CreateCaseProcess(QueryInput input);
    }
}
