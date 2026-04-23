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
    public class ProcesoController : Controller
    {
        private readonly OCRDbContext db;

        public ProcesoController(OCRDbContext context)
        {
            db = context;
        }

        public async Task<IActionResult> Index()
        {
            var list = await db.Process.OrderBy(x => x.Name).ToListAsync();
            return View(list);
        }

        public async Task<IActionResult> Manage(string id)
        {
            try
            {
                ViewBag.accion = string.IsNullOrEmpty(id) ? "Create" : "Edit";

                if (!string.IsNullOrEmpty(id))
                {
                    var record = await db.Process.FirstOrDefaultAsync(c => c.Code == id);
                    if (record == null)
                        return this.RedirectTo($"{Mensaje.Error}|{Mensaje.RecordNotFound}");
                    return View(record);
                }
                return View(new Process());
            }
            catch (Exception)
            {
                return this.RedirectTo($"{Mensaje.Error}|{Mensaje.ErrorLoadData}");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Manage(Process model)
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
                var current = await db.Process.FirstOrDefaultAsync(c => c.Code.ToUpper().Trim() == code);

                if (current == null)
                {
                    model.Code = code;
                    await db.Process.AddAsync(model);
                }
                else
                {
                    current.Name = model.Name;
                    current.Description = model.Description;
                    db.Process.Update(current);
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
                var record = await db.Process.FindAsync(id);
                if (record == null)
                    return Json(new { Estado = Constantes.ErrorState, Mensaje = Mensaje.RecordNotFound });

                db.Process.Remove(record);
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
