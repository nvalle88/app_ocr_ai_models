using app_ocr_ai_models.Data;
using app_ocr_ai_models.Models.ViewModel;
using app_tramites.Models.ModelAi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SmartAdmin.Web.Controllers
{
    [Authorize]
    public class MantenedoresController : Controller
    {
        private readonly OCRDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public MantenedoresController(OCRDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.ConfigCount       = await _db.OPAIConfiguration.CountAsync(x => x.IsActive);
            ViewBag.AgentCount        = await _db.Agent.CountAsync(x => x.IsActive);
            ViewBag.PromptCount       = await _db.OPAIPrompt.CountAsync(x => x.IsActive);
            ViewBag.AsignacionCount   = await _db.OPAIModelPrompt.CountAsync();
            ViewBag.ProcesoCount      = await _db.Process.CountAsync();
            ViewBag.AgentProcessCount = await _db.AgentProcesses.CountAsync();
            ViewBag.PolicyCount       = await _db.AccessAgentPolicies.CountAsync(x => x.Status);
            ViewBag.BlobCount         = await _db.AzureBlobConf.CountAsync();
            ViewBag.UserCount         = await _userManager.Users.CountAsync();
            return View();
        }

        public async Task<IActionResult> Config()
        {
            var model = new MantenedorConfigViewModel
            {
                Configuraciones = await _db.OPAIConfiguration.OrderBy(x => x.Name).ToListAsync(),
                Agentes         = await _db.Agent.Include(a => a.AgentConfig).OrderBy(x => x.Name).ToListAsync(),
                Prompts         = await _db.OPAIPrompt.OrderBy(x => x.Code).ToListAsync(),
                Asignaciones    = await _db.OPAIModelPrompt
                                      .Include(x => x.ModelCodeNavigation)
                                      .Include(x => x.PromptCodeNavigation)
                                      .OrderBy(x => x.ModelCode).ThenBy(x => x.Order).ToListAsync(),
                Procesos        = await _db.Process.OrderBy(x => x.Name).ToListAsync(),
                AgentProcesos   = await _db.AgentProcesses
                                      .Include(x => x.Agent)
                                      .Include(x => x.Process)
                                      .ToListAsync(),
                Politicas       = await _db.AccessAgentPolicies
                                      .Include(x => x.Policy)
                                      .Include(x => x.AgentProcess).ThenInclude(ap => ap.Agent)
                                      .Include(x => x.AgentProcess).ThenInclude(ap => ap.Process)
                                      .ToListAsync(),
                BlobConfs       = await _db.AzureBlobConf.ToListAsync(),
                Usuarios        = await _userManager.Users.ToListAsync(),
                ListaPolicys    = await _db.Policies.OrderBy(x => x.PolicyName).ToListAsync(),
                PolicyUsers     = await _db.PolicyUsers.ToListAsync()
            };
            return View(model);
        }

        // ══ CONFIGURACION AI ══════════════════════════════════════════════════

        [HttpPost, IgnoreAntiforgeryToken]
        public async Task<IActionResult> SaveConfiguracionAi([FromBody] OPAIConfiguration m)
        {
            try
            {
                var ex = await _db.OPAIConfiguration.FindAsync(m.Code);
                if (ex == null) { m.CreatedDate = m.ModifiedDate = DateTime.Now; _db.OPAIConfiguration.Add(m); }
                else { ex.Name = m.Name; ex.EndpointUrl = m.EndpointUrl; ex.ApiKey = m.ApiKey; ex.ConfigType = m.ConfigType; ex.Notes = m.Notes; ex.IsActive = m.IsActive; ex.ModifiedDate = DateTime.Now; }
                await _db.SaveChangesAsync();
                return Json(new { Estado = "OK" });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }

        // ══ AGENTE (con prompts y procesos) ══════════════════════════════════

        [HttpPost, IgnoreAntiforgeryToken]
        public async Task<IActionResult> SaveAgenteFull([FromBody] SaveAgenteFullDto dto)
        {
            try
            {
                var ex = await _db.Agent.FindAsync(dto.Code);
                if (ex == null)
                {
                    _db.Agent.Add(new Agent
                    {
                        Code = dto.Code, Name = dto.Name, ConfigCode = dto.ConfigCode,
                        VersionNumber = dto.VersionNumber, Description = dto.Description,
                        IsActive = dto.IsActive, CreatedDate = DateTime.Now, ModifiedDate = DateTime.Now
                    });
                }
                else
                {
                    ex.Name = dto.Name; ex.ConfigCode = dto.ConfigCode; ex.VersionNumber = dto.VersionNumber;
                    ex.Description = dto.Description; ex.IsActive = dto.IsActive; ex.ModifiedDate = DateTime.Now;
                }
                await _db.SaveChangesAsync();

                // Sync prompts (delete-recreate safe here)
                var oldPrompts = await _db.OPAIModelPrompt.Where(x => x.ModelCode == dto.Code).ToListAsync();
                _db.OPAIModelPrompt.RemoveRange(oldPrompts);
                foreach (var p in dto.Prompts)
                    _db.OPAIModelPrompt.Add(new OPAIModelPrompt { ModelCode = dto.Code, PromptCode = p.PromptCode, Order = p.Order, IsDefault = p.IsDefault });

                // Sync processes (cascade-aware: remove AccessAgentPolicies before AgentProcess)
                var existingAp = await _db.AgentProcesses.Where(x => x.AgentCode == dto.Code).ToListAsync();
                var toRemoveAp = existingAp.Where(e => !dto.Procesos.Contains(e.DefinitionCode)).ToList();
                if (toRemoveAp.Any())
                {
                    var ids = toRemoveAp.Select(x => x.Id).ToList();
                    _db.AccessAgentPolicies.RemoveRange(await _db.AccessAgentPolicies.Where(x => ids.Contains(x.AgentProcessId)).ToListAsync());
                    _db.AgentProcesses.RemoveRange(toRemoveAp);
                }
                var existingCodes = existingAp.Select(e => e.DefinitionCode).ToList();
                foreach (var code in dto.Procesos.Except(existingCodes))
                    _db.AgentProcesses.Add(new AgentProcess { AgentCode = dto.Code, DefinitionCode = code });

                await _db.SaveChangesAsync();
                return Json(new { Estado = "OK" });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }

        // ══ PROMPT ═══════════════════════════════════════════════════════════

        [HttpPost, IgnoreAntiforgeryToken]
        public async Task<IActionResult> SavePrompt([FromBody] OPAIPrompt m)
        {
            try
            {
                var ex = await _db.OPAIPrompt.FindAsync(m.Code);
                if (ex == null) { m.CreatedDate = m.ModifiedDate = DateTime.Now; _db.OPAIPrompt.Add(m); }
                else { ex.Content = m.Content; ex.VersionNumber = m.VersionNumber; ex.IsActive = m.IsActive; ex.ModifiedDate = DateTime.Now; }
                await _db.SaveChangesAsync();
                return Json(new { Estado = "OK" });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }

        // ══ PROCESO (con agentes) ═════════════════════════════════════════════

        [HttpPost, IgnoreAntiforgeryToken]
        public async Task<IActionResult> SaveProcesoFull([FromBody] SaveProcesoFullDto dto)
        {
            try
            {
                var ex = await _db.Process.FindAsync(dto.Code);
                if (ex == null) _db.Process.Add(new Process { Code = dto.Code, Name = dto.Name, Description = dto.Description });
                else { ex.Name = dto.Name; ex.Description = dto.Description; }
                await _db.SaveChangesAsync();

                // Sync agents for this process
                var existing = await _db.AgentProcesses.Where(x => x.DefinitionCode == dto.Code).ToListAsync();
                var toRemove = existing.Where(e => !dto.Agentes.Contains(e.AgentCode)).ToList();
                if (toRemove.Any())
                {
                    var ids = toRemove.Select(x => x.Id).ToList();
                    _db.AccessAgentPolicies.RemoveRange(await _db.AccessAgentPolicies.Where(x => ids.Contains(x.AgentProcessId)).ToListAsync());
                    _db.AgentProcesses.RemoveRange(toRemove);
                }
                var existingAgents = existing.Select(e => e.AgentCode).ToList();
                foreach (var agentCode in dto.Agentes.Except(existingAgents))
                    _db.AgentProcesses.Add(new AgentProcess { AgentCode = agentCode, DefinitionCode = dto.Code });

                await _db.SaveChangesAsync();
                return Json(new { Estado = "OK" });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }

        // ══ POLITICA (con coberturas AgentProcess) ════════════════════════════

        [HttpPost, IgnoreAntiforgeryToken]
        public async Task<IActionResult> SavePoliciaFull([FromBody] SavePoliciaFullDto dto)
        {
            try
            {
                var ex = await _db.Policies.FindAsync(dto.Code);
                if (ex == null) _db.Policies.Add(new Policys { Code = dto.Code, PolicyName = dto.PolicyName });
                else ex.PolicyName = dto.PolicyName;
                await _db.SaveChangesAsync();

                // Sync AccessAgentPolicies
                var existing = await _db.AccessAgentPolicies.Where(x => x.PolicyCode == dto.Code).ToListAsync();
                _db.AccessAgentPolicies.RemoveRange(existing);
                foreach (var apId in dto.AgentProcessIds)
                    _db.AccessAgentPolicies.Add(new AccessAgentPolicy { PolicyCode = dto.Code, AgentProcessId = apId, Status = true });

                await _db.SaveChangesAsync();
                return Json(new { Estado = "OK" });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }

        [HttpGet]
        public async Task<IActionResult> DeleteMantPolitica(string code)
        {
            try
            {
                _db.PolicyUsers.RemoveRange(await _db.PolicyUsers.Where(x => x.PolicyCode == code).ToListAsync());
                _db.AccessAgentPolicies.RemoveRange(await _db.AccessAgentPolicies.Where(x => x.PolicyCode == code).ToListAsync());
                var pol = await _db.Policies.FindAsync(code);
                if (pol != null) _db.Policies.Remove(pol);
                await _db.SaveChangesAsync();
                return Json(new { Estado = "OK" });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }

        // ══ USUARIO (con políticas) ══════════════════════════════════════════

        [HttpPost, IgnoreAntiforgeryToken]
        public async Task<IActionResult> SaveUsuarioFull([FromBody] SaveUsuarioFullDto dto)
        {
            try
            {
                string userId;
                if (string.IsNullOrEmpty(dto.Id))
                {
                    var newUser = new IdentityUser { UserName = dto.Email, Email = dto.Email };
                    var result = await _userManager.CreateAsync(newUser, dto.Password ?? "Default@123");
                    if (!result.Succeeded)
                        return Json(new { Estado = "Error", Mensaje = string.Join(", ", result.Errors.Select(e => e.Description)) });
                    userId = newUser.Id;
                }
                else
                {
                    userId = dto.Id;
                    var user = await _userManager.FindByIdAsync(userId);
                    if (user != null && user.Email != dto.Email)
                    {
                        user.Email = dto.Email;
                        user.UserName = dto.Email;
                        user.NormalizedEmail = dto.Email.ToUpper();
                        user.NormalizedUserName = dto.Email.ToUpper();
                        await _userManager.UpdateAsync(user);
                    }
                    if (user != null && !string.IsNullOrEmpty(dto.Password))
                    {
                        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                        var r = await _userManager.ResetPasswordAsync(user, token, dto.Password);
                        if (!r.Succeeded)
                            return Json(new { Estado = "Error", Mensaje = string.Join(", ", r.Errors.Select(e => e.Description)) });
                    }
                }

                // Sync PolicyUser
                _db.PolicyUsers.RemoveRange(await _db.PolicyUsers.Where(x => x.UserId == userId).ToListAsync());
                foreach (var code in dto.PolicyCodes)
                    _db.PolicyUsers.Add(new PolicyUser { UserId = userId, PolicyCode = code });
                await _db.SaveChangesAsync();
                return Json(new { Estado = "OK" });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }

        [HttpGet]
        public async Task<IActionResult> DeleteMantUsuario(string id)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(id);
                if (user != null) await _userManager.DeleteAsync(user);
                return Json(new { Estado = "OK" });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }

        // ══ AZURE BLOB ════════════════════════════════════════════════════════

        [HttpPost, IgnoreAntiforgeryToken]
        public async Task<IActionResult> SaveAzureBlob([FromBody] AzureBlobConf m)
        {
            try
            {
                var ex = await _db.AzureBlobConf.FindAsync(m.Codigo);
                if (ex == null) { _db.AzureBlobConf.Add(m); }
                else { ex.ConnectionString = m.ConnectionString; ex.ContainerName = m.ContainerName; }
                await _db.SaveChangesAsync();
                return Json(new { Estado = "OK" });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }
    }
}
