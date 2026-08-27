using System.Text;
using System.Text.Json;
using app_ocr_ai_models.Areas.Studio.Models;
using app_ocr_ai_models.Data;
using app_tramites.Models.ModelAi;
using app_tramites.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace app_ocr_ai_models.Areas.Studio.Controllers;

// ============================================================
// REQ-019 — Expediente documental (document intelligence del caso):
//   el agente CLASIFICA cada documento (factura/receta/pedido/...),
//   EXTRAE entidades, VINCULA la evidencia (factura → justificantes)
//   y arma la FICHA del cliente. Se persiste como Note y se muestra
//   en la pestaña "Expediente" del workspace del caso.
// ============================================================

/// <summary>Controller del expediente documental generado por IA.</summary>
[Area("Studio")]
[Authorize]
public sealed class ExpedienteController : Controller
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly OCRDbContext _db;
    private readonly AiCompletionServiceFactory _factory;
    private readonly ILogger<ExpedienteController> _logger;

    public ExpedienteController(OCRDbContext db, AiCompletionServiceFactory factory, ILogger<ExpedienteController> logger)
    {
        _db      = db      ?? throw new ArgumentNullException(nameof(db));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger  = logger  ?? throw new ArgumentNullException(nameof(logger));
    }

    // GET /Studio/Expediente?caseCode=&embed=true — pestaña Expediente
    [HttpGet]
    public async Task<IActionResult> Index(Guid caseCode, bool embed = false)
    {
        ViewData["Embed"] = embed;
        if (caseCode == Guid.Empty)
            return View(new ExpedienteViewModel { Error = "Falta el CaseCode." });

        var nota = await _db.Note.AsNoTracking()
            .Where(n => n.CaseCode == caseCode && n.Title == OcrPromptHelper.ExpedienteNoteTitle)
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync();

        if (nota == null)
            return View(new ExpedienteViewModel { CaseCode = caseCode, Generado = false });

        return View(BuildVm(caseCode, nota.Detail, nota.CreatedAt));
    }

    // POST /Studio/Expediente/Generar — corre la clasificación IA y persiste
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generar(Guid caseCode, bool embed = false)
    {
        if (caseCode == Guid.Empty)
            return RedirectToAction(nameof(Index), new { caseCode, embed });

        var caso = await _db.ProcessCase
            .Include(pc => pc.DataFile)
            .Include(pc => pc.Notes)
            .FirstOrDefaultAsync(pc => pc.CaseCode == caseCode);

        if (caso == null)
        {
            TempData["Error"] = "Caso no encontrado.";
            return RedirectToAction(nameof(Index), new { caseCode, embed });
        }

        // Config del agente Claude (misma selección que el chat: Anthropic preferido)
        var config = await _db.OPAIConfiguration.AsNoTracking()
            .Where(c => c.IsActive && c.Provider == "Anthropic")
            .OrderBy(c => c.Code)
            .FirstOrDefaultAsync();
        if (config == null)
        {
            TempData["Error"] = "No hay una configuración IA activa (Anthropic).";
            return RedirectToAction(nameof(Index), new { caseCode, embed });
        }

        // ── Prompt de clasificación documental (sin tools: extracción pura) ──
        var caseContext = OcrPromptHelper.BuildCaseContext(caso.Notes);
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(caseContext))
            sb.AppendLine("## Contexto del sobre").AppendLine(caseContext).AppendLine();
        sb.AppendLine("## Documentos del caso (docId :: nombre :: texto OCR)");
        foreach (var f in caso.DataFile.OrderBy(f => f.CreatedDate))
        {
            sb.AppendLine($"--- docId={f.Id} :: {f.OriginalName} ---");
            sb.AppendLine(string.IsNullOrWhiteSpace(f.Text) ? "(sin texto OCR)" : f.Text);
            sb.AppendLine();
        }

        var aiRequest = new AiCompletionRequest
        {
            SystemPrompt = ExpedientePrompt,
            UserMessage  = sb.ToString(),
            MaxTokens    = 6000,
            Temperature  = 0
        };

        // Igual que la clasificacion: este paso tampoco dejaba rastro. La fila de
        // StepExecution solo se creaba cuando habia tools, y aqui no hay, asi que
        // el expediente no aparecia en ninguna medicion del pipeline.
        var exec = new StepExecution
        {
            CaseCode       = caseCode,
            StepOrder      = 0,
            DataFileId     = caso.DataFile.FirstOrDefault()?.Id,
            // El codigo del AGENTE, no el de la configuracion: ModelCode tiene
            // FK contra dbo.Agent. Este paso no tenia agente propio -de ahi que
            // nunca se pudiera registrar-; se creo en Req021e.
            ModelCode      = AgenteCode,
            RequestContent = aiRequest.UserMessage,
            Status         = "Running",
            StartDate      = DateTime.UtcNow,
            EndpointUrl    = config.EndpointUrl
        };
        _db.StepExecution.Add(exec);
        await _db.SaveChangesAsync();

        string texto;
        try
        {
            var svc = _factory.Create(config);
            var res = await svc.CompleteAsync(aiRequest, HttpContext.RequestAborted);
            texto = res.Text;

            exec.ResponseContent = texto;
            exec.Status  = "Completed";
            exec.EndDate = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await UsoDelModelo.ApuntarAsync(_db, exec.ExecutionId, res);
        }
        catch (Exception ex)
        {
            exec.Status  = "Failed";
            exec.EndDate = DateTime.UtcNow;
            exec.ResponseContent = ex.Message;
            await _db.SaveChangesAsync();

            _logger.LogWarning(ex, "Error generando expediente del caso {CaseCode}.", caseCode);
            TempData["Error"] = $"Error al generar el expediente: {ex.Message}";
            return RedirectToAction(nameof(Index), new { caseCode, embed });
        }

        var json = ExtractJson(texto);
        if (string.IsNullOrWhiteSpace(json))
        {
            TempData["Error"] = "El motor no devolvió un expediente estructurado. Reintenta.";
            return RedirectToAction(nameof(Index), new { caseCode, embed });
        }

        // Persistir (una vigente por caso)
        var previas = await _db.Note
            .Where(n => n.CaseCode == caseCode && n.Title == OcrPromptHelper.ExpedienteNoteTitle)
            .ToListAsync();
        if (previas.Count > 0) _db.Note.RemoveRange(previas);
        _db.Note.Add(new Note
        {
            CaseCode  = caseCode,
            Title     = OcrPromptHelper.ExpedienteNoteTitle,
            Detail    = json,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = $"expediente-ia|{User?.Identity?.Name}"
        });
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index), new { caseCode, embed });
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private ExpedienteViewModel BuildVm(Guid caseCode, string json, DateTime creado)
    {
        ExpedienteDto? dto = null;
        try { dto = JsonSerializer.Deserialize<ExpedienteDto>(json, JsonOpts); }
        catch (JsonException ex) { _logger.LogWarning(ex, "Expediente JSON inválido {CaseCode}.", caseCode); }

        return new ExpedienteViewModel
        {
            CaseCode   = caseCode,
            Generado   = true,
            Expediente = dto,
            GeneradoEn = creado,
            Error      = dto == null ? "No se pudo interpretar el expediente guardado; regenera." : null
        };
    }

    private static string? ExtractJson(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var start = texto.IndexOf('{');
        if (start < 0) return null;
        int depth = 0; bool inStr = false; char prev = '\0';
        for (var i = start; i < texto.Length; i++)
        {
            var c = texto[i];
            if (inStr) { if (c == '"' && prev != '\\') inStr = false; }
            else
            {
                if (c == '"') inStr = true;
                else if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return texto.Substring(start, i - start + 1); }
            }
            prev = c;
        }
        return null;
    }

    /// <summary>Prompt del clasificador documental (formato de salida estricto).</summary>
    /// <summary>El agente de este paso. Existe para poder registrarlo (Req021e).</summary>
    private const string AgenteCode = "AGENTE_EXPEDIENTE";

    private const string ExpedientePrompt = """
Eres el clasificador documental de sobres de reembolso de Saludsa. Recibes los documentos OCR de un caso.
Tu trabajo: (1) clasificar CADA documento, (2) extraer sus entidades clave, (3) vincular la evidencia
(qué documento justifica a cuál), (4) armar la ficha consolidada del cliente y (5) marcar alertas.

Devuelve ÚNICAMENTE un objeto JSON con EXACTAMENTE esta estructura (camelCase, sin texto fuera del JSON, sin fences):
{
  "fichaCliente": { "nombre": "", "cedula": "", "contrato": "", "producto": "", "prestador": "", "diagnosticos": "", "fechaAtencion": "", "totalFacturado": 0 },
  "documentos": [
    {
      "docId": 0,
      "nombre": "",
      "tipo": "FACTURA",
      "resumen": "Factura de consulta pediátrica del Dr. X por $123.00",
      "entidades": { "emisor": "", "ruc": "", "fecha": "", "valor": "", "paciente": "", "diagnostico": "", "medicamentos": "" },
      "observaciones": []
    }
  ],
  "vinculos": [
    { "dePrincipal": 0, "aJustificante": 0, "relacion": "JUSTIFICA", "detalle": "La receta respalda los medicamentos de la factura" }
  ],
  "alertas": [],
  "resumenCaso": "1 frase con qué contiene el sobre y si la evidencia está completa"
}

Reglas:
- "tipo" ∈ {FACTURA, RECETA, PEDIDO_MEDICO, RESULTADO_EXAMEN, INFORME_MEDICO, CEDULA, OTRO}.
- "docId" es el número que viene en la cabecera de cada documento (docId=N). Úsalo tal cual.
- En "entidades" incluye solo lo que realmente aparece; no inventes valores.
- "vinculos": una factura se justifica con receta/pedido/resultado del mismo episodio. Si una factura
  NO tiene justificante, agrégalo a "alertas" ("Factura X sin receta/pedido que la respalde").
- Documentos duplicados → relacion "DUPLICADO" + alerta. Inconsistencias (nombres/fechas que no cuadran) → "CONTRADICE" + alerta.
- Sé preciso y conciso; español.
""";
}
