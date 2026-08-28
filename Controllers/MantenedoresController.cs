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
                PolicyUsers     = await _db.PolicyUsers.ToListAsync(),

                Tools            = await _db.OPAITool.OrderBy(x => x.Name).ToListAsync(),
                ToolAsignaciones = await _db.OPAIModelTool
                                       .OrderBy(x => x.ModelCode).ThenBy(x => x.SortOrder).ToListAsync(),
                Skills           = await _db.OPAISkill.OrderBy(x => x.Name).ToListAsync(),
                SkillAsignaciones= await _db.OPAIModelSkill
                                       .OrderBy(x => x.ModelCode).ThenBy(x => x.SortOrder).ToListAsync(),
                Pasos            = await _db.ProcessStep
                                       .OrderBy(x => x.ProcessCode).ThenBy(x => x.StepOrder).ToListAsync()
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
                    // Un agente nuevo nacia SIN ModelId, MaxTokens ni ToolChoice, y
                    // asi no puede atender una sola peticion: ClaudeCompletionService
                    // necesita MaxTokens y el modelo. La pantalla no ofrece esos
                    // campos, asi que se heredan de otro agente de la misma
                    // configuracion; si no hay ninguno, valores de arranque
                    // razonables. Mejor un agente que funcione y se ajuste que uno
                    // que se guarda bien y falla al primer uso.
                    var hermano = await _db.Agent
                        .Where(a => a.ConfigCode == dto.ConfigCode && a.ModelId != null)
                        .OrderByDescending(a => a.ModifiedDate)
                        .FirstOrDefaultAsync();

                    _db.Agent.Add(new Agent
                    {
                        Code = dto.Code, Name = dto.Name, ConfigCode = dto.ConfigCode,
                        VersionNumber = dto.VersionNumber, Description = dto.Description,
                        IsActive = dto.IsActive, CreatedDate = DateTime.Now, ModifiedDate = DateTime.Now,
                        ModelId      = hermano?.ModelId,
                        MaxTokens    = hermano?.MaxTokens ?? 4000,
                        ThinkingMode = "off",
                        ToolChoice   = "auto"
                    });
                }
                else
                {
                    ex.Name = dto.Name; ex.ConfigCode = dto.ConfigCode; ex.VersionNumber = dto.VersionNumber;
                    ex.Description = dto.Description; ex.IsActive = dto.IsActive; ex.ModifiedDate = DateTime.Now;

                    // Solo lo que venga: la pantalla puede mandar unos campos y
                    // no otros, y un null aqui borraria el prompt del agente.
                    if (!string.IsNullOrWhiteSpace(dto.ModelId))      ex.ModelId      = dto.ModelId;
                    if (dto.MaxTokens is > 0)                         ex.MaxTokens    = dto.MaxTokens;
                    if (dto.SystemPrompt != null)                     ex.SystemPrompt = dto.SystemPrompt;
                    if (!string.IsNullOrWhiteSpace(dto.ThinkingMode)) ex.ThinkingMode = dto.ThinkingMode;
                    if (!string.IsNullOrWhiteSpace(dto.ToolChoice))   ex.ToolChoice   = dto.ToolChoice;
                }
                await _db.SaveChangesAsync();

                // Sync prompts (delete-recreate safe here)
                var oldPrompts = await _db.OPAIModelPrompt.Where(x => x.ModelCode == dto.Code).ToListAsync();
                _db.OPAIModelPrompt.RemoveRange(oldPrompts);
                foreach (var p in dto.Prompts)
                    _db.OPAIModelPrompt.Add(new OPAIModelPrompt { ModelCode = dto.Code, PromptCode = p.PromptCode, Order = p.Order, IsDefault = p.IsDefault });

                // Sync de tools y skills. Se rehacen enteras, como los prompts:
                // son tablas de enlace pequeñas y asi el orden queda tal cual lo
                // dejo la pantalla, sin huecos ni duplicados.
                _db.OPAIModelTool.RemoveRange(
                    await _db.OPAIModelTool.Where(x => x.ModelCode == dto.Code).ToListAsync());
                foreach (var t in dto.Tools)
                    _db.OPAIModelTool.Add(new OPAIModelTool
                    {
                        ModelCode = dto.Code, ToolCode = t.Code,
                        SortOrder = t.SortOrder, IsEnabled = t.IsEnabled
                    });

                _db.OPAIModelSkill.RemoveRange(
                    await _db.OPAIModelSkill.Where(x => x.ModelCode == dto.Code).ToListAsync());
                foreach (var k in dto.Skills)
                    _db.OPAIModelSkill.Add(new OPAIModelSkill
                    {
                        ModelCode = dto.Code, SkillCode = k.Code,
                        SortOrder = k.SortOrder, IsEnabled = k.IsEnabled
                    });

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

        // POST por la misma razon que el de usuarios: borra.
        [HttpPost, IgnoreAntiforgeryToken]
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

        // POST, no GET: borra. Una accion destructiva detras de un GET se dispara
        // con un prefetch del navegador, un acelerador de enlaces o alguien que
        // pega la URL en la barra.
        [HttpPost, IgnoreAntiforgeryToken]
        public async Task<IActionResult> DeleteMantUsuario(string id)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(id);
                if (user == null) return Json(new { Estado = "Error", Mensaje = "El usuario ya no existe." });

                // Primero sus politicas: dejarlas colgando de un usuario borrado
                // ensucia PolicyUsers y puede impedir el propio borrado por FK.
                _db.PolicyUsers.RemoveRange(await _db.PolicyUsers.Where(x => x.UserId == id).ToListAsync());
                await _db.SaveChangesAsync();

                // Y NO se ignora el resultado: antes se llamaba a DeleteAsync sin
                // mirarlo y se devolvia OK pasara lo que pasara. La pantalla decia
                // "borrado" y el usuario seguia ahi.
                var r = await _userManager.DeleteAsync(user);
                if (!r.Succeeded)
                    return Json(new { Estado = "Error",
                                      Mensaje = string.Join(", ", r.Errors.Select(e => e.Description)) });

                return Json(new { Estado = "OK" });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }

        // ══ TOOLS ════════════════════════════════════════════════════════════
        //
        // El mantenedor que no existía. Dar de alta una tool eran tres
        // migraciones SQL y acordarse de tres trampas que no avisan:
        //   * Name DEBE ser igual a Code, o el motor devuelve BadRequest y el
        //     paso del agente vuelve en un segundo sin haber hecho nada.
        //   * Description es nvarchar(1000) pero max_length va en BYTES: son
        //     500 CARACTERES. Pasarse no trunca, revienta el INSERT entero y la
        //     tool se queda en la versión anterior aparentando estar guardada.
        //   * InputSchema y BindingConfig son JSON: si no parsean, la tool
        //     existe y falla en ejecución.
        // Aquí se comprueban las tres antes de tocar la base.

        private const int MaxCaracteresDescripcion = 500;

        [HttpPost, IgnoreAntiforgeryToken]
        public async Task<IActionResult> SaveToolFull([FromBody] SaveToolFullDto dto)
        {
            try
            {
                var mal = ValidarTool(dto);
                if (mal != null) return Json(new { Estado = "Error", Mensaje = mal });

                var ex = await _db.OPAITool.FindAsync(dto.Code);
                if (ex == null)
                {
                    _db.OPAITool.Add(new OPAITool
                    {
                        Code = dto.Code, Name = dto.Code,   // Name = Code, siempre
                        Description = dto.Description, InputSchema = dto.InputSchema,
                        Strict = dto.Strict, BindingType = dto.BindingType,
                        BindingConfig = dto.BindingConfig, IsActive = dto.IsActive,
                        VersionNumber = dto.VersionNumber, CreatedDate = DateTime.UtcNow
                    });
                }
                else
                {
                    ex.Name = dto.Code;
                    ex.Description = dto.Description; ex.InputSchema = dto.InputSchema;
                    ex.Strict = dto.Strict; ex.BindingType = dto.BindingType;
                    ex.BindingConfig = dto.BindingConfig; ex.IsActive = dto.IsActive;
                    ex.VersionNumber = dto.VersionNumber;
                }
                await _db.SaveChangesAsync();

                // A qué agentes se la damos. El orden se calcula solo: al modelo
                // no le importa, y pedírselo a la persona es una decisión de más.
                _db.OPAIModelTool.RemoveRange(
                    await _db.OPAIModelTool.Where(x => x.ToolCode == dto.Code).ToListAsync());
                await _db.SaveChangesAsync();

                foreach (var agente in dto.Agentes.Distinct())
                {
                    var ultimo = await _db.OPAIModelTool
                        .Where(x => x.ModelCode == agente)
                        .MaxAsync(x => (int?)x.SortOrder) ?? 0;
                    _db.OPAIModelTool.Add(new OPAIModelTool
                    {
                        ModelCode = agente, ToolCode = dto.Code,
                        SortOrder = ultimo + 1, IsEnabled = true
                    });
                }
                await _db.SaveChangesAsync();
                return Json(new { Estado = "OK" });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }

        /// <summary>Las tres trampas, comprobadas antes de tocar la base.</summary>
        private static string? ValidarTool(SaveToolFullDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Code))
                return "El código es obligatorio: es el nombre con el que el modelo la invoca.";

            if ((dto.Description ?? "").Length > MaxCaracteresDescripcion)
                return $"La descripción tiene {dto.Description!.Length} caracteres y el máximo real "
                     + $"son {MaxCaracteresDescripcion}. La columna dice 1000, pero cuenta bytes.";

            foreach (var par in new[] { (Json: dto.InputSchema, Campo: "InputSchema"),
                                        (Json: dto.BindingConfig, Campo: "BindingConfig") })
            {
                if (string.IsNullOrWhiteSpace(par.Json)) continue;
                try { System.Text.Json.JsonDocument.Parse(par.Json!); }
                catch (System.Text.Json.JsonException je)
                { return $"{par.Campo} no es JSON válido: {je.Message}"; }
            }
            return null;
        }

        [HttpPost, IgnoreAntiforgeryToken]
        public async Task<IActionResult> DeleteMantTool(string code)
        {
            try
            {
                // Primero los enlaces: si no, la FK impide borrarla y el mensaje
                // que sale no le dice nada a nadie.
                _db.OPAIModelTool.RemoveRange(
                    await _db.OPAIModelTool.Where(x => x.ToolCode == code).ToListAsync());
                var t = await _db.OPAITool.FindAsync(code);
                if (t == null) return Json(new { Estado = "Error", Mensaje = "Esa herramienta ya no existe." });
                _db.OPAITool.Remove(t);
                await _db.SaveChangesAsync();
                return Json(new { Estado = "OK" });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }

        // ══ SKILLS ═══════════════════════════════════════════════════════════

        [HttpPost, IgnoreAntiforgeryToken]
        public async Task<IActionResult> SaveSkillFull([FromBody] SaveSkillFullDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Code))
                    return Json(new { Estado = "Error", Mensaje = "El código es obligatorio." });

                var ex = await _db.OPAISkill.FindAsync(dto.Code);
                if (ex == null)
                {
                    _db.OPAISkill.Add(new OPAISkill
                    {
                        Code = dto.Code, Name = dto.Name, SkillType = dto.SkillType,
                        SkillId = dto.SkillId, SkillVersion = dto.SkillVersion,
                        Description = dto.Description, IsActive = dto.IsActive,
                        VersionNumber = dto.VersionNumber, CreatedDate = DateTime.UtcNow
                    });
                }
                else
                {
                    ex.Name = dto.Name; ex.SkillType = dto.SkillType; ex.SkillId = dto.SkillId;
                    ex.SkillVersion = dto.SkillVersion; ex.Description = dto.Description;
                    ex.IsActive = dto.IsActive; ex.VersionNumber = dto.VersionNumber;
                }
                await _db.SaveChangesAsync();

                _db.OPAIModelSkill.RemoveRange(
                    await _db.OPAIModelSkill.Where(x => x.SkillCode == dto.Code).ToListAsync());
                await _db.SaveChangesAsync();

                foreach (var agente in dto.Agentes.Distinct())
                {
                    var ultimo = await _db.OPAIModelSkill
                        .Where(x => x.ModelCode == agente)
                        .MaxAsync(x => (int?)x.SortOrder) ?? 0;
                    _db.OPAIModelSkill.Add(new OPAIModelSkill
                    {
                        ModelCode = agente, SkillCode = dto.Code,
                        SortOrder = ultimo + 1, IsEnabled = true
                    });
                }
                await _db.SaveChangesAsync();
                return Json(new { Estado = "OK" });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }

        [HttpPost, IgnoreAntiforgeryToken]
        public async Task<IActionResult> DeleteMantSkill(string code)
        {
            try
            {
                _db.OPAIModelSkill.RemoveRange(
                    await _db.OPAIModelSkill.Where(x => x.SkillCode == code).ToListAsync());
                var k = await _db.OPAISkill.FindAsync(code);
                if (k == null) return Json(new { Estado = "Error", Mensaje = "Esa skill ya no existe." });
                _db.OPAISkill.Remove(k);
                await _db.SaveChangesAsync();
                return Json(new { Estado = "OK" });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }

        // ══ PASOS DE UN PROCESO ══════════════════════════════════════════════
        //
        // El proceso ya se podía crear; sus PASOS no, y son los que deciden qué
        // agente corre y en qué orden. Un proceso sin pasos no hace nada: el
        // orquestador recorre una lista vacía y devuelve texto vacío. Pasó
        // exactamente eso con PORTAL_CLIENTE y costó una tarde entenderlo.

        [HttpPost, IgnoreAntiforgeryToken]
        public async Task<IActionResult> SavePasosProceso([FromBody] SavePasosDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.ProcessCode))
                    return Json(new { Estado = "Error", Mensaje = "Falta el proceso." });

                _db.ProcessStep.RemoveRange(
                    await _db.ProcessStep.Where(x => x.ProcessCode == dto.ProcessCode).ToListAsync());
                await _db.SaveChangesAsync();

                // Se renumera al guardar: si se borra el paso 2, no tiene sentido
                // dejar un hueco entre el 1 y el 3.
                var orden = 0;
                foreach (var paso in dto.Pasos)
                {
                    _db.ProcessStep.Add(new ProcessStep
                    {
                        ProcessCode        = dto.ProcessCode,
                        StepOrder          = orden++,
                        ModelCode          = paso.ModelCode,
                        StepName           = paso.StepName,
                        StepsToInclude     = paso.StepsToInclude,
                        SourceType         = (InputSourceType)paso.SourceType,
                        AggregateExecution = paso.AggregateExecution
                    });
                }
                await _db.SaveChangesAsync();
                return Json(new { Estado = "OK", Mensaje = $"{dto.Pasos.Count} paso(s) guardados." });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }

        // ══ BORRADOS QUE NUNCA EXISTIERON ════════════════════════════════════
        //
        // Los cinco botones de la papelera llamaban a /{Entidad}/Delete —
        // /Agente/Delete, /ConfiguracionAi/Delete, /Proceso/Delete,
        // /Prompt/Delete, /AzureBlobConf/Delete. Esos controladores existen,
        // pero NINGUNO tiene una acción Delete: los cinco devolvían 404 y el
        // callback de bootbox no comprueba el fallo, así que la fila ni
        // desaparecía ni salía aviso. Pulsar y que no pase nada.
        //
        // Se implementan aquí, junto a los otros borrados, y cada uno se hace
        // cargo de sus dependencias: dejar que salte la FK produce un mensaje
        // que no le dice nada a nadie.

        [HttpPost, IgnoreAntiforgeryToken]
        public async Task<IActionResult> DeleteMantAgente(string code)
        {
            try
            {
                var ag = await _db.Agent.FindAsync(code);
                if (ag == null) return Json(new { Estado = "Error", Mensaje = "Ese agente ya no existe." });

                // Un agente puede estar en medio de un proceso. Borrarlo dejaría
                // el proceso sin ese paso y sin decírselo a nadie.
                var pasos = await _db.ProcessStep.CountAsync(x => x.ModelCode == code);
                if (pasos > 0)
                    return Json(new { Estado = "Error",
                                      Mensaje = $"No se puede borrar: es el agente de {pasos} paso(s) de proceso. "
                                              + "Quítelo primero de esos procesos." });

                var ap = await _db.AgentProcesses.Where(x => x.AgentCode == code).ToListAsync();
                if (ap.Count > 0)
                {
                    var ids = ap.Select(x => x.Id).ToList();
                    _db.AccessAgentPolicies.RemoveRange(
                        await _db.AccessAgentPolicies.Where(x => ids.Contains(x.AgentProcessId)).ToListAsync());
                    _db.AgentProcesses.RemoveRange(ap);
                }
                _db.OPAIModelPrompt.RemoveRange(await _db.OPAIModelPrompt.Where(x => x.ModelCode == code).ToListAsync());
                _db.OPAIModelTool.RemoveRange(await _db.OPAIModelTool.Where(x => x.ModelCode == code).ToListAsync());
                _db.OPAIModelSkill.RemoveRange(await _db.OPAIModelSkill.Where(x => x.ModelCode == code).ToListAsync());
                await _db.SaveChangesAsync();

                _db.Agent.Remove(ag);
                await _db.SaveChangesAsync();
                return Json(new { Estado = "OK" });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }

        [HttpPost, IgnoreAntiforgeryToken]
        public async Task<IActionResult> DeleteMantConfigAi(string code)
        {
            try
            {
                var cfg = await _db.OPAIConfiguration.FindAsync(code);
                if (cfg == null) return Json(new { Estado = "Error", Mensaje = "Esa configuración ya no existe." });

                var usada = await _db.Agent.CountAsync(a => a.ConfigCode == code);
                if (usada > 0)
                    return Json(new { Estado = "Error",
                                      Mensaje = $"No se puede borrar: la usan {usada} agente(s)." });

                _db.OPAIConfiguration.Remove(cfg);
                await _db.SaveChangesAsync();
                return Json(new { Estado = "OK" });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }

        [HttpPost, IgnoreAntiforgeryToken]
        public async Task<IActionResult> DeleteMantPrompt(string code)
        {
            try
            {
                var pr = await _db.OPAIPrompt.FindAsync(code);
                if (pr == null) return Json(new { Estado = "Error", Mensaje = "Ese prompt ya no existe." });

                var asignado = await _db.OPAIModelPrompt.CountAsync(x => x.PromptCode == code);
                if (asignado > 0)
                    return Json(new { Estado = "Error",
                                      Mensaje = $"No se puede borrar: está asignado a {asignado} agente(s)." });

                _db.OPAIPrompt.Remove(pr);
                await _db.SaveChangesAsync();
                return Json(new { Estado = "OK" });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }

        [HttpPost, IgnoreAntiforgeryToken]
        public async Task<IActionResult> DeleteMantProceso(string code)
        {
            try
            {
                var pc = await _db.Process.FindAsync(code);
                if (pc == null) return Json(new { Estado = "Error", Mensaje = "Ese proceso ya no existe." });

                var casos = await _db.ProcessCase.CountAsync(x => x.DefinitionCode == code);
                if (casos > 0)
                    return Json(new { Estado = "Error",
                                      Mensaje = $"No se puede borrar: tiene {casos} caso(s) procesados. "
                                              + "Un proceso con historia no se borra, se desactiva." });

                var ap = await _db.AgentProcesses.Where(x => x.DefinitionCode == code).ToListAsync();
                if (ap.Count > 0)
                {
                    var ids = ap.Select(x => x.Id).ToList();
                    _db.AccessAgentPolicies.RemoveRange(
                        await _db.AccessAgentPolicies.Where(x => ids.Contains(x.AgentProcessId)).ToListAsync());
                    _db.AgentProcesses.RemoveRange(ap);
                }
                _db.ProcessStep.RemoveRange(await _db.ProcessStep.Where(x => x.ProcessCode == code).ToListAsync());
                await _db.SaveChangesAsync();

                _db.Process.Remove(pc);
                await _db.SaveChangesAsync();
                return Json(new { Estado = "OK" });
            }
            catch (Exception e) { return Json(new { Estado = "Error", Mensaje = e.Message }); }
        }

        [HttpPost, IgnoreAntiforgeryToken]
        public async Task<IActionResult> DeleteMantBlob(string code)
        {
            try
            {
                var b = await _db.AzureBlobConf.FindAsync(code);
                if (b == null) return Json(new { Estado = "Error", Mensaje = "Esa configuración ya no existe." });
                _db.AzureBlobConf.Remove(b);
                await _db.SaveChangesAsync();
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
