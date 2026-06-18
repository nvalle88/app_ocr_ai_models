using app_ocr_ai_models.Data;
using app_tramites.Models.ModelAi;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Usage = app_tramites.Models.ModelAi.Usage;

namespace app_tramites.Services.Ai;

/// <summary>
/// Implementación del orquestador de análisis multi-paso para el Área Studio.
/// </summary>
/// <remarks>
/// <para>
/// Flujo:
/// <list type="number">
///   <item><description>Carga el <see cref="ProcessCase"/> con sus <see cref="DataFile"/>.</description></item>
///   <item><description>Paso CLASIFICADOR: si no se forzó un proceso, usa el primer agente activo
///     del caso para determinar qué proceso aplicar.</description></item>
///   <item><description>Runner multi-paso: itera los <see cref="ProcessStep"/> del proceso
///     seleccionado en orden, llama al proveedor IA y persiste
///     <see cref="StepExecution"/> + <see cref="Usage"/> por paso.</description></item>
/// </list>
/// </para>
/// <para>
/// Autorización: usa el mismo patrón de <c>GetProcessesByUser</c> de <c>NexusService</c>
/// (PolicyUser → Policys → AccessAgentPolicies) para filtrar agentes accesibles al usuario.
/// No añade autorización por tool/step (queda para T5/D2).
/// </para>
/// </remarks>
public sealed class ProcessOrchestrator : IProcessOrchestrator
{
    private readonly OCRDbContext _db;
    private readonly AiCompletionServiceFactory _factory;
    private readonly ILogger<ProcessOrchestrator> _logger;

    /// <summary>
    /// Inicializa el orquestador con sus dependencias.
    /// </summary>
    /// <param name="db">Contexto EF de la BD OCR.</param>
    /// <param name="factory">Factory de servicios de completado IA.</param>
    /// <param name="logger">Logger.</param>
    public ProcessOrchestrator(
        OCRDbContext db,
        AiCompletionServiceFactory factory,
        ILogger<ProcessOrchestrator> logger)
    {
        _db      = db      ?? throw new ArgumentNullException(nameof(db));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger  = logger  ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<OrchestrationResult> RunAsync(
        Guid caseCode,
        IdentityUser? user,
        string? processCodeOverride = null,
        CancellationToken cancellationToken = default)
    {
        // ── 1. Cargar el caso ─────────────────────────────────────────────
        var processCase = await _db.ProcessCase
            .Include(pc => pc.DataFile)
            .Include(pc => pc.DefinitionCodeNavigation)
            .FirstOrDefaultAsync(pc => pc.CaseCode == caseCode, cancellationToken);

        if (processCase == null)
            return Failed(caseCode, $"Caso {caseCode} no encontrado.");

        // ── 2. Construir contexto OCR ─────────────────────────────────────
        var ocrContext = BuildOcrContext(processCase.DataFile);

        // ── 3. Obtener agentes permitidos (autorización existente) ────────
        var allowedAgentCodes = await GetAllowedAgentCodesAsync(user, processCase.DefinitionCode);

        // ── 4. Clasificador ───────────────────────────────────────────────
        string processCode;
        string classificationText;

        if (!string.IsNullOrWhiteSpace(processCodeOverride))
        {
            processCode        = processCodeOverride;
            classificationText = $"[Override directo: {processCodeOverride}]";
        }
        else
        {
            var classResult = await RunClassifierAsync(
                processCase, ocrContext, allowedAgentCodes, cancellationToken);

            if (classResult == null)
                return Failed(caseCode, "No se encontró un agente clasificador activo para este caso.");

            processCode        = classResult.Value.ProcessCode;
            classificationText = classResult.Value.Text;
        }

        // ── 5. Cargar proceso y pasos ─────────────────────────────────────
        var process = await _db.Process
            .Include(p => p.ProcessStep.OrderBy(s => s.StepOrder))
                .ThenInclude(s => s.ModelCodeNavigation)
                    .ThenInclude(a => a.AgentConfig)
            .Include(p => p.ProcessStep)
                .ThenInclude(s => s.ModelCodeNavigation)
                    .ThenInclude(a => a.OPAIModelPrompt)
                    .ThenInclude(op => op.PromptCodeNavigation)
            .FirstOrDefaultAsync(p => p.Code == processCode, cancellationToken);

        if (process == null)
            return Failed(caseCode, $"Proceso '{processCode}' no encontrado.");

        if (!process.ProcessStep.Any())
        {
            return new OrchestrationResult
            {
                CaseCode           = caseCode,
                ProcessCode        = processCode,
                ClassificationText = classificationText,
                Steps              = Array.Empty<StepResult>(),
                Success            = true
            };
        }

        // ── 6. Runner multi-paso ──────────────────────────────────────────
        var stepResults       = new List<StepResult>();
        var previousResponses = new List<string>();

        foreach (var step in process.ProcessStep.OrderBy(s => s.StepOrder))
        {
            var agent = step.ModelCodeNavigation;
            if (agent == null || !agent.IsActive)
            {
                _logger.LogWarning(
                    "Paso {StepOrder} del proceso {ProcessCode}: agente '{ModelCode}' no encontrado o inactivo.",
                    step.StepOrder, processCode, step.ModelCode);
                continue;
            }

            // Filtro de autorización por agente
            if (allowedAgentCodes.Count > 0 && !allowedAgentCodes.Contains(agent.Code))
            {
                _logger.LogWarning(
                    "Paso {StepOrder}: agente '{ModelCode}' no autorizado para el usuario.",
                    step.StepOrder, agent.Code);
                continue;
            }

            var config = agent.AgentConfig;
            if (config == null)
            {
                _logger.LogWarning(
                    "Paso {StepOrder}: agente '{ModelCode}' sin OPAIConfiguration.",
                    step.StepOrder, agent.Code);
                continue;
            }

            // Prompt del sistema
            var systemPrompt = ResolveSystemPrompt(agent, step);

            // Texto de usuario según SourceType del paso
            var userMessage = BuildUserMessage(
                step, ocrContext, previousResponses, classificationText);

            var aiRequest = new AiCompletionRequest
            {
                SystemPrompt  = systemPrompt,
                UserMessage   = userMessage,
                MaxTokens     = agent.MaxTokens ?? 4096,
                Temperature   = agent.Temperature,
                ThinkingMode  = agent.ThinkingMode
            };

            // ── Persistir StepExecution BEFORE (para obtener ExecutionId) ─
            var dataFileId = processCase.DataFile.FirstOrDefault()?.Id ?? 0;
            var execution  = new StepExecution
            {
                CaseCode       = caseCode,
                StepOrder      = step.StepOrder,
                DataFileId     = dataFileId,
                ModelCode      = agent.Code,
                RequestContent = userMessage,
                Status         = "Running",
                StartDate      = DateTime.UtcNow,
                EndpointUrl    = config.EndpointUrl
                // ApiKey NO se persiste para no exponer secretos en BD
            };
            _db.StepExecution.Add(execution);
            await _db.SaveChangesAsync(cancellationToken);

            AiCompletionResult? aiResult = null;
            var stepStatus               = "Completed";
            string responseText          = string.Empty;

            try
            {
                var completionService = _factory.Create(config);
                aiResult    = await completionService.CompleteAsync(aiRequest, cancellationToken);
                responseText = aiResult.Text;
            }
            catch (Exception ex)
            {
                stepStatus   = "Error";
                responseText = $"[Error en paso {step.StepOrder}: {ex.Message}]";
                _logger.LogWarning(ex, "Error ejecutando paso {StepOrder} del proceso {ProcessCode}.",
                    step.StepOrder, processCode);
            }

            // ── Actualizar StepExecution AFTER ────────────────────────────
            execution.ResponseContent = responseText;
            execution.Status          = stepStatus;
            execution.EndDate         = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            // ── Persistir Usage ───────────────────────────────────────────
            if (aiResult != null)
            {
                var usage = new Usage
                {
                    ExecutionId      = execution.ExecutionId,
                    PromptTokens     = aiResult.PromptTokens,
                    CompletionTokens = aiResult.CompletionTokens,
                    CreatedDate      = DateTime.UtcNow,
                    ThinkingTokens      = aiResult.ThinkingTokens,
                    CacheReadTokens     = aiResult.CacheReadTokens,
                    CacheCreationTokens = aiResult.CacheCreationTokens
                };
                _db.Usage.Add(usage);
                await _db.SaveChangesAsync(cancellationToken);
            }

            previousResponses.Add(responseText);

            stepResults.Add(new StepResult
            {
                StepOrder        = step.StepOrder,
                StepName         = step.StepName,
                ModelCode        = agent.Code,
                RequestText      = userMessage,
                ResponseText     = responseText,
                ExecutionId      = execution.ExecutionId,
                Status           = stepStatus,
                PromptTokens     = aiResult?.PromptTokens     ?? 0,
                CompletionTokens = aiResult?.CompletionTokens ?? 0
            });
        }

        return new OrchestrationResult
        {
            CaseCode           = caseCode,
            ProcessCode        = processCode,
            ClassificationText = classificationText,
            Steps              = stepResults,
            Success            = true
        };
    }

    // ── Clasificador ─────────────────────────────────────────────────────────

    private async Task<(string ProcessCode, string Text)?> RunClassifierAsync(
        ProcessCase processCase,
        string ocrContext,
        IReadOnlyCollection<string> allowedAgentCodes,
        CancellationToken ct)
    {
        // Buscar un agente clasificador: un AgentProcess activo para el proceso del caso
        var agentProcess = await _db.AgentProcesses
            .Include(ap => ap.Agent)
                .ThenInclude(a => a.AgentConfig)
            .Include(ap => ap.Agent)
                .ThenInclude(a => a.OPAIModelPrompt)
                    .ThenInclude(op => op.PromptCodeNavigation)
            .Where(ap =>
                ap.DefinitionCode == processCase.DefinitionCode
                && ap.Agent.IsActive
                && (allowedAgentCodes.Count == 0 || allowedAgentCodes.Contains(ap.AgentCode)))
            .FirstOrDefaultAsync(ct);

        if (agentProcess?.Agent == null)
            return null;

        var agent  = agentProcess.Agent;
        var config = agent.AgentConfig;
        if (config == null)
            return null;

        // Prompt del clasificador: usa el primer prompt del agente o el SystemPrompt del Agent
        var systemPrompt = ResolveSystemPromptForClassifier(agent, processCase.DefinitionCode);
        var userMessage  = BuildClassifierUserMessage(ocrContext, processCase.DefinitionCode);

        var aiRequest = new AiCompletionRequest
        {
            SystemPrompt = systemPrompt,
            UserMessage  = userMessage,
            MaxTokens    = agent.MaxTokens ?? 1024,
            Temperature  = agent.Temperature
        };

        try
        {
            var svc    = _factory.Create(config);
            var result = await svc.CompleteAsync(aiRequest, ct);
            return (processCase.DefinitionCode, result.Text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Clasificador falló para caso {CaseCode}.", processCase.CaseCode);
            // Fallback: usar el DefinitionCode del caso
            return (processCase.DefinitionCode, $"[Clasificación fallida — usando proceso por defecto: {processCase.DefinitionCode}]");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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

    private static string ResolveSystemPrompt(Agent agent, ProcessStep step)
    {
        // Prioridad: SystemPrompt del Agent (campo nuevo T1) → primer OPAIPrompt activo del agente
        if (!string.IsNullOrWhiteSpace(agent.SystemPrompt))
            return agent.SystemPrompt;

        var promptContent = agent.OPAIModelPrompt
            ?.OrderBy(p => p.Order)
            .Select(p => p.PromptCodeNavigation?.Content)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        if (!string.IsNullOrWhiteSpace(promptContent))
            return promptContent;

        return $"Eres un asistente IA especializado en análisis OCR. Ejecuta el paso: {step.StepName ?? step.StepOrder.ToString()}.";
    }

    private static string ResolveSystemPromptForClassifier(Agent agent, string processCode)
    {
        if (!string.IsNullOrWhiteSpace(agent.SystemPrompt))
            return agent.SystemPrompt;

        var promptContent = agent.OPAIModelPrompt
            ?.OrderBy(p => p.Order)
            .Select(p => p.PromptCodeNavigation?.Content)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        if (!string.IsNullOrWhiteSpace(promptContent))
            return promptContent;

        return $"Eres un clasificador de documentos OCR. Analiza el contenido y confirma el tipo de proceso '{processCode}'.";
    }

    private static string BuildClassifierUserMessage(string ocrContext, string processCode)
    {
        return $"Proceso a confirmar: {processCode}\n\nContenido OCR de los documentos:\n{ocrContext}";
    }

    private static string BuildUserMessage(
        ProcessStep step,
        string ocrContext,
        IReadOnlyList<string> previousResponses,
        string classificationText)
    {
        var sb = new StringBuilder();

        var includeOriginal = step.SourceType is InputSourceType.Original or InputSourceType.Both;
        var includePrevious = step.SourceType is InputSourceType.PreviousSteps or InputSourceType.Both;

        if (includeOriginal)
            sb.AppendLine("## Documentos OCR del caso").AppendLine(ocrContext);

        if (includePrevious && previousResponses.Count > 0)
        {
            sb.AppendLine("## Respuestas de pasos anteriores");
            var stepsToInclude = step.StepsToInclude > 0
                ? previousResponses.TakeLast(step.StepsToInclude)
                : previousResponses;

            foreach (var (resp, idx) in stepsToInclude.Select((r, i) => (r, i + 1)))
                sb.AppendLine($"### Paso {idx}").AppendLine(resp);
        }

        if (sb.Length == 0)
            sb.AppendLine("## Clasificación inicial").AppendLine(classificationText);

        return sb.ToString().Trim();
    }

    private async Task<IReadOnlyCollection<string>> GetAllowedAgentCodesAsync(
        IdentityUser? user,
        string processCode)
    {
        if (user == null)
            return Array.Empty<string>();

        // Mismo patrón que NexusService.GetProcessesByUser
        var allowedAgentProcessIds = await _db.PolicyUsers
            .Where(pu => pu.UserId == user.Id)
            .SelectMany(pu => pu.Policys.AccessAgentPolicies.Select(aap => aap.AgentProcessId))
            .Distinct()
            .ToListAsync();

        if (allowedAgentProcessIds.Count == 0)
            return Array.Empty<string>();

        var agentCodes = await _db.AgentProcesses
            .Where(ap =>
                ap.DefinitionCode == processCode
                && ap.Agent.IsActive
                && allowedAgentProcessIds.Contains(ap.Id))
            .Select(ap => ap.AgentCode)
            .Distinct()
            .ToListAsync();

        return agentCodes;
    }

    private static OrchestrationResult Failed(Guid caseCode, string error) =>
        new()
        {
            CaseCode      = caseCode,
            Success       = false,
            ErrorMessage  = error
        };
}
