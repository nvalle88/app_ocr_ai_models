using app_ocr_ai_models.Data;
using app_tramites.Extensions;
using app_tramites.Models.Dto;
using app_tramites.Models.ViewModel;
using app_tramites.Services.NexusProcess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace app_tramites.Controllers.Api
{
    [AllowAnonymous]
    [ApiController]
    [Route("api/[controller]")]
    public class NexusApiController(INexusService nexusService, OCRDbContext db, UserManager<IdentityUser> userManager) : ControllerBase
    {

        [AllowAnonymous]
        // GET api/nexus/agenttypes?userId={userId}&processCode={processCode}
        [HttpGet("agenttypes")]
        public async Task<IActionResult> GetAgentTypes([FromQuery] string userId, [FromQuery] string processCode)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(processCode))
                return BadRequest("userId and processCode are required.");

            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound("User not found.");

            // Obtener AgentProcess ids permitidos por políticas del usuario
            var allowedAgentProcessIds = await db.PolicyUsers 
                .Where(pu => pu.UserId == user.Id)
                .SelectMany(pu => pu.Policys.AccessAgentPolicies.Select(aap => aap.AgentProcessId))
                .Distinct()
                .ToListAsync();

            // Base query de AgentProcesses
            var query = db.AgentProcesses
                .Include(ap => ap.Agent)
                    .ThenInclude(a => a.OPAIModelPrompt)
                    .ThenInclude(op => op.TypeAgentNavigation)
                .Where(ap => ap.DefinitionCode == processCode && ap.Agent.IsActive);

            if (allowedAgentProcessIds.Any())
            {
                query = query.Where(ap => allowedAgentProcessIds.Contains(ap.Id));
            }

            var agentProcesses = await query.ToListAsync();

            var dtoList = agentProcesses
                .SelectMany(ap => (ap.Agent.OPAIModelPrompt ?? Enumerable.Empty<dynamic>())
                    .Select(op => new { ap, catalog = op.TypeAgentNavigation }))
                .Where(x => x.catalog != null)
                .Select(x => new AgentTypeDto
                {
                    Code = (string)x.catalog.Code,
                    CatalogId = (int)x.catalog.Id,
                    DefinitionCode = x.ap.DefinitionCode,
                    AgentCode = x.ap.AgentCode
                })
                .DistinctBy(d => (d.CatalogId, d.AgentCode, d.DefinitionCode)) // eliminar duplicados
                .ToList();

            return Ok(dtoList);
        }


        [AllowAnonymous]
        [HttpPost("CreateCaseProcess")]
        public async Task<IActionResult> CreateCaseProcess([FromBody] QueryInput input)
        {
            try
            {
                var respuesta = await nexusService.CreateCaseProcess(input);
                return Ok(respuesta);                
            }
            catch (NegocioException e)
            {
                return NotFound(new
                {
                    error = true,
                    message = e.Message,
                    errorCode = e.ErrorCode
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    error = true,
                    message = ex.Message,
                });
            }
        }

        [AllowAnonymous]
        [HttpPost("ExecutePrompt")]
        public async Task<IActionResult> ExecutePrompt([FromBody] PromptRequest req)
        {
            var user = await userManager.GetUserAsync(User);
            if (req == null || req.CaseCode == Guid.Empty || string.IsNullOrEmpty(req.Origin))
                return BadRequest("Datos inválidos.");

            req.Usuario = user?.UserName ?? "Sistema";
            var respuesta = await nexusService.EjecutarPrompt(req);

            if (respuesta == null)
            {
                return NotFound("No se puede ejecutar la acción con los datos proporcionados.");
            }

           return Ok(respuesta);
        }
    }
}