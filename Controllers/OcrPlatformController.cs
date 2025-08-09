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
    public class OcrPlatformController : Controller
    {
        private readonly OCRDbContext db;

        public OcrPlatformController(OCRDbContext context)
        {
            db = context;
        }
        // GET: /forum/general-view
        public async Task<IActionResult> Index()
        {
            var list = await db.OCRPlatform.ToListAsync();
            return View(list);
        }


        public async Task<IActionResult> Manage(string id)
        {
            try
            {
                ViewBag.accion = string.IsNullOrEmpty(id) == true ? "Create" : "Edit";

                if (!string.IsNullOrEmpty(id))
                {
                    var record = await db.OCRPlatform.FirstOrDefaultAsync(c => c.PlatformCode == id);
                    if (record == null)
                        return this.RedirectTo($"{Mensaje.Error}|{Mensaje.RecordNotFound}");

                    return View(record);
                }
                return View(new OCRPlatform());
            }
            catch (Exception)
            {
                return this.RedirectTo($"{Mensaje.Error}|{Mensaje.ErrorLoadData}");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Manage(OCRPlatform platform)
        {
            try
            {
                ViewBag.accion = string.IsNullOrEmpty(platform.PlatformCode) == true ? "Create" : "Edit";
                if (!ModelState.IsValid)
                {
                    TempData["Mensaje"] = $"{Mensaje.Error}|{Mensaje.FixForm}";
                    return View(platform);
                }
                var code = platform.PlatformCode.ToUpper().Trim();
                var currentPlatform = await db.OCRPlatform.Where(c => c.PlatformCode.ToUpper().Trim() == code).FirstOrDefaultAsync();

                if (currentPlatform == null)
                {
                    platform.CreatedAt = DateTime.Now;
                    await db.OCRPlatform.AddAsync(platform);
                }
                else
                {
                    currentPlatform.Name = platform.Name;
                    currentPlatform.PricePerPage = platform.PricePerPage;
                    currentPlatform.Description = platform.Description;
                    currentPlatform.IsActive = platform.IsActive;
                    currentPlatform.MaxFileSizeMB = platform.MaxFileSizeMB;
                    currentPlatform.LanguageSupport = platform.LanguageSupport;
                    db.OCRPlatform.Update(currentPlatform);

                }
                var d= await db.SaveChangesAsync();
                return this.RedirectTo($"{Mensaje.MessaggeOK}|{Mensaje.Satisfactory}");

            }
            catch (Exception ex)
            {
                return this.RedirectTo($"{Mensaje.Error}|{Mensaje.Excepcion}");
            }
        }


        [HttpGet]
        public async Task<JsonResult> Delete(string id)
        {
            try
            {
                var record = await db.OCRPlatform.FindAsync(id);
                if (record == null)
                {
                    return Json(new
                    {
                        Estado = Constantes.ErrorState,
                        Mensaje = Mensaje.RecordNotFound
                    });
                }

                db.OCRPlatform.Remove(record);
                await db.SaveChangesAsync();
                this.TempData["Mensaje"] = $"{Mensaje.MessaggeOK}|  {Mensaje.Satisfactory}";
                return Json(new
                {
                    Estado = Constantes.OKState,
                    Mensaje = Mensaje.Satisfactory
                });

            }
            catch (Exception ex)
            {
                return Json(new
                {
                    Estado = Constantes.ErrorState,
                    Mensaje = ex.Message
                });
            }
        }
    }
}
