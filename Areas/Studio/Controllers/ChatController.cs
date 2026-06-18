using app_ocr_ai_models.Areas.Studio.Models;
using app_ocr_ai_models.Data;
using app_tramites.Models.ModelAi;
using app_tramites.Services.Ai;
using app_tramites.Services.Ai.Tools;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace app_ocr_ai_models.Areas.Studio.Controllers;

// ============================================================
// REQ-019 T7 — Área Studio: chat streaming "tipo Claude" (SSE).
// Régimen doble: NO toca NexusController.ChatAjax ni A-HOSP.
// ============================================================

/// <summary>
/// Controller del Área Studio para chat interactivo con streaming SSE.
/// Consume <see cref="IAiCompletionService.StreamAsync"/> y reenvía
/// cada <see cref="AiStreamChunk"/> al navegador como Server-Sent Events.
/// </summary>
[Area("Studio")]
[Authorize]
public sealed class ChatController : Controller
{
    private readonly OCRDbContext _db;
    private readonly AiCompletionServiceFactory _factory;
    private readonly IToolExecutor? _toolExecutor;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ILogger<ChatController> _logger;

    /// <summary>
    /// Crea el controller con sus dependencias.
    /// </summary>
    /// <param name="db">Contexto EF de la BD OCR.</param>
    /// <param name="factory">Factory de servicios de completado IA.</param>
    /// <param name="userManager">Gestor de identidad ASP.NET Core.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="toolExecutor">
    /// Executor de tools (REQ-019 T5). Opcional: si es nulo, el chat opera sin tools.
    /// Se inyecta cuando el servidor tiene acceso a las APIs internas (B1/B2 desbloqueados).
    /// </param>
    public ChatController(
        OCRDbContext db,
        AiCompletionServiceFactory factory,
        UserManager<IdentityUser> userManager,
        ILogger<ChatController> logger,
        IToolExecutor? toolExecutor = null)
    {
        _db           = db           ?? throw new ArgumentNullException(nameof(db));
        _factory      = factory      ?? throw new ArgumentNullException(nameof(factory));
        _userManager  = userManager  ?? throw new ArgumentNullException(nameof(userManager));
        _logger       = logger       ?? throw new ArgumentNullException(nameof(logger));
        _toolExecutor = toolExecutor; // null si B1/B2 no disponibles
    }

    // ----------------------------------------------------------------
    // GET /Studio/Chat/Index?caseCode=<guid>
    // Muestra la UI de chat para el caso indicado
    // ----------------------------------------------------------------

    /// <summary>
    /// Muestra la interfaz de chat para un caso ya importado.
    /// </summary>
    /// <param name="caseCode">CaseCode del caso (opcional desde query string).</param>
    [HttpGet]
    public IActionResult Index(Guid? caseCode)
    {
        var vm = new ChatIndexViewModel
        {
            CaseCode = caseCode ?? Guid.Empty
        };
        return View(vm);
    }

    // ----------------------------------------------------------------
    // POST /Studio/Chat/Stream  (JSON body: ChatStreamRequest)
    // Endpoint SSE: emite chunks del modelo en tiempo real
    // ----------------------------------------------------------------

    /// <summary>
    /// Endpoint SSE que transmite la respuesta del modelo en tiempo real.
    /// Construye el contexto OCR del caso, llama a
    /// <see cref="IAiCompletionService.StreamAsync"/> y reenvía los chunks
    /// como Server-Sent Events con <c>Content-Type: text/event-stream</c>.
    /// </summary>
    /// <remarks>
    /// Protocolo de eventos (REQ-019 T5/T7):
    /// <list type="bullet">
    ///   <item><c>event: thinking</c>     — delta de razonamiento interno (Claude extended thinking).</item>
    ///   <item><c>event: text</c>         — delta de texto visible.</item>
    ///   <item><c>event: tool_use</c>     — chip: el modelo invocó una tool (nombre + input resumido).</item>
    ///   <item><c>event: tool_result</c>  — chip: resultado de la tool (ok/error).</item>
    ///   <item><c>event: done</c>         — fin del stream, payload JSON con tokens.</item>
    ///   <item><c>event: error</c>        — error fatal durante el stream.</item>
    /// </list>
    /// El path de tools solo se activa si el agente tiene tools habilitadas (<c>OPAIModelTool</c>)
    /// y el <see cref="IToolExecutor"/> está registrado en DI (B1/B2 desbloqueados).
    /// </remarks>
    /// <param name="request">CaseCode y mensaje del usuario.</param>
    /// <param name="ct">Token de cancelación del request HTTP.</param>
    [HttpPost]
    public async Task Stream([FromBody] ChatStreamRequest request, CancellationToken ct)
    {
        if (request == null || request.CaseCode == Guid.Empty || string.IsNullOrWhiteSpace(request.Message))
        {
            Response.StatusCode = 400;
            return;
        }

        // ── 1. Cargar caso + DataFiles ────────────────────────────────────
        var processCase = await _db.ProcessCase
            .Include(pc => pc.DataFile)
            .FirstOrDefaultAsync(pc => pc.CaseCode == request.CaseCode, ct);

        if (processCase == null)
        {
            Response.StatusCode = 404;
            return;
        }

        // ── 2. Seleccionar agente activo para el caso ─────────────────────
        //   Busca el primer AgentProcess activo del proceso del caso que tenga
        //   una OPAIConfiguration (Anthropic preferido, Azure OpenAI como fallback).
        var agentProcess = await _db.AgentProcesses
            .Include(ap => ap.Agent)
                .ThenInclude(a => a.AgentConfig)
            .Include(ap => ap.Agent)
                .ThenInclude(a => a.OPAIModelPrompt)
                    .ThenInclude(op => op.PromptCodeNavigation)
            .Include(ap => ap.Agent)
                .ThenInclude(a => a.OPAIModelTool)
                    .ThenInclude(mt => mt.ToolCodeNavigation)
            .Where(ap =>
                ap.DefinitionCode == processCase.DefinitionCode
                && ap.Agent.IsActive
                && ap.Agent.AgentConfig != null)
            .OrderByDescending(ap =>
                ap.Agent.AgentConfig.Provider == "Anthropic" ? 1 : 0)
            .FirstOrDefaultAsync(ct);

        if (agentProcess?.Agent?.AgentConfig == null)
        {
            Response.StatusCode = 503;
            return;
        }

        var agent  = agentProcess.Agent;
        var config = agent.AgentConfig;

        // ── 3. Construir prompt de sistema ───────────────────────────────
        var systemPrompt = ResolveSystemPrompt(agent);

        // ── 4. Construir mensaje del usuario: contexto OCR + pregunta ────
        var ocrContext  = BuildOcrContext(processCase.DataFile);
        var userMessage = BuildUserMessage(ocrContext, request.Message);

        var aiRequest = new AiCompletionRequest
        {
            SystemPrompt = systemPrompt,
            UserMessage  = userMessage,
            MaxTokens    = agent.MaxTokens ?? 4096,
            Temperature  = agent.Temperature,
            ThinkingMode = agent.ThinkingMode
        };

        // ── 5. Configurar respuesta SSE ───────────────────────────────────
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"]    = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";   // desactiva buffering en Nginx

        var completionService = _factory.Create(config);
        var writer            = Response.Body;
        var enc               = Encoding.UTF8;

        // ── 5b. Verificar si el agente tiene tools habilitadas ────────────
        //   El path de tool-use se activa solo si:
        //   a) el agente tiene OPAIModelTool con IsEnabled, Y
        //   b) el IToolExecutor está disponible (B1/B2 desbloqueados)
        var enabledTools = agent.OPAIModelTool
            .Where(mt => mt.IsEnabled && mt.ToolCodeNavigation?.IsActive == true)
            .Select(mt => mt.ToolCodeNavigation!)
            .ToList();

        var hasTools = enabledTools.Count > 0
                       && _toolExecutor != null
                       && string.Equals(config.Provider, "Anthropic", StringComparison.OrdinalIgnoreCase);

        // Acumulador para persistencia al final
        var fullText     = new StringBuilder();
        var fullThinking = new StringBuilder();
        AiStreamChunk? doneChunk = null;

        // Helper local: emite un frame SSE
        async Task EmitSseAsync(string evtName, string payload)
        {
            var frame = $"event: {evtName}\ndata: {payload}\n\n";
            await writer.WriteAsync(enc.GetBytes(frame), ct);
            await writer.FlushAsync(ct);
        }

        try
        {
            if (hasTools)
            {
                // ── PATH con tools: CompleteWithToolsAsync (tool-use loop) ──
                // Persistir un StepExecution para atar las ToolInvocation
                var execution = new StepExecution
                {
                    CaseCode       = processCase.CaseCode,
                    StepOrder      = 0,        // chat ad-hoc
                    DataFileId     = processCase.DataFile.FirstOrDefault()?.Id ?? 0,
                    ModelCode      = agent.Code,
                    RequestContent = userMessage,
                    Status         = "Running",
                    StartDate      = DateTime.UtcNow,
                    EndpointUrl    = config.EndpointUrl
                };
                _db.StepExecution.Add(execution);
                await _db.SaveChangesAsync(ct);

                // Callback SSE para emitir chips de tool en tiempo real
                async Task OnToolEvent(ToolStreamEvent evt)
                {
                    string toolPayload = JsonSerializer.Serialize(new
                    {
                        toolName     = evt.ToolName,
                        inputSummary = evt.InputSummary,
                        resultStatus = evt.ResultStatus
                    });
                    await EmitSseAsync(evt.EventType, toolPayload);
                }

                var toolsContext = new ToolsContext
                {
                    AvailableTools      = enabledTools,
                    AgentCode           = agent.Code,
                    ExecutionId         = execution.ExecutionId,
                    CaseIdentity        = null,   // sin identidad de caso en chat (se resuelve por la tool)
                    ToolChoice          = agent.ToolChoice ?? "auto",
                    StreamEventCallback = OnToolEvent
                };

                var toolResult = await completionService.CompleteWithToolsAsync(
                    aiRequest, toolsContext, _toolExecutor!, ct)
                    .ConfigureAwait(false);

                // Emitir texto final como un único chunk text
                if (!string.IsNullOrEmpty(toolResult.Text))
                {
                    fullText.Append(toolResult.Text);
                    await EmitSseAsync("text", JsonSerializer.Serialize(new { delta = toolResult.Text }));
                }

                // Actualizar StepExecution
                execution.ResponseContent = toolResult.Text;
                execution.Status          = "Completed";
                execution.EndDate         = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                doneChunk = AiStreamChunk.DoneChunk(toolResult.PromptTokens, toolResult.CompletionTokens);
                await EmitSseAsync("done", JsonSerializer.Serialize(new
                {
                    promptTokens     = toolResult.PromptTokens,
                    completionTokens = toolResult.CompletionTokens,
                    thinkingTokens   = (int?)null
                }));
            }
            else
            {
                // ── PATH sin tools: stream clásico ────────────────────────
                await foreach (var chunk in completionService.StreamAsync(aiRequest, ct).ConfigureAwait(false))
                {
                    string eventName;
                    string payload;

                    switch (chunk.Type)
                    {
                        case AiStreamChunkType.Thinking:
                            eventName = "thinking";
                            fullThinking.Append(chunk.Delta);
                            payload = JsonSerializer.Serialize(new { delta = chunk.Delta });
                            break;

                        case AiStreamChunkType.Text:
                            eventName = "text";
                            fullText.Append(chunk.Delta);
                            payload = JsonSerializer.Serialize(new { delta = chunk.Delta });
                            break;

                        case AiStreamChunkType.Done:
                        default:
                            doneChunk = chunk;
                            eventName = "done";
                            payload   = JsonSerializer.Serialize(new
                            {
                                promptTokens     = chunk.PromptTokens,
                                completionTokens = chunk.CompletionTokens,
                                thinkingTokens   = chunk.ThinkingTokens
                            });
                            break;
                    }

                    await EmitSseAsync(eventName, payload);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // El cliente cerró la conexión — no propagar
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error durante streaming SSE para caso {CaseCode}.", request.CaseCode);
            var errorPayload = JsonSerializer.Serialize(new { message = ex.Message });
            try { await EmitSseAsync("error", errorPayload); }
            catch (Exception writeEx)
            {
                _logger.LogWarning(writeEx, "No se pudo escribir el evento error SSE.");
            }
        }

        // ── 6. Persistir resultado ────────────────────────────────────────
        if (doneChunk != null && fullText.Length > 0)
        {
            try
            {
                await PersistChatResultAsync(
                    processCase.CaseCode,
                    agentProcess,
                    userMessage,
                    fullText.ToString(),
                    doneChunk,
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo persistir el resultado del chat SSE para caso {CaseCode}.", request.CaseCode);
            }
        }
    }

    // ── Helpers privados ──────────────────────────────────────────────────

    private static string ResolveSystemPrompt(Agent agent)
    {
        if (!string.IsNullOrWhiteSpace(agent.SystemPrompt))
            return agent.SystemPrompt;

        var promptContent = agent.OPAIModelPrompt
            ?.OrderBy(p => p.Order)
            .Select(p => p.PromptCodeNavigation?.Content)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        return !string.IsNullOrWhiteSpace(promptContent)
            ? promptContent
            : "Eres un asistente IA especializado en análisis de documentos OCR. Responde con claridad y precisión.";
    }

    private static string BuildOcrContext(IEnumerable<DataFile> files)
    {
        var sb = new StringBuilder();
        foreach (var f in files)
        {
            if (!string.IsNullOrWhiteSpace(f.Text))
                sb.AppendLine($"--- Documento: {f.OriginalName} ---").AppendLine(f.Text);
        }
        return sb.ToString();
    }

    private static string BuildUserMessage(string ocrContext, string userQuestion)
    {
        if (string.IsNullOrWhiteSpace(ocrContext))
            return userQuestion.Trim();

        return new StringBuilder()
            .AppendLine("## Documentos OCR del caso")
            .AppendLine(ocrContext)
            .AppendLine("## Pregunta del usuario")
            .AppendLine(userQuestion.Trim())
            .ToString()
            .Trim();
    }

    private async Task PersistChatResultAsync(
        Guid caseCode,
        AgentProcess agentProcess,
        string requestText,
        string responseText,
        AiStreamChunk doneChunk,
        CancellationToken ct)
    {
        var result = new FinalResponseResult
        {
            CaseCode        = caseCode,
            ResponseText    = responseText,
            RequestText     = requestText,
            CreatedDate     = DateTime.UtcNow,
            AgentProccessId = agentProcess.Id,
            MetadataJson    = JsonSerializer.Serialize(new
            {
                source           = "Studio.Chat.SSE",
                promptTokens     = doneChunk.PromptTokens,
                completionTokens = doneChunk.CompletionTokens,
                thinkingTokens   = doneChunk.ThinkingTokens
            })
        };

        _db.FinalResponseResult.Add(result);
        await _db.SaveChangesAsync(ct);
    }
}
