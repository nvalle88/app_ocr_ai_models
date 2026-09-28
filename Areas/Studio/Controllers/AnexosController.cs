using app_ocr_ai_models.Areas.Studio.Models;
using app_ocr_ai_models.Data;
using app_ocr_ai_models.Services.Anexos;
using app_tramites.Models.ModelAi;
using app_tramites.Models.ViewModel;
using Azure.Storage.Blobs;
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

    // GET /Studio/Anexos/Documento/{id}  → sirve el PDF del anexo (inline) para el visor
    [HttpGet]
    public async Task<IActionResult> Documento(int id, bool inline = true)
    {
        var anexo = await _db.Anexo.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (anexo == null || string.IsNullOrWhiteSpace(anexo.ArchivoUri))
            return NotFound("El anexo no tiene un PDF asociado.");

        var blobCfg = await _db.AzureBlobConf.AsNoTracking().FirstOrDefaultAsync();
        var bytes = await DescargarBlobAsync(anexo.ArchivoUri!, blobCfg);
        if (bytes == null)
            return NotFound("No se pudo descargar el PDF del anexo.");

        var nombre = $"anexo-{anexo.CodigoPlan}.pdf".Replace(' ', '_');
        Response.Headers["Content-Disposition"] =
            (inline ? "inline" : "attachment") + $"; filename=\"{nombre}\"";
        return File(bytes, "application/pdf");
    }

    /// <summary>
    /// Descarga el blob del anexo SOLO vía el SDK con la connection string + contenedor
    /// configurados (los anexos siempre viven en NUESTRO contenedor). No se hace un GET
    /// HTTP a la URL guardada en BD: eso sería un SSRF (fetch de una URL no validada).
    /// </summary>
    private static async Task<byte[]?> DescargarBlobAsync(string archivoUri, AzureBlobConf? blobCfg)
    {
        if (blobCfg == null || string.IsNullOrWhiteSpace(blobCfg.ConnectionString)
            || string.IsNullOrWhiteSpace(blobCfg.ContainerName))
            return null;

        try
        {
            var u = new Uri(archivoUri);
            var path = u.AbsolutePath.TrimStart('/');
            var prefix = blobCfg.ContainerName + "/";
            var blobName = path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(prefix.Length)
                : System.IO.Path.GetFileName(path);
            var client = new BlobClient(blobCfg.ConnectionString, blobCfg.ContainerName, Uri.UnescapeDataString(blobName));
            var dl = await client.DownloadContentAsync();
            return dl.Value.Content.ToArray();
        }
        catch
        {
            // Si el SDK no puede resolver el blob, se devuelve null → 404. No se
            // intenta un GET HTTP a la URL (evita SSRF hacia hosts internos).
            return null;
        }
    }
}
