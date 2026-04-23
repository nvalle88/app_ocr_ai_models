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
    public class AsignacionPromptController : Controller
    {
        private readonly OCRDbContext db;

        public AsignacionPromptController(OCRDbContext context)
        {
            db = context;
        }

        public async Task<IActionResult> Index()
        {
            var list = await db.OPAIModelPrompt
                .Include(x => x.ModelCodeNavigation)
                .Include(x => x.PromptCodeNavigation)
                .OrderBy(x => x.ModelCode).ThenBy(x => x.Order)
                .ToListAsync();
            return View(list);
        }

        public async Task<IActionResult> Manage(string modelCode, string promptCode)
        {
            try
            {
                bool esEdicion = !string.IsNullOrEmpty(modelCode) && !string.IsNullOrEmpty(promptCode);
                ViewBag.accion = esEdicion ? "Edit" : "Create";
                await CargarDropdowns();

                if (esEdicion)
                {
                    var record = await db.OPAIModelPrompt
                        .FirstOrDefaultAsync(x => x.ModelCode == modelCode && x.PromptCode == promptCode);
                    if (record == null)
                        return this.RedirectTo($"{Mensaje.Error}|{Mensaje.RecordNotFound}");
                    return View(record);
                }
                return View(new OPAIModelPrompt { Order = 1 });
            }
            catch (Exception)
            {
                return this.RedirectTo($"{Mensaje.Error}|{Mensaje.ErrorLoadData}");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Manage(OPAIModelPrompt model, string originalModelCode, string originalPromptCode)
        {
            try
            {
                bool esEdicion = !string.IsNullOrEmpty(originalModelCode) && !string.IsNullOrEmpty(originalPromptCode);
                ViewBag.accion = esEdicion ? "Edit" : "Create";

                if (!ModelState.IsValid)
                {
                    await CargarDropdowns();
                    TempData["Mensaje"] = $"{Mensaje.Error}|{Mensaje.FixForm}";
                    return View(model);
                }

                if (esEdicion)
                {
                    var current = await db.OPAIModelPrompt
                        .FirstOrDefaultAsync(x => x.ModelCode == originalModelCode && x.PromptCode == originalPromptCode);
                    if (current != null)
                    {
                        current.Order = model.Order;
                        db.OPAIModelPrompt.Update(current);
                    }
                }
                else
                {
                    var existe = await db.OPAIModelPrompt
                        .AnyAsync(x => x.ModelCode == model.ModelCode && x.PromptCode == model.PromptCode);
                    if (existe)
                    {
                        await CargarDropdowns();
                        ModelState.AddModelError("", "Ya existe esta asignación de agente y prompt.");
                        return View(model);
                    }
                    await db.OPAIModelPrompt.AddAsync(model);
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
        public async Task<JsonResult> Delete(string modelCode, string promptCode)
        {
            try
            {
                var record = await db.OPAIModelPrompt
                    .FirstOrDefaultAsync(x => x.ModelCode == modelCode && x.PromptCode == promptCode);
                if (record == null)
                    return Json(new { Estado = Constantes.ErrorState, Mensaje = Mensaje.RecordNotFound });

                db.OPAIModelPrompt.Remove(record);
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
            var prompts = await db.OPAIPrompt.Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync();
            ViewBag.Agentes = new SelectList(agentes, "Code", "Name");
            ViewBag.Prompts = new SelectList(prompts, "Code", "Code");
        }
    }
}
