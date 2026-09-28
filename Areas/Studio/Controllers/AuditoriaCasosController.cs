using System.Text.Json;
using app_ocr_ai_models.Areas.Studio.Models;
using app_ocr_ai_models.Data;
using app_ocr_ai_models.Services.Documents;
using app_tramites.Models.ModelAi;
using app_tramites.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace app_ocr_ai_models.Areas.Studio.Controllers;

// ============================================================
// REQ-046 — Pantalla "Auditoría Casos" (rol Auditor Saludsa).
//   Entrada dedicada del auditor: busca el sobre en Armonix, lo importa como
//   caso de AUDITORIA_MEDICINA (documentos desde M-Files + contexto del sobre)
//   y lo lleva al workspace del caso para generar la auditoría con IA.
//   Reusa: ArmonixDocumentProvider (búsqueda + documentos), el proceso
//   AUDITORIA_MEDICINA y el agente AGENTE_AUDITOR_MEDICINA (con las tools de
//   la biblioteca de anexos ya enlazadas).
// ============================================================

/// <summary>Auditoría de casos de reembolso asistida por IA, desde la búsqueda en Armonix.</summary>
[Area("Studio")]
[Authorize]
public sealed class AuditoriaCasosController : Controller
{
    private const string ProcesoAuditoria = "AUDITORIA_MEDICINA";

    private readonly OCRDbContext _db;
    private readonly ArmonixDocumentProvider _armonix;
    private readonly ILogger<AuditoriaCasosController> _logger;

    public AuditoriaCasosController(
        OCRDbContext db,
        ArmonixDocumentProvider armonix,
        ILogger<AuditoriaCasosController> logger)
    {
        _db      = db      ?? throw new ArgumentNullException(nameof(db));
        _armonix = armonix ?? throw new ArgumentNullException(nameof(armonix));
        _logger  = logger  ?? throw new ArgumentNullException(nameof(logger));
    }

    // GET /Studio/AuditoriaCasos
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var vm = new AuditoriaCasosViewModel
        {
            Recientes = await CargarRecientesAsync()
        };
        return View(vm);
    }

    // POST /Studio/AuditoriaCasos/Buscar
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Buscar(string? criterio)
    {
        var vm = new AuditoriaCasosViewModel
        {
            Criterio  = criterio,
            Buscado   = true,
            Recientes = await CargarRecientesAsync()
        };

        if (string.IsNullOrWhiteSpace(criterio))
        {
            vm.Error = "Escribe el número de sobre o la cédula del afiliado.";
            return View(nameof(Index), vm);
        }

        // El criterio puede ser número de sobre, cédula o nombre del titular.
        var texto = criterio.Trim();
        string? sobre = null, cedula = null, nombre = null;
        if (texto.All(char.IsDigit))
            cedula = texto;                                   // solo dígitos → cédula
        else if (texto.Contains('-') || texto.Any(char.IsDigit))
            sobre = texto;                                    // NA-2612551 / alfanumérico → sobre
        else
            nombre = texto;                                   // solo letras → nombre del titular

        try
        {
            vm.Resultados = await _armonix.BuscarSobresAsync(
                sobre, cedula, nombre, null, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REQ-046] Error al buscar sobre '{Criterio}' en Armonix.", criterio);
            vm.Error = $"Error al consultar Armonix: {ex.Message}";
        }

        return View(nameof(Index), vm);
    }

    // POST /Studio/AuditoriaCasos/Auditar
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Auditar(
        string numeroSobre, string? numeroContrato, string? codigoProducto,
        string? codigoRegion, string? nombreTitular)
    {
        if (string.IsNullOrWhiteSpace(numeroSobre))
        {
            TempData["Error"] = "Falta el número de sobre.";
            return RedirectToAction(nameof(Index));
        }

        // ── 1. Proceso de auditoría
        var proceso = await _db.Process.FindAsync(ProcesoAuditoria);
        if (proceso == null)
        {
            TempData["Error"] = $"No existe el proceso '{ProcesoAuditoria}'.";
            return RedirectToAction(nameof(Index));
        }

        // ── 2. Caso nuevo
        var caso = new ProcessCase
        {
            CaseCode       = Guid.NewGuid(),
            DefinitionCode = ProcesoAuditoria,
            StartDate      = DateTime.UtcNow,
            State          = "Started"
        };
        _db.ProcessCase.Add(caso);
        await _db.SaveChangesAsync();

        // ── 3. Contexto del sobre (Note "ContextoSobre") para el agente y las tools
        var contexto = new Dictionary<string, string?>
        {
            ["numeroSobre"]   = numeroSobre.Trim(),
            ["numeroContrato"] = numeroContrato,
            ["producto"]      = codigoProducto,
            ["codigoRegion"]  = codigoRegion,
            ["nombreTitular"] = nombreTitular,
            ["origen"]        = "Armonix"
        };
        _db.Note.Add(new Note
        {
            CaseCode  = caso.CaseCode,
            Title     = OcrPromptHelper.ContextoSobreNoteTitle,
            Detail    = JsonSerializer.Serialize(contexto),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = $"auditoria-casos|{User?.Identity?.Name}"
        });
        await _db.SaveChangesAsync();

        // ── 4. Importar documentos del sobre desde M-Files (OCR → DataFile)
        try
        {
            var filter = new SobreDocumentosFilter
            {
                NumeroSobre    = numeroSobre.Trim(),
                NumeroContrato = numeroContrato,
                CodigoProducto = codigoProducto,
                CodigoRegion   = codigoRegion
            };
            var res = await _armonix.ImportarDocumentosAsync(filter, caso, _db, HttpContext.RequestAborted);
            if (res.DataFileIds.Count == 0)
            {
                caso.State = res.Advertencias.Count > 0 ? "ImportedWithWarnings" : "ImportedEmpty";
                await _db.SaveChangesAsync();
                if (res.Advertencias.Count > 0)
                    TempData["Error"] = "El sobre se importó pero: " + string.Join("; ", res.Advertencias);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REQ-046] Error al importar documentos del sobre {NumeroSobre}.", numeroSobre);
            TempData["Error"] = $"No se pudieron traer los documentos del sobre: {ex.Message}";
        }

        // ── 5. Al workspace del caso (documentos + pestaña Auditoría)
        return RedirectToAction("Caso", "Sobres", new { area = "Studio", caseCode = caso.CaseCode });
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private async Task<List<CasoAuditoriaReciente>> CargarRecientesAsync()
    {
        var casos = await _db.ProcessCase.AsNoTracking()
            .Where(c => c.DefinitionCode == ProcesoAuditoria)
            .OrderByDescending(c => c.StartDate)
            .Take(15)
            .Select(c => new
            {
                c.CaseCode,
                c.State,
                c.StartDate,
                Docs = c.DataFile.Count,
                Contexto = c.Notes.Where(n => n.Title == OcrPromptHelper.ContextoSobreNoteTitle)
                                   .Select(n => n.Detail).FirstOrDefault(),
                TieneAud = c.Notes.Any(n => n.Title == OcrPromptHelper.AuditoriaMedicinaNoteTitle)
            })
            .ToListAsync();

        var lista = new List<CasoAuditoriaReciente>();
        foreach (var c in casos)
        {
            string? sobre = null, titular = null;
            if (!string.IsNullOrWhiteSpace(c.Contexto))
            {
                try
                {
                    using var doc = JsonDocument.Parse(c.Contexto);
                    if (doc.RootElement.TryGetProperty("numeroSobre", out var s)) sobre = s.GetString();
                    if (doc.RootElement.TryGetProperty("nombreTitular", out var t)) titular = t.GetString();
                }
                catch (JsonException) { }
            }
            lista.Add(new CasoAuditoriaReciente
            {
                CaseCode       = c.CaseCode,
                Estado         = c.State ?? "—",
                StartDate      = c.StartDate,
                Documentos     = c.Docs,
                NumeroSobre    = sobre,
                NombreTitular  = titular,
                TieneAuditoria = c.TieneAud
            });
        }
        return lista;
    }
}
