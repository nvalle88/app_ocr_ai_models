using System.Text;
using System.Text.Json;
using app_ocr_ai_models.Areas.Studio.Models;
using app_ocr_ai_models.Data;
using app_ocr_ai_models.Services.Documents;
using app_tramites.Models.ModelAi;
using app_tramites.Services.Ai;
using app_tramites.Services.Ai.Tools;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace app_ocr_ai_models.Areas.Studio.Controllers;

// ============================================================
// REQ-046 — Pantalla "Auditoría Casos" (rol Auditor Saludsa), INDEPENDIENTE
//   del workspace de la bandeja (Sobres). Flujo propio:
//     buscar sobre en Armonix → importar (M-Files) → CASO PROPIO →
//     generar el DICTAMEN con AGENTE_AUDITOR_CASOS (6 secciones) y mostrarlo
//     aquí mismo (no redirige a la bandeja).
//   Reusa: ArmonixDocumentProvider, motor Claude, tools de la biblioteca de anexos.
// ============================================================

/// <summary>Auditoría de casos de reembolso asistida por IA, con dictamen propio de 6 secciones.</summary>
[Area("Studio")]
[Authorize]
public sealed class AuditoriaCasosController : Controller
{
    private const string ProcesoAuditoria = "AUDITORIA_MEDICINA";
    private const string AgenteAuditorCasos = "AGENTE_AUDITOR_CASOS";
    private const string NoteAuditoriaCasos = "AuditoriaCasos";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly OCRDbContext _db;
    private readonly ArmonixDocumentProvider _armonix;
    private readonly AiCompletionServiceFactory _factory;
    private readonly IToolExecutor? _toolExecutor;
    private readonly ILogger<AuditoriaCasosController> _logger;

    public AuditoriaCasosController(
        OCRDbContext db,
        ArmonixDocumentProvider armonix,
        AiCompletionServiceFactory factory,
        ILogger<AuditoriaCasosController> logger,
        IToolExecutor? toolExecutor = null)
    {
        _db           = db      ?? throw new ArgumentNullException(nameof(db));
        _armonix      = armonix ?? throw new ArgumentNullException(nameof(armonix));
        _factory      = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger       = logger  ?? throw new ArgumentNullException(nameof(logger));
        _toolExecutor = toolExecutor;
    }

    // GET /Studio/AuditoriaCasos
    [HttpGet]
    public async Task<IActionResult> Index()
        => View(new AuditoriaCasosViewModel { Recientes = await CargarRecientesAsync() });

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

        var texto = criterio.Trim();
        string? sobre = null, cedula = null, nombre = null;
        if (texto.All(char.IsDigit))                                cedula = texto;
        else if (texto.Contains('-') || texto.Any(char.IsDigit))    sobre  = texto;
        else                                                        nombre = texto;

        try
        {
            vm.Resultados = await _armonix.BuscarSobresAsync(sobre, cedula, nombre, null, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REQ-046] Error al buscar sobre '{Criterio}' en Armonix.", criterio);
            vm.Error = $"Error al consultar Armonix: {ex.Message}";
        }

        return View(nameof(Index), vm);
    }

    // POST /Studio/AuditoriaCasos/Auditar  → crea el caso propio y va a su pantalla
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

        var proceso = await _db.Process.FindAsync(ProcesoAuditoria);
        if (proceso == null)
        {
            TempData["Error"] = $"No existe el proceso '{ProcesoAuditoria}'.";
            return RedirectToAction(nameof(Index));
        }

        var caso = new ProcessCase
        {
            CaseCode       = Guid.NewGuid(),
            DefinitionCode = ProcesoAuditoria,
            StartDate      = DateTime.UtcNow,
            State          = "Started"
        };
        _db.ProcessCase.Add(caso);
        await _db.SaveChangesAsync();

        // Contexto del sobre (Note "ContextoSobre") para el agente y las tools
        var contexto = new Dictionary<string, string?>
        {
            ["numeroSobre"]    = numeroSobre.Trim(),
            ["numeroContrato"] = numeroContrato,
            ["producto"]       = codigoProducto,
            ["codigoRegion"]   = codigoRegion,
            ["nombreTitular"]  = nombreTitular,
            ["origen"]         = "Armonix"
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

        // Importar documentos del sobre desde M-Files (OCR → DataFile)
        try
        {
            var filter = new SobreDocumentosFilter
            {
                NumeroSobre    = numeroSobre.Trim(),
                NumeroContrato = numeroContrato,
                CodigoProducto = codigoProducto,
                CodigoRegion   = codigoRegion
            };
            // RÁPIDO: solo trae los PDF a Blob (sin OCR). El OCR se hace al generar
            // el dictamen. Así el auditor ve los documentos en el visor de inmediato.
            var res = await _armonix.ImportarDocumentosSinOcrAsync(filter, caso, _db, HttpContext.RequestAborted);
            if (res.DataFileIds.Count == 0 && res.Advertencias.Count > 0)
                TempData["Error"] = "El sobre se importó pero: " + string.Join("; ", res.Advertencias);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REQ-046] Error al importar documentos del sobre {NumeroSobre}.", numeroSobre);
            TempData["Error"] = $"No se pudieron traer los documentos del sobre: {ex.Message}";
        }

        // A la pantalla PROPIA del caso (no a la bandeja)
        return RedirectToAction(nameof(Caso), new { caseCode = caso.CaseCode });
    }

    // GET /Studio/AuditoriaCasos/Caso/{caseCode}
    [HttpGet]
    public async Task<IActionResult> Caso(Guid caseCode)
    {
        var caso = await _db.ProcessCase
            .Include(c => c.DataFile)
            .Include(c => c.Notes)
            .FirstOrDefaultAsync(c => c.CaseCode == caseCode);
        if (caso == null)
        {
            TempData["Error"] = "Caso no encontrado.";
            return RedirectToAction(nameof(Index));
        }

        var vm = BuildCasoVm(caso);
        vm.Anexos = await CargarAnexosLateralAsync(vm.Producto);
        return View(vm);
    }

    // Anexos de la biblioteca para el panel lateral; los que coinciden con el
    // producto del sobre se muestran primero (resaltados).
    private async Task<List<AnexoLateralVM>> CargarAnexosLateralAsync(string? producto)
    {
        var anexos = await _db.Anexo.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderByDescending(a => a.CreatedDate)
            .Take(50)
            .Select(a => new AnexoLateralVM
            {
                Id             = a.Id,
                CodigoPlan     = a.CodigoPlan,
                NombrePlan     = a.NombrePlan,
                CodigoProducto = a.CodigoProducto,
                Coberturas     = a.Coberturas.Count
            })
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(producto))
            foreach (var a in anexos)
                a.Coincide = string.Equals(a.CodigoProducto, producto, StringComparison.OrdinalIgnoreCase);

        return anexos.OrderByDescending(a => a.Coincide).ThenBy(a => a.CodigoPlan).ToList();
    }

    // POST /Studio/AuditoriaCasos/Generar  → corre el agente propio y persiste el dictamen
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generar(Guid caseCode)
    {
        var caso = await _db.ProcessCase
            .Include(c => c.DataFile)
            .Include(c => c.Notes)
            .FirstOrDefaultAsync(c => c.CaseCode == caseCode);
        if (caso == null)
        {
            TempData["Error"] = "Caso no encontrado.";
            return RedirectToAction(nameof(Index));
        }

        var agent = await _db.Agent
            .Include(a => a.AgentConfig)
            .Include(a => a.OPAIModelTool).ThenInclude(mt => mt.ToolCodeNavigation)
            .FirstOrDefaultAsync(a => a.Code == AgenteAuditorCasos && a.IsActive);
        if (agent?.AgentConfig == null)
        {
            TempData["Error"] = $"El agente '{AgenteAuditorCasos}' no está configurado o está inactivo.";
            return RedirectToAction(nameof(Caso), new { caseCode });
        }

        var config     = agent.AgentConfig;
        var caseCtx    = OcrPromptHelper.BuildCaseContext(caso.Notes);
        var caseCedula = OcrPromptHelper.ExtractCedulaFromCaseContext(caso.Notes);

        // OCR DIFERIDO: los documentos se trajeron rápido (sin OCR). Aquí, al analizar,
        // se extrae el texto de los que falten. Es el momento donde el auditor ya espera
        // que "piense", así que el costo del OCR no retrasa el traer los documentos.
        var numSobreCtx = OcrPromptHelper.ExtractStringFromCaseContext(caso.Notes, "numeroSobre");
        if (!string.IsNullOrWhiteSpace(numSobreCtx))
        {
            try { await _armonix.OcrDocumentosPendientesAsync(numSobreCtx, caso, _db, HttpContext.RequestAborted); }
            catch (Exception ex) { _logger.LogWarning(ex, "[REQ-046] OCR diferido falló para caso {CaseCode}.", caseCode); }
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(caseCtx))
            sb.AppendLine("## Datos estructurados del sobre").AppendLine(caseCtx).AppendLine();
        sb.AppendLine("## Documentos del caso (OCR)");
        if (caso.DataFile.Count == 0)
            sb.AppendLine("(el caso no tiene documentos adjuntos)");
        foreach (var f in caso.DataFile.OrderBy(f => f.CreatedDate))
        {
            sb.AppendLine($"--- {f.OriginalName} ---");
            sb.AppendLine(string.IsNullOrWhiteSpace(f.Text) ? "(sin texto OCR)" : f.Text);
            sb.AppendLine();
        }
        sb.AppendLine("## Solicitud")
          .AppendLine("Audita este caso de reembolso y devuelve ÚNICAMENTE el JSON de 6 secciones del formato de salida. Usa las herramientas de la biblioteca de anexos para el análisis contractual y las demás para verificar historial y preexistencias.");

        var aiRequest = new AiCompletionRequest
        {
            SystemPrompt = OcrPromptHelper.ResolveSystemPrompt(agent),
            UserMessage  = sb.ToString(),
            MaxTokens    = agent.MaxTokens ?? 8000,
            Temperature  = agent.Temperature,
            ThinkingMode = agent.ThinkingMode
        };

        var enabledTools = agent.OPAIModelTool
            .Where(mt => mt.IsEnabled && mt.ToolCodeNavigation?.IsActive == true)
            .Select(mt => mt.ToolCodeNavigation!)
            .ToList();
        var hasTools = enabledTools.Count > 0 && _toolExecutor != null
                       && string.Equals(config.Provider, "Anthropic", StringComparison.OrdinalIgnoreCase);

        string texto;
        try
        {
            var svc = _factory.Create(config);
            if (hasTools)
            {
                var exec = new StepExecution
                {
                    CaseCode       = caseCode,
                    StepOrder      = 0,
                    DataFileId     = caso.DataFile.FirstOrDefault()?.Id,
                    ModelCode      = agent.Code,
                    RequestContent = aiRequest.UserMessage,
                    Status         = "Running",
                    StartDate      = DateTime.UtcNow,
                    EndpointUrl    = config.EndpointUrl
                };
                _db.StepExecution.Add(exec);
                await _db.SaveChangesAsync();

                var toolsCtx = new ToolsContext
                {
                    AvailableTools      = enabledTools,
                    AgentCode           = agent.Code,
                    ExecutionId         = exec.ExecutionId,
                    CaseIdentity        = caseCedula,
                    ToolChoice          = agent.ToolChoice,
                    StreamEventCallback = null
                };
                var res = await svc.CompleteWithToolsAsync(aiRequest, toolsCtx, _toolExecutor!, HttpContext.RequestAborted);
                texto = res.Text;

                exec.ResponseContent = texto;
                exec.Status  = "Completed";
                exec.EndDate = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                await UsoDelModelo.ApuntarAsync(_db, exec.ExecutionId, res);
            }
            else
            {
                var res = await svc.CompleteAsync(aiRequest, HttpContext.RequestAborted);
                texto = res.Text;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REQ-046] Error generando el dictamen del caso {CaseCode}.", caseCode);
            TempData["Error"] = $"Error al generar la auditoría: {ex.Message}";
            return RedirectToAction(nameof(Caso), new { caseCode });
        }

        var json = ExtractJson(texto);
        var previas = await _db.Note.Where(n => n.CaseCode == caseCode && n.Title == NoteAuditoriaCasos).ToListAsync();
        if (previas.Count > 0) _db.Note.RemoveRange(previas);
        _db.Note.Add(new Note
        {
            CaseCode  = caseCode,
            Title     = NoteAuditoriaCasos,
            Detail    = string.IsNullOrWhiteSpace(json) ? texto : json,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = $"auditoria-casos-ia|{User?.Identity?.Name}"
        });
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Caso), new { caseCode });
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private AuditoriaCasoViewModel BuildCasoVm(ProcessCase caso)
    {
        var vm = new AuditoriaCasoViewModel
        {
            CaseCode   = caso.CaseCode,
            Documentos = caso.DataFile.OrderBy(f => f.CreatedDate).ToList()
        };

        // Datos del sobre desde el contexto
        vm.NumeroSobre    = OcrPromptHelper.ExtractStringFromCaseContext(caso.Notes, "numeroSobre");
        vm.NombreTitular  = OcrPromptHelper.ExtractStringFromCaseContext(caso.Notes, "nombreTitular");
        vm.Producto       = OcrPromptHelper.ExtractStringFromCaseContext(caso.Notes, "producto");
        vm.NumeroContrato = OcrPromptHelper.ExtractStringFromCaseContext(caso.Notes, "numeroContrato");

        var nota = caso.Notes
            .Where(n => n.Title == NoteAuditoriaCasos)
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefault();

        if (nota != null && !string.IsNullOrWhiteSpace(nota.Detail))
        {
            vm.Generada    = true;
            vm.GeneradoEn  = nota.CreatedAt;
            vm.GeneradoPor = nota.CreatedBy;
            if (nota.Detail.TrimStart().StartsWith('{'))
            {
                try { vm.Dictamen = JsonSerializer.Deserialize<AuditoriaCasoDto>(nota.Detail, JsonOpts); }
                catch (JsonException ex) { _logger.LogWarning(ex, "Dictamen JSON inválido {CaseCode}.", caso.CaseCode); }
            }
            if (vm.Dictamen == null)
            {
                vm.RawText = nota.Detail;
                vm.Error   = "El motor no devolvió un dictamen estructurado; se muestra el texto para revisión humana.";
            }
        }
        return vm;
    }

    private async Task<List<CasoAuditoriaReciente>> CargarRecientesAsync()
    {
        var casos = await _db.ProcessCase.AsNoTracking()
            .Where(c => c.DefinitionCode == ProcesoAuditoria)
            .OrderByDescending(c => c.StartDate)
            .Take(15)
            .Select(c => new
            {
                c.CaseCode, c.State, c.StartDate,
                Docs = c.DataFile.Count,
                Contexto = c.Notes.Where(n => n.Title == OcrPromptHelper.ContextoSobreNoteTitle).Select(n => n.Detail).FirstOrDefault(),
                TieneAud = c.Notes.Any(n => n.Title == NoteAuditoriaCasos)
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
}
