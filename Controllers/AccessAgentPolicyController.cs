#region Using

using app_ocr_ai_models.Data;
using app_ocr_ai_models.Utils;
using app_tramites.Extensions;
using app_tramites.Models.ModelAi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

#endregion

namespace SmartAdmin.Web.Controllers
{
    [Authorize]
    public class AccessAgentPolicyController : Controller
    {
        private readonly OCRDbContext db;

        public AccessAgentPolicyController(OCRDbContext context)
        {
            db = context;
        }

        public async Task<IActionResult> Index()
        {
            var list = await db.AccessAgentPolicies
                .Include(x => x.Policy)
                .Include(x => x.AgentProcess)
                    .ThenInclude(ap => ap.Agent)
                .Include(x => x.AgentProcess)
                    .ThenInclude(ap => ap.Process)
                .OrderBy(x => x.PolicyCode)
                .ToListAsync();
            return View(list);
        }

        public async Task<IActionResult> Manage(int? id)
        {
            try
            {
                ViewBag.accion = id == null ? "Create" : "Edit";
                await CargarDropdowns();

                if (id != null)
                {
                    var record = await db.AccessAgentPolicies.FirstOrDefaultAsync(x => x.Id == id);
                    if (record == null)
                        return this.RedirectTo($"{Mensaje.Error}|{Mensaje.RecordNotFound}");
                    return View(record);
                }
                return View(new AccessAgentPolicy { Status = true });
            }
            catch (Exception)
            {
                return this.RedirectTo($"{Mensaje.Error}|{Mensaje.ErrorLoadData}");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Manage(AccessAgentPolicy model)
        {
            try
            {
                ViewBag.accion = model.Id == 0 ? "Create" : "Edit";
                if (!ModelState.IsValid)
                {
                    await CargarDropdowns();
                    TempData["Mensaje"] = $"{Mensaje.Error}|{Mensaje.FixForm}";
                    return View(model);
                }

                if (model.Id == 0)
                {
                    await db.AccessAgentPolicies.AddAsync(model);
                }
                else
                {
                    var current = await db.AccessAgentPolicies.FindAsync(model.Id);
                    if (current != null)
                    {
                        current.PolicyCode = model.PolicyCode;
                        current.AgentProcessId = model.AgentProcessId;
                        current.Status = model.Status;
                        db.AccessAgentPolicies.Update(current);
                    }
                }

                await db.SaveChangesAsync();
                return this.RedirectTo($"{Mensaje.MessaggeOK}|{Mensaje.Satisfactory}");
            }
            catch (Exception)
            {
                return this.RedirectTo($"{Mensaje.Error}|{Mensaje.Excepcion}");
            }
        }

        [HttpGet]
        public async Task<JsonResult> Delete(int id)
        {
            try
            {
                var record = await db.AccessAgentPolicies.FindAsync(id);
                if (record == null)
                    return Json(new { Estado = Constantes.ErrorState, Mensaje = Mensaje.RecordNotFound });

                db.AccessAgentPolicies.Remove(record);
                await db.SaveChangesAsync();
                TempData["Mensaje"] = $"{Mensaje.MessaggeOK}|{Mensaje.Satisfactory}";
                return Json(new { Estado = Constantes.OKState, Mensaje = Mensaje.Satisfactory });
            }
            catch (Exception ex)
            {
                return Json(new { Estado = Constantes.ErrorState, Mensaje = ex.Message });
            }
        }

        private async Task CargarDropdowns()
        {
            var politicas = await db.Policies.OrderBy(x => x.PolicyName).ToListAsync();
            ViewBag.Politicas = new SelectList(politicas, "Code", "PolicyName");

            var agentProcesses = await db.AgentProcesses
                .Include(x => x.Agent)
                .Include(x => x.Process)
                .ToListAsync();
            ViewBag.AgentProcesses = new SelectList(
                agentProcesses.Select(ap => new {
                    ap.Id,
                    Nombre = $"{ap.Agent?.Name} → {ap.Process?.Name}"
                }),
                "Id", "Nombre"
            );
        }
    }
}
