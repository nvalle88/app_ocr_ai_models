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
    public class AzureBlobConfController : Controller
    {
        private readonly OCRDbContext db;

        public AzureBlobConfController(OCRDbContext context)
        {
            db = context;
        }

        public async Task<IActionResult> Index()
        {
            var list = await db.AzureBlobConf.OrderBy(x => x.Codigo).ToListAsync();
            return View(list);
        }

        public async Task<IActionResult> Manage(string id)
        {
            try
            {
                ViewBag.accion = string.IsNullOrEmpty(id) ? "Create" : "Edit";

                if (!string.IsNullOrEmpty(id))
                {
                    var record = await db.AzureBlobConf.FirstOrDefaultAsync(c => c.Codigo == id);
                    if (record == null)
                        return this.RedirectTo($"{Mensaje.Error}|{Mensaje.RecordNotFound}");
                    return View(record);
                }
                return View(new AzureBlobConf());
            }
            catch (Exception)
            {
                return this.RedirectTo($"{Mensaje.Error}|{Mensaje.ErrorLoadData}");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Manage(AzureBlobConf model)
        {
            try
            {
                ViewBag.accion = string.IsNullOrEmpty(model.Codigo) ? "Create" : "Edit";
                if (!ModelState.IsValid)
                {
                    TempData["Mensaje"] = $"{Mensaje.Error}|{Mensaje.FixForm}";
                    return View(model);
                }

                var codigo = model.Codigo.ToUpper().Trim();
                var current = await db.AzureBlobConf.FirstOrDefaultAsync(c => c.Codigo.ToUpper().Trim() == codigo);

                if (current == null)
                {
                    model.Codigo = codigo;
                    await db.AzureBlobConf.AddAsync(model);
                }
                else
                {
                    current.ConnectionString = model.ConnectionString;
                    current.ContainerName = model.ContainerName;
                    db.AzureBlobConf.Update(current);
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
                var record = await db.AzureBlobConf.FindAsync(id);
                if (record == null)
                    return Json(new { Estado = Constantes.ErrorState, Mensaje = Mensaje.RecordNotFound });

                db.AzureBlobConf.Remove(record);
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
