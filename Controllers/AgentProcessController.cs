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
    public class AgentProcessController : Controller
    {
        private readonly OCRDbContext db;

        public AgentProcessController(OCRDbContext context)
        {
            db = context;
        }

        public async Task<IActionResult> Index()
        {
            var list = await db.AgentProcesses
                .Include(x => x.Agent)
                .Include(x => x.Process)
                .OrderBy(x => x.Agent.Name)
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
                    var record = await db.AgentProcesses.FirstOrDefaultAsync(x => x.Id == id);
                    if (record == null)
                        return this.RedirectTo($"{Mensaje.Error}|{Mensaje.RecordNotFound}");
                    return View(record);
                }
                return View(new AgentProcess());
            }
            catch (Exception)
            {
                return this.RedirectTo($"{Mensaje.Error}|{Mensaje.ErrorLoadData}");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Manage(AgentProcess model)
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
                    await db.AgentProcesses.AddAsync(model);
                }
                else
                {
                    var current = await db.AgentProcesses.FindAsync(model.Id);
                    if (current != null)
                    {
                        current.DefinitionCode = model.DefinitionCode;
                        current.AgentCode = model.AgentCode;
                        db.AgentProcesses.Update(current);
                    }
                }

                await db.SaveChangesAsync();
                return this.RedirectTo($"{Mensaje.MessaggeOK}|{Mensaje.Satisfactory}");
            }
            catch (Exception)
            {
                await CargarDropdowns();
                return this.RedirectTo($"{Mensaje.Error}|{Mensaje.Excepcion}");
            }
        }

        [HttpGet]
        public async Task<JsonResult> Delete(int id)
        {
            try
            {
                var record = await db.AgentProcesses.FindAsync(id);
                if (record == null)
                    return Json(new { Estado = Constantes.ErrorState, Mensaje = Mensaje.RecordNotFound });

                db.AgentProcesses.Remove(record);
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
            var agentes = await db.Agent.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
            var procesos = await db.Process.OrderBy(x => x.Name).ToListAsync();
            ViewBag.Agentes = new SelectList(agentes, "Code", "Name");
            ViewBag.Procesos = new SelectList(procesos, "Code", "Name");
        }
    }
}
