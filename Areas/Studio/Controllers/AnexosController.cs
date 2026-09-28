using app_ocr_ai_models.Areas.Studio.Models;
using app_ocr_ai_models.Data;
using app_ocr_ai_models.Services.Anexos;
using app_tramites.Models.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace app_ocr_ai_models.Areas.Studio.Controllers;

// ============================================================
// REQ-046 — Biblioteca de ANEXOS (administración).
//   Sube un contrato/anexo → OCR → Claude lo estructura → se guarda en la base
//   propia de Nexus. Las tools del agente auditor leen de aquí.
//   Aislado: no toca ningún flujo existente.
// ============================================================

/// <summary>Administración de la biblioteca de anexos estructurada.</summary>
[Area("Studio")]
[Authorize]
public sealed class AnexosController : Controller
{
    private readonly OCRDbContext _db;
    private readonly IAnexoIngestService _ingest;
    private readonly ILogger<AnexosController> _logger;

    public AnexosController(OCRDbContext db, IAnexoIngestService ingest, ILogger<AnexosController> logger)
    {
        _db     = db     ?? throw new ArgumentNullException(nameof(db));
        _ingest = ingest ?? throw new ArgumentNullException(nameof(ingest));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // GET /Studio/Anexos
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var contratos = await _db.AnexoContrato.AsNoTracking()
            .OrderBy(c => c.Tipo).ThenByDescending(c => c.CreatedDate).ToListAsync();

        var anexos = await _db.Anexo.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderByDescending(a => a.CreatedDate)
            .Select(a => new AnexoResumenViewModel
            {
                Id             = a.Id,
                CodigoPlan     = a.CodigoPlan,
                NombrePlan     = a.NombrePlan,
                CodigoProducto = a.CodigoProducto,
                Version        = a.Version,
                TipoContrato   = a.Contrato != null ? a.Contrato.Tipo : null,
                Estado         = a.Estado,
                Coberturas     = a.Coberturas.Count,
                Carencias      = a.Carencias.Count,
                Exclusiones    = a.Exclusiones.Count,
                CreatedDate    = a.CreatedDate
            })
            .ToListAsync();

        var ingestas = await _db.AnexoIngesta.AsNoTracking()
            .OrderByDescending(i => i.CreatedDate).Take(10).ToListAsync();

        return View(new AnexosIndexViewModel
        {
            Contratos       = contratos,
            Anexos          = anexos,
            UltimasIngestas = ingestas,
            Mensaje         = TempData["Mensaje"] as string,
            Error           = TempData["Error"] as string
        });
    }

    // POST /Studio/Anexos/Cargar
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(80_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 80_000_000)]
    public async Task<IActionResult> Cargar(IFormFile? archivo, string? tipo, string? codigoPlan)
    {
        if (archivo == null || archivo.Length == 0)
        {
            TempData["Error"] = "Debe seleccionar un archivo (PDF o imagen del contrato/anexo).";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            using var ms = new MemoryStream();
            await archivo.CopyToAsync(ms);
            var base64 = Convert.ToBase64String(ms.ToArray());
            var ext = Path.GetExtension(archivo.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".pdf";

            var file = new OcrFile
            {
                FileName  = archivo.FileName,
                Content   = base64,
                Extension = ext
            };

            var result = await _ingest.CargarYEstructurarAsync(
                file,
                string.IsNullOrWhiteSpace(tipo) ? null : tipo.Trim(),
                string.IsNullOrWhiteSpace(codigoPlan) ? null : codigoPlan.Trim(),
                User?.Identity?.Name,
                HttpContext.RequestAborted);

            if (result.Estado == "GUARDADO")
            {
                TempData["Mensaje"] =
                    $"Anexo procesado: {result.Coberturas} coberturas, {result.Carencias} carencias, " +
                    $"{result.Exclusiones} exclusiones, {result.Clausulas} cláusulas. " +
                    (result.Advertencias.Count > 0 ? "Avisos: " + string.Join("; ", result.Advertencias) : "");
            }
            else
            {
                TempData["Error"] =
                    $"No se pudo estructurar el anexo (estado {result.Estado}). " +
                    string.Join("; ", result.Advertencias);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REQ-046] Error al cargar anexo {Archivo}.", archivo.FileName);
            TempData["Error"] = $"Error al procesar el archivo: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    // GET /Studio/Anexos/Detalle/{id}
    [HttpGet]
    public async Task<IActionResult> Detalle(int id)
    {
        var anexo = await _db.Anexo.AsNoTracking()
            .Include(a => a.Contrato)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (anexo == null)
        {
            TempData["Error"] = "Anexo no encontrado.";
            return RedirectToAction(nameof(Index));
        }

        var vm = new AnexoDetalleViewModel
        {
            Anexo       = anexo,
            Contrato    = anexo.Contrato,
            Coberturas  = await _db.AnexoCobertura.AsNoTracking().Where(c => c.AnexoId == id).OrderBy(c => c.Beneficio).ToListAsync(),
            Carencias   = await _db.AnexoCarencia.AsNoTracking().Where(c => c.AnexoId == id).OrderBy(c => c.Beneficio).ToListAsync(),
            Exclusiones = await _db.AnexoExclusion.AsNoTracking().Where(c => c.AnexoId == id).OrderBy(c => c.Id).ToListAsync(),
            Clausulas   = anexo.ContratoId != null
                ? await _db.AnexoClausula.AsNoTracking().Where(c => c.ContratoId == anexo.ContratoId).OrderBy(c => c.Id).ToListAsync()
                : new List<app_tramites.Models.ModelAi.AnexoClausula>()
        };
        return View(vm);
    }

    // POST /Studio/Anexos/Eliminar/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Eliminar(int id)
    {
        var anexo = await _db.Anexo.FindAsync(id);
        if (anexo != null)
        {
            // Los hijos (coberturas/carencias/exclusiones) caen por cascade.
            _db.Anexo.Remove(anexo);
            await _db.SaveChangesAsync();
            TempData["Mensaje"] = $"Anexo {anexo.CodigoPlan} eliminado.";
        }
        return RedirectToAction(nameof(Index));
    }
}
