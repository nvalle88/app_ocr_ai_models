using app_ocr_ai_models.Areas.Studio.Models;
using app_ocr_ai_models.Data;
using app_tramites.Models.ModelAi;
using app_tramites.Services.Ai;
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
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ILogger<ChatController> _logger;

    /// <summary>
    /// Crea el controller con sus dependencias.
    /// </summary>
    /// <param name="db">Contexto EF de la BD OCR.</param>
    /// <param name="factory">Factory de servicios de completado IA.</param>
    /// <param name="userManager">Gestor de identidad ASP.NET Core.</param>
    /// <param name="logger">Logger.</param>
    public ChatController(
        OCRDbContext db,
        AiCompletionServiceFactory factory,
        UserManager<IdentityUser> userManager,
        ILogger<ChatController> logger)
    {
        _db          = db          ?? throw new ArgumentNullException(nameof(db));
        _factory     = factory     ?? throw new ArgumentNullException(nameof(factory));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _logger      = logger      ?? throw new ArgumentNullException(nameof(logger));
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
    /// Protocolo de eventos:
    /// <list type="bullet">
    ///   <item><c>event: thinking</c> — delta de razonamiento interno (Claude extended thinking).</item>
    ///   <item><c>event: text</c>    — delta de texto visible.</item>
    ///   <item><c>event: done</c>    — fin del stream, payload JSON con tokens.</item>
    ///   <item><c>event: error</c>   — error fatal durante el stream.</item>
    /// </list>
    /// <para>TODO T5: cuando se implemente function-calling, aquí se añadirán
    /// eventos <c>event: tool_use</c> para renderizar chips de herramienta.</para>
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

        // Acumulador para persistencia al final
        var fullText     = new StringBuilder();
        var fullThinking = new StringBuilder();
        AiStreamChunk? doneChunk = null;

        try
        {
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

                var sseFrame = $"event: {eventName}\ndata: {payload}\n\n";
                await writer.WriteAsync(enc.GetBytes(sseFrame), ct);
                await writer.FlushAsync(ct);
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
            var errorFrame   = $"event: error\ndata: {errorPayload}\n\n";
            try
            {
                await writer.WriteAsync(enc.GetBytes(errorFrame), ct);
                await writer.FlushAsync(ct);
            }
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
