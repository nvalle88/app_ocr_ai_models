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
    public class ConfiguracionAiController : Controller
    {
        private readonly OCRDbContext db;

        public ConfiguracionAiController(OCRDbContext context)
        {
            db = context;
        }

        public async Task<IActionResult> Index()
        {
            var list = await db.OPAIConfiguration.OrderBy(x => x.Name).ToListAsync();
            return View(list);
        }

        public async Task<IActionResult> Manage(string id)
        {
            try
            {
                ViewBag.accion = string.IsNullOrEmpty(id) ? "Create" : "Edit";

                if (!string.IsNullOrEmpty(id))
                {
                    var record = await db.OPAIConfiguration.FirstOrDefaultAsync(c => c.Code == id);
                    if (record == null)
                        return this.RedirectTo($"{Mensaje.Error}|{Mensaje.RecordNotFound}");
                    return View(record);
                }
                return View(new OPAIConfiguration());
            }
            catch (Exception)
            {
                return this.RedirectTo($"{Mensaje.Error}|{Mensaje.ErrorLoadData}");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Manage(OPAIConfiguration model)
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
                var current = await db.OPAIConfiguration.FirstOrDefaultAsync(c => c.Code.ToUpper().Trim() == code);

                if (current == null)
                {
                    model.Code = code;
                    model.CreatedDate = DateTime.UtcNow;
                    model.ModifiedDate = DateTime.UtcNow;
                    await db.OPAIConfiguration.AddAsync(model);
                }
                else
                {
                    current.Name = model.Name;
                    current.EndpointUrl = model.EndpointUrl;
                    current.ApiKey = model.ApiKey;
                    current.ConfigType = model.ConfigType;
                    current.Notes = model.Notes;
                    current.IsActive = model.IsActive;
                    current.ModifiedDate = DateTime.UtcNow;
                    db.OPAIConfiguration.Update(current);
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
                var record = await db.OPAIConfiguration.FindAsync(id);
                if (record == null)
                    return Json(new { Estado = Constantes.ErrorState, Mensaje = Mensaje.RecordNotFound });

                db.OPAIConfiguration.Remove(record);
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
