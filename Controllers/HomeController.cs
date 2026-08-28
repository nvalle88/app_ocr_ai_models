#region Using

using System.Diagnostics;
using app_ocr_ai_models.Data;
using app_ocr_ai_models.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

#endregion

namespace app_ocr_ai_models.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly OCRDbContext _db;

        public HomeController(OCRDbContext db) => _db = db;

        /// <summary>
        /// La página principal.
        ///
        /// Antes esto era un RedirectToAction a Nexus con un return View()
        /// inalcanzable detrás, y la vista que no se usaba eran 1.644 líneas del
        /// dashboard de demo de SmartAdmin: "My Income $47.171" y gráficas de
        /// mentira. O sea, no había portada: quien entraba caía en una bandeja.
        ///
        /// Los contadores son reales y salen de la base. Un número inventado en
        /// una portada es peor que ninguno: enseña a no mirarla.
        /// </summary>
        [Authorize]
        public async Task<IActionResult> Index()
        {
            ViewBag.Casos       = await _db.ProcessCase.CountAsync();
            ViewBag.Solicitudes = await _db.SolicitudCliente.CountAsync();
            ViewBag.Tools       = await _db.OPAITool.CountAsync(x => x.IsActive);
            ViewBag.Agentes     = await _db.Agent.CountAsync(x => x.IsActive);

            // Lo único accionable de la portada: un proceso sin pasos existe y
            // no hace nada — el orquestador recorre una lista vacía y devuelve
            // texto vacío. Vale más avisarlo aquí que descubrirlo en un caso.
            var conPasos = await _db.ProcessStep.Select(x => x.ProcessCode).Distinct().ToListAsync();
            ViewBag.ProcesosSinPasos = await _db.Process.CountAsync(p => !conPasos.Contains(p.Code));

            return View();
        }
        

        [Route("dashboard-marketing")]
        public IActionResult DashboardMarketing() => View();

        [Route("dashboard-social")]
        public IActionResult SocialWall() => View();

        public IActionResult Inbox() => View();

        public IActionResult Chat() => View();

        public IActionResult Widgets() => View();
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() => View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}
