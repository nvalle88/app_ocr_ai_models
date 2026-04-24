using app_tramites.Models.ModelAi;
using Microsoft.AspNetCore.Identity;

namespace app_ocr_ai_models.Models.ViewModel
{
    public class MantenedorConfigViewModel
    {
        public List<OPAIConfiguration> Configuraciones { get; set; } = new();
        public List<Agent>             Agentes          { get; set; } = new();
        public List<OPAIPrompt>        Prompts          { get; set; } = new();
        public List<OPAIModelPrompt>   Asignaciones     { get; set; } = new();
        public List<Process>           Procesos         { get; set; } = new();
        public List<AgentProcess>      AgentProcesos    { get; set; } = new();
        public List<AccessAgentPolicy> Politicas        { get; set; } = new();
        public List<AzureBlobConf>     BlobConfs        { get; set; } = new();
        public List<IdentityUser>      Usuarios         { get; set; } = new();
        public List<Policys>           ListaPolicys     { get; set; } = new();
        public List<PolicyUser>        PolicyUsers      { get; set; } = new();
    }
}
