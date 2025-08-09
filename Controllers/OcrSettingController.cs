#region Using

using app_ocr_ai_models.Utils;
using app_tramites.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using app_ocr_ai_models.Data;
using app_tramites.Models.ModelAi;

#endregion

namespace SmartAdmin.Web.Controllers
{
    [Authorize]
    public class OcrSettingController : Controller
    {
        private readonly OCRDbContext db;

        public OcrSettingController(OCRDbContext context)
        {
            db = context;
        }
        // GET: /forum/general-view
        public async Task<IActionResult> Index()
        {
            var list = await db.OCRSetting.Include(x=>x.PlatformCodeNavigation).ToListAsync();
            return View(list);
        }


        public async Task<IActionResult> Manage([FromQuery]string SettingCode, string PlatformCode)
        {
            try
            {
                var parameters = string.IsNullOrEmpty(SettingCode) && string.IsNullOrEmpty(PlatformCode);
                ViewBag.accion = parameters == true ? "Create" : "Edit";
                ViewBag.Platforms = new SelectList( await db.OCRPlatform.OrderBy(a => a.PlatformCode).Select(a =>
                new SelectListItem
                {
                    Value = a.PlatformCode,
                    Text = $"({a.PlatformCode}){a.Name}"
                }).ToListAsync(), "Value", "Text", null);

                if (!parameters)
                {
                    var record = await db.OCRSetting.FirstOrDefaultAsync(c => c.PlatformCode == PlatformCode && c.SettingCode==SettingCode);
                    if (record == null)
                        return this.RedirectTo($"{Mensaje.Error}|{Mensaje.RecordNotFound}");

                    return View(record);
                }
                return View(new OCRSetting());
            }
            catch (Exception)
            {
                return this.RedirectTo($"{Mensaje.Error}|{Mensaje.ErrorLoadData}");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Manage(OCRSetting setting)
        {
            try
            {
                ViewBag.accion = string.IsNullOrEmpty(setting.PlatformCode) == true ? "Create" : "Edit";
                ModelState.Remove(nameof(setting.PlatformCodeNavigation));
                if (!ModelState.IsValid)
                {
                    TempData["Mensaje"] = $"{Mensaje.Error}|{Mensaje.FixForm}";
                    return View(setting);
                }
                var platformCode = setting.PlatformCode.ToUpper().Trim();
                var settingCode = setting.SettingCode.ToUpper().Trim();
                var currentSetting = await db.OCRSetting.Where(c => c.PlatformCode.ToUpper().Trim() == platformCode 
                && c.SettingCode.ToUpper().Trim()==settingCode).FirstOrDefaultAsync();

                if (currentSetting == null)
                {
                    setting.CreatedAt = DateTime.Now;
                    await db.OCRSetting.AddAsync(setting);
                }
                else
                {
                    currentSetting.Name = setting.Name;
                    currentSetting.Endpoint = setting.Endpoint;
                    currentSetting.ApiKey = setting.ApiKey;
                    currentSetting.ModelId = setting.ModelId;
                    currentSetting.IsActive = setting.IsActive;
                    currentSetting.SettingCode = setting.SettingCode;
                    db.OCRSetting.Update(currentSetting);

                }
                await db.SaveChangesAsync();
                return this.RedirectTo($"{Mensaje.MessaggeOK}|{Mensaje.Satisfactory}");

            }
            catch (Exception ex)
            {
                return this.RedirectTo($"{Mensaje.Error}|{Mensaje.Excepcion}");
            }
        }


        [HttpGet]
        public async Task<JsonResult> Delete([FromQuery]string platformCode, string settingCode)
        {
            try
            {
                var record = await db.OCRSetting.Where(x=>x.PlatformCode.Equals(platformCode) && x.SettingCode.Equals(settingCode)).FirstAsync();
                if (record == null)
                {
                    return Json(new
                    {
                        Estado = Constantes.ErrorState,
                        Mensaje = Mensaje.RecordNotFound
                    });
                }

                db.OCRSetting.Remove(record);
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
