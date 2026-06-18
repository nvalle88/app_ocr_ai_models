using app_ocr_ai_models.Areas.Studio.Models;
using app_tramites.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace app_ocr_ai_models.Areas.Studio.Controllers;

// ============================================================
// REQ-019 T6 — Área Studio: disparar análisis IA de un Caso.
// Aislado: no toca NexusController/OcrTestController/HomeController.
// ============================================================

/// <summary>
/// Controller del Área Studio para disparar el análisis IA
/// de un <see cref="app_tramites.Models.ModelAi.ProcessCase"/> ya importado.
/// Ejecuta el orquestador+runner sobre el caso y muestra el resultado.
/// </summary>
[Area("Studio")]
[Authorize]
public sealed class AnalizarController : Controller
{
    private readonly IProcessOrchestrator _orchestrator;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ILogger<AnalizarController> _logger;

    /// <summary>
    /// Crea el controller con sus dependencias.
    /// </summary>
    /// <param name="orchestrator">Orquestador del motor IA multi-paso.</param>
    /// <param name="userManager">Gestor de identidad de ASP.NET Core.</param>
    /// <param name="logger">Logger.</param>
    public AnalizarController(
        IProcessOrchestrator orchestrator,
        UserManager<IdentityUser> userManager,
        ILogger<AnalizarController> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _userManager  = userManager  ?? throw new ArgumentNullException(nameof(userManager));
        _logger       = logger       ?? throw new ArgumentNullException(nameof(logger));
    }

    // ----------------------------------------------------------------
    // GET /Studio/Analizar/Index?caseCode=<guid>
    // Muestra el formulario de disparo de análisis
    // ----------------------------------------------------------------

    /// <summary>
    /// Muestra el formulario para disparar el análisis IA de un caso.
    /// </summary>
    /// <param name="caseCode">CaseCode del caso a analizar (opcional desde query string).</param>
    [HttpGet]
    public IActionResult Index(Guid? caseCode)
    {
        var vm = new AnalizarResultadoViewModel
        {
            Request = new AnalizarCasoRequest { CaseCode = caseCode ?? Guid.Empty }
        };
        return View(vm);
    }

    // ----------------------------------------------------------------
    // POST /Studio/Analizar/Ejecutar
    // Dispara el orquestador y muestra el resultado
    // ----------------------------------------------------------------

    /// <summary>
    /// Ejecuta el orquestador IA sobre el caso indicado y presenta los resultados
    /// de cada <see cref="app_tramites.Models.ModelAi.StepExecution"/> generado.
    /// </summary>
    /// <param name="request">CaseCode y proceso opcional de override.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ejecutar(AnalizarCasoRequest request)
    {
        if (request.CaseCode == Guid.Empty)
        {
            return View("Index", new AnalizarResultadoViewModel
            {
                Request = request,
                Error   = "Debe especificar un CaseCode válido."
            });
        }

        var user = await _userManager.GetUserAsync(User);

        try
        {
            var resultado = await _orchestrator.RunAsync(
                request.CaseCode,
                user,
                string.IsNullOrWhiteSpace(request.ProcessCodeOverride)
                    ? null
                    : request.ProcessCodeOverride.Trim());

            return View("Index", new AnalizarResultadoViewModel
            {
                Request   = request,
                Resultado = resultado
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al ejecutar análisis del caso {CaseCode}.", request.CaseCode);
            return View("Index", new AnalizarResultadoViewModel
            {
                Request = request,
                Error   = $"Error durante el análisis: {ex.Message}"
            });
        }
    }
}
