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

        // ── Lo que hasta ahora solo se podia tocar por migracion SQL ──────
        // Dar de alta una tool eran tres scripts y acordarse de que Name debe
        // ser igual a Code, de que Description son 500 caracteres reales
        // aunque la columna diga 1000, y de registrar el placeholder {api-*}
        // en dos sitios. Eso es memoria de una persona, no del sistema.
        public List<OPAITool>          Tools            { get; set; } = new();
        public List<OPAIModelTool>     ToolAsignaciones { get; set; } = new();
        public List<OPAISkill>         Skills           { get; set; } = new();
        public List<OPAIModelSkill>    SkillAsignaciones{ get; set; } = new();
        public List<ProcessStep>       Pasos            { get; set; } = new();
    }
}
