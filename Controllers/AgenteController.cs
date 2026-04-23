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
    public class AgenteController : Controller
    {
        private readonly OCRDbContext db;

        public AgenteController(OCRDbContext context)
        {
            db = context;
        }

        public async Task<IActionResult> Index()
        {
            var list = await db.Agent
                .Include(a => a.AgentConfig)
                .OrderBy(a => a.Name)
                .ToListAsync();
            return View(list);
        }

        public async Task<IActionResult> Manage(string id)
        {
            try
            {
                ViewBag.accion = string.IsNullOrEmpty(id) ? "Create" : "Edit";
                await CargarDropdowns();

                if (!string.IsNullOrEmpty(id))
                {
                    var record = await db.Agent.FirstOrDefaultAsync(c => c.Code == id);
                    if (record == null)
                        return this.RedirectTo($"{Mensaje.Error}|{Mensaje.RecordNotFound}");
                    return View(record);
                }
                return View(new Agent { VersionNumber = 1, IsActive = true });
            }
            catch (Exception)
            {
                return this.RedirectTo($"{Mensaje.Error}|{Mensaje.ErrorLoadData}");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Manage(Agent model)
        {
            try
            {
                ViewBag.accion = string.IsNullOrEmpty(model.Code) ? "Create" : "Edit";
                if (!ModelState.IsValid)
                {
                    await CargarDropdowns();
                    TempData["Mensaje"] = $"{Mensaje.Error}|{Mensaje.FixForm}";
                    return View(model);
                }

                var code = model.Code.ToUpper().Trim();
                var current = await db.Agent.FirstOrDefaultAsync(c => c.Code.ToUpper().Trim() == code);

                if (current == null)
                {
                    model.Code = code;
                    model.CreatedDate = DateTime.UtcNow;
                    model.ModifiedDate = DateTime.UtcNow;
                    await db.Agent.AddAsync(model);
                }
                else
                {
                    current.Name = model.Name;
                    current.ConfigCode = model.ConfigCode;
                    current.VersionNumber = model.VersionNumber;
                    current.Description = model.Description;
                    current.IsActive = model.IsActive;
                    current.ModifiedDate = DateTime.UtcNow;
                    db.Agent.Update(current);
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
        public async Task<JsonResult> Delete(string id)
        {
            try
            {
                var record = await db.Agent.FindAsync(id);
                if (record == null)
                    return Json(new { Estado = Constantes.ErrorState, Mensaje = Mensaje.RecordNotFound });

                db.Agent.Remove(record);
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
            var configs = await db.OPAIConfiguration.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
            ViewBag.Configs = new SelectList(configs, "Code", "Name");
        }
    }
}
