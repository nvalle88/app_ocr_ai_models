using System.Text;
using System.Text.Json;
using app_ocr_ai_models.Areas.Studio.Models;
using app_ocr_ai_models.Data;
using app_tramites.Models.ModelAi;
using app_tramites.Services.Ai;
using app_tramites.Services.Ai.Tools;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace app_ocr_ai_models.Areas.Studio.Controllers;

// ============================================================
// REQ-019 — Auditoría de Medicina (paridad con Resolución de reembolso).
//   Ejecuta SIEMPRE el agente AGENTE_AUDITOR_MEDICINA (independiente del
//   proceso del caso) CON sus herramientas, para que pueda revisar el
//   historial de compras del cliente (deteccion de abuso) y preexistencias.
//   Persiste el JSON como Note "AuditoriaMedicina".
//
//   SALVAGUARDA: el agente SEÑALA banderas; no dictamina ni aprueba cobertura.
// ============================================================

/// <summary>Controller de la auditoría de medicina asistida por IA.</summary>
[Area("Studio")]
[Authorize]
public sealed class AuditoriaController : Controller
{
    private const string NoteAuditoria = "AuditoriaMedicina";
    private const string AgenteAuditor = "AGENTE_AUDITOR_MEDICINA";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly OCRDbContext _db;
    private readonly AiCompletionServiceFactory _factory;
    private readonly IToolExecutor? _toolExecutor;
    private readonly ILogger<AuditoriaController> _logger;
    private readonly Services.Ai.IPreValidaciones? _previas;

    public AuditoriaController(
        OCRDbContext db,
        AiCompletionServiceFactory factory,
        ILogger<AuditoriaController> logger,
        IToolExecutor? toolExecutor = null,
        Services.Ai.IPreValidaciones? previas = null)
    {
        _previas = previas;
        _db           = db      ?? throw new ArgumentNullException(nameof(db));
        _factory      = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger       = logger  ?? throw new ArgumentNullException(nameof(logger));
        _toolExecutor = toolExecutor;
    }

    // GET /Studio/Auditoria?caseCode=&embed=true
    [HttpGet]
    public async Task<IActionResult> Index(Guid caseCode, bool embed = false)
    {
        ViewData["Embed"] = embed;
        if (caseCode == Guid.Empty)
            return View(new AuditoriaViewModel { Error = "Falta el CaseCode." });

        var nota = await _db.Note.AsNoTracking()
            .Where(n => n.CaseCode == caseCode && n.Title == NoteAuditoria)
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync();

        if (nota == null)
            return View(new AuditoriaViewModel { CaseCode = caseCode, Generada = false });

        return View(BuildVm(caseCode, nota.Detail, nota.CreatedAt,
                            await CargarToolInvocacionesAsync(caseCode)));
    }

    // POST /Studio/Auditoria/Generar
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

        // Agente auditor con su config, prompts apilados y tools habilitadas
        var agent = await _db.Agent
            .Include(a => a.AgentConfig)
            .Include(a => a.OPAIModelPrompt).ThenInclude(op => op.PromptCodeNavigation)
            .Include(a => a.OPAIModelTool).ThenInclude(mt => mt.ToolCodeNavigation)
            .FirstOrDefaultAsync(a => a.Code == AgenteAuditor && a.IsActive);

        if (agent?.AgentConfig == null)
        {
            TempData["Error"] = $"El agente '{AgenteAuditor}' no está configurado o está inactivo.";
            return RedirectToAction(nameof(Index), new { caseCode, embed });
        }

        var config     = agent.AgentConfig;
        var caseCtx    = OcrPromptHelper.BuildCaseContext(caso.Notes);
        var caseCedula = OcrPromptHelper.ExtractCedulaFromCaseContext(caso.Notes);

        // Mensaje: contexto del sobre + OCR de los documentos + instrucción
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
          .AppendLine("Audita este caso de medicina. Usa las herramientas para verificar el historial de sobres del cliente (recompras/duplicidad) y sus preexistencias cuando aporte. Devuelve únicamente el JSON del formato de salida.");

        var aiRequest = new AiCompletionRequest
        {
            SystemPrompt = OcrPromptHelper.ResolveSystemPrompt(agent),
            UserMessage  = sb.ToString(),
            MaxTokens    = agent.MaxTokens ?? 6000,
            Temperature  = agent.Temperature,
            ThinkingMode = agent.ThinkingMode
        };

        // Tools habilitadas del auditor
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
                // StepExecution para atar las ToolInvocation (audit trail)
                var exec = new StepExecution
                {
                    CaseCode       = caseCode,
                    StepOrder      = 0,
                    // Sin documentos no se inventa uno: la columna admite null (REQ-020b).
                    DataFileId     = caso.DataFile.FirstOrDefault()?.Id,
                    ModelCode      = agent.Code,
                    RequestContent = aiRequest.UserMessage,
                    Status         = "Running",
                    StartDate      = DateTime.UtcNow,
                    EndpointUrl    = config.EndpointUrl
                };
                _db.StepExecution.Add(exec);
                await _db.SaveChangesAsync();

                // ── Lo que se va a consultar SI o SI, ya consultado ──────────
                //
                // Cada herramienta que el modelo pide es una ida y vuelta
                // completa. Medido: 6,6 herramientas por ejecucion y 87 s de
                // media, contra los 4 s del agente que solo pide una. El
                // contrato, las preexistencias y el convenio del prestador se
                // consultan SIEMPRE, asi que se lanzan a la vez y entran ya en
                // el mensaje. Si alguna falla se omite y el agente la pide.
                if (_previas != null)
                {
                    var rucs = caso.DataFile
                        .SelectMany(f => _db.DocumentoClasificacion
                                            .Where(c => c.DataFileId == f.Id && c.IsCurrent)
                                            .Select(c => c.EmisorRuc))
                        .ToList();

                    var bloque = await _previas.BloqueAsync(
                        agent.Code, exec.ExecutionId, caseCedula, rucs, caseCedula,
                        HttpContext.RequestAborted);

                    if (!string.IsNullOrWhiteSpace(bloque))
                    {
                        // UserMessage es init-only: se rehace la petición en vez
                        // de mutarla.
                        aiRequest = new AiCompletionRequest
                        {
                            SystemPrompt = aiRequest.SystemPrompt,
                            UserMessage  = bloque + Environment.NewLine + aiRequest.UserMessage,
                            MaxTokens    = aiRequest.MaxTokens,
                            Temperature  = aiRequest.Temperature,
                            ThinkingMode = aiRequest.ThinkingMode
                        };
                        exec.RequestContent = aiRequest.UserMessage;
                        await _db.SaveChangesAsync();
                    }
                }

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
            _logger.LogWarning(ex, "Error generando la auditoría del caso {CaseCode}.", caseCode);
            TempData["Error"] = $"Error al generar la auditoría: {ex.Message}";
            return RedirectToAction(nameof(Index), new { caseCode, embed });
        }

        var json = ExtractJson(texto);

        // Persistir (una vigente por caso). Si no hubo JSON, se guarda el texto para revisión humana.
        var previas = await _db.Note.Where(n => n.CaseCode == caseCode && n.Title == NoteAuditoria).ToListAsync();
        if (previas.Count > 0) _db.Note.RemoveRange(previas);
        _db.Note.Add(new Note
        {
            CaseCode  = caseCode,
            Title     = NoteAuditoria,
            Detail    = string.IsNullOrWhiteSpace(json) ? texto : json,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = $"auditoria-ia|{User?.Identity?.Name}"
        });
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index), new { caseCode, embed });
    }

    // GET /Studio/Auditoria/Informe?caseCode= → PDF imprimible
    [HttpGet]
    public async Task<IActionResult> Informe(Guid caseCode)
    {
        var nota = await _db.Note.AsNoTracking()
            .Where(n => n.CaseCode == caseCode && n.Title == NoteAuditoria)
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync();

        if (nota == null) return NotFound("Genera primero la auditoría del caso.");

        return View(BuildVm(caseCode, nota.Detail, nota.CreatedAt,
                            await CargarToolInvocacionesAsync(caseCode)));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private AuditoriaViewModel BuildVm(Guid caseCode, string contenido, DateTime creado,
        IReadOnlyList<ToolInvocacionVM> tools)
    {
        AuditoriaMedicinaDto? dto = null;
        if (!string.IsNullOrWhiteSpace(contenido) && contenido.TrimStart().StartsWith('{'))
        {
            try { dto = JsonSerializer.Deserialize<AuditoriaMedicinaDto>(contenido, JsonOpts); }
            catch (JsonException ex) { _logger.LogWarning(ex, "Auditoría JSON inválido {CaseCode}.", caseCode); }
        }

        if (dto != null)
            dto.Alertas = dto.Alertas.OrderBy(a => a.Orden).ToList();

        return new AuditoriaViewModel
        {
            CaseCode         = caseCode,
            Generada         = true,
            Auditoria        = dto,
            RawText          = dto == null ? contenido : null,
            GeneradoEn       = creado,
            GeneradoPor      = User?.Identity?.Name ?? "—",
            ToolInvocaciones = tools,
            Error            = dto == null ? "El motor no devolvió una auditoría estructurada; se muestra el texto para revisión humana." : null
        };
    }

    /// <summary>Audit trail real de herramientas ejecutadas para el caso.</summary>
    private async Task<IReadOnlyList<ToolInvocacionVM>> CargarToolInvocacionesAsync(Guid caseCode)
    {
        try
        {
            return await _db.ToolInvocation.AsNoTracking()
                .Where(ti => ti.Execution.CaseCode == caseCode)
                .OrderByDescending(ti => ti.StartDate)
                .Take(30)
                .Select(ti => new ToolInvocacionVM
                {
                    ToolCode    = ti.ToolCode,
                    StartDate   = ti.StartDate,
                    DuracionSeg = ti.EndDate != null
                        ? EF.Functions.DateDiffMillisecond(ti.StartDate, ti.EndDate.Value) / 1000.0
                        : null,
                    IsError      = ti.IsError,
                    InputResumen = ti.RequestJson
                })
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudieron cargar ToolInvocation del caso {CaseCode}.", caseCode);
            return Array.Empty<ToolInvocacionVM>();
        }
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
