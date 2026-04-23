#region Using

using app_ocr_ai_models.Data;
using app_ocr_ai_models.Utils;
using app_tramites.Extensions;
using app_tramites.Models.ModelAi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

#endregion

namespace SmartAdmin.Web.Controllers
{
    [Authorize]
    public class PromptController : Controller
    {
        private readonly OCRDbContext db;

        public PromptController(OCRDbContext context)
        {
            db = context;
        }

        public async Task<IActionResult> Index()
        {
            var list = await db.OPAIPrompt.OrderBy(x => x.Code).ToListAsync();
            return View(list);
        }

        public async Task<IActionResult> Manage(string id)
        {
            try
            {
                ViewBag.accion = string.IsNullOrEmpty(id) ? "Create" : "Edit";

                if (!string.IsNullOrEmpty(id))
                {
                    var record = await db.OPAIPrompt.FirstOrDefaultAsync(c => c.Code == id);
                    if (record == null)
                        return this.RedirectTo($"{Mensaje.Error}|{Mensaje.RecordNotFound}");
                    return View(record);
                }
                return View(new OPAIPrompt { VersionNumber = 1 });
            }
            catch (Exception)
            {
                return this.RedirectTo($"{Mensaje.Error}|{Mensaje.ErrorLoadData}");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Manage(OPAIPrompt model)
        {
            try
            {
                ViewBag.accion = string.IsNullOrEmpty(model.Code) ? "Create" : "Edit";
                if (!ModelState.IsValid)
                {
                    TempData["Mensaje"] = $"{Mensaje.Error}|{Mensaje.FixForm}";
                    return View(model);
                }

                var code = model.Code.ToUpper().Trim();
                var current = await db.OPAIPrompt.FirstOrDefaultAsync(c => c.Code.ToUpper().Trim() == code);

                if (current == null)
                {
                    model.Code = code;
                    model.CreatedDate = DateTime.UtcNow;
                    model.ModifiedDate = DateTime.UtcNow;
                    await db.OPAIPrompt.AddAsync(model);
                }
                else
                {
                    current.Content = model.Content;
                    current.VersionNumber = model.VersionNumber;
                    current.IsActive = model.IsActive;
                    current.ModifiedDate = DateTime.UtcNow;
                    db.OPAIPrompt.Update(current);
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
        public async Task<JsonResult> Delete(string id)
        {
            try
            {
                var record = await db.OPAIPrompt.FindAsync(id);
                if (record == null)
                    return Json(new { Estado = Constantes.ErrorState, Mensaje = Mensaje.RecordNotFound });

                db.OPAIPrompt.Remove(record);
                await db.SaveChangesAsync();
                TempData["Mensaje"] = $"{Mensaje.MessaggeOK}|{Mensaje.Satisfactory}";
                return Json(new { Estado = Constantes.OKState, Mensaje = Mensaje.Satisfactory });
            }
            catch (Exception ex)
            {
                return Json(new { Estado = Constantes.ErrorState, Mensaje = ex.Message });
            }
        }
    }
}
