using app_ocr_ai_models.Data;
using app_tramites.Extensions;
using app_tramites.Models.Dto;
using app_tramites.Models.External;
using app_tramites.Models.ModelAi;
using app_tramites.Models.ViewModel;
using app_tramites.Utils;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Usage = app_tramites.Models.ModelAi.Usage;

namespace app_tramites.Services.NexusProcess;

public class NexusService : INexusService
{

    private readonly OCRDbContext db;
    private readonly FileDownloader fileDownloader;

    // Init una sola vez (por instancia)
    private readonly object _initLock = new();
    private Task? _initTask;

    private OCRSetting? _ocrSetting;
    private AzureBlobConf? _blobCfg;

    private DocumentIntelligenceClient? _docClient;
    private BlobContainerClient? _containerClient;



    public NexusService(OCRDbContext db, FileDownloader fileDownloader)
    {
        this.db = db ?? throw new ArgumentNullException(nameof(db));
        this.fileDownloader = fileDownloader ?? throw new ArgumentNullException(nameof(fileDownloader));
    }

    public async Task<ResponsePromptDto> EjecutarPrompt(PromptRequest req)
    {
        // Cargar caso, archivos y definición
        var processCase = await db.ProcessCase
            .Include(pc => pc.DataFile)
            .Include(pc => pc.DefinitionCodeNavigation)
            .FirstOrDefaultAsync(pc => pc.CaseCode == req.CaseCode);
        if (processCase == null) return null!;

        var dataFiles = processCase.DataFile.ToList();
        var processDefinition = processCase.DefinitionCodeNavigation; //proceso

        if (!string.IsNullOrWhiteSpace(req.ProcessCode) &&
            (processDefinition == null || !string.Equals(processDefinition.Code, req.ProcessCode, StringComparison.OrdinalIgnoreCase)))
        {
            var requestedProcess = await db.Process
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Code == req.ProcessCode);

            if (requestedProcess != null)
            {
                processDefinition = requestedProcess;
            }
        }

        if (processDefinition == null) return null!;

        var agenteProceso = BuscarPromptPorAgenteProceso(req, processDefinition);
        if (agenteProceso == null || agenteProceso.Agent == null) return null!;

        var agentPrompt = agenteProceso.Agent.OPAIModelPrompt?
            .FirstOrDefault(op =>
                op.TypeAgentNavigation != null &&
                op.TypeAgentNavigation.Code == req.Origin)
            ?? agenteProceso.Agent.OPAIModelPrompt?.FirstOrDefault();
        var promptModel = agentPrompt?.PromptCodeNavigation;
        string prompt = promptModel?.Content ?? "";

        if (string.IsNullOrWhiteSpace(prompt) || agentPrompt == null)
            return null!;

        var metadata = !string.IsNullOrWhiteSpace(agentPrompt!.MetadataJson)
            ? JsonSerializer.Deserialize<FinalResponseMetadata>(agentPrompt.MetadataJson)!
            : new FinalResponseMetadata();
        if (!string.IsNullOrWhiteSpace(metadata.CustomInstructions))
            prompt += metadata.CustomInstructions;

        var transientFiles = new List<(string Url, string Text, string OriginalName)>();
        try
        {
            if (req.Files is { Count: > 0 })
            {
                await EnsureInitializedAsync();
                var ocrTasks = req.Files.Select(f =>
                    ProcessFileAsync(f, _ocrSetting!, _blobCfg!, _docClient!, timeoutMilliseconds: 90000));

                transientFiles = [.. await Task.WhenAll(ocrTasks)];
            }

            var userContent = req.Message ?? "";
            if (req.FileUrls != null && req.FileUrls.Count > 0)
            {
                userContent += "\n\nArchivos remitidos por el cliente:\n" + string.Join("\n", req.FileUrls);
            }

            if (transientFiles.Count > 0)
            {
                var attachmentLabel = transientFiles.Count == 1 ? "1 adjunto temporal" : $"{transientFiles.Count} adjuntos temporales";
                userContent += $"\n\n{attachmentLabel} enviados en esta interacción.";
            }

            var combined = new StringBuilder();
            foreach (var df in dataFiles)
            {
                combined.AppendLine($"documento: {df.OriginalName}---{df.Text}---");
            }

            foreach (var df in transientFiles)
            {
                combined.AppendLine($"documento temporal: {df.OriginalName}---{df.Text}---");
            }

            var context = string.Empty;
            if (req.Origin.Equals(ConstanteTipoAgente.Chat, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(req.Message))
            {
                context = userContent + $"\n\nInformación del Caso número NE-{(processCase.CaseCode.ToString()?.Split('-').FirstOrDefault() ?? "")}: Usuario que consulta: {req.Usuario}\n" +
                    combined.ToString();
            }
            else
            {
                context = combined.ToString();
            }

            var finalResp = await CallOpenAiAsync(
                agenteProceso.Agent!,
                prompt,
                userText: context,
                dataFiles.First().Id,
                stepOrder: 999,
                maxTokens: metadata.MaxTokens ?? 100000,
                temperature: metadata.Temperature ?? 0.2,
                topP: 1.0);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = null
            };

            var requestText = string.IsNullOrWhiteSpace(req.Message) ? agenteProceso.Agent.Description ?? string.Empty : req.Message.Trim();
            if (transientFiles.Count > 0)
            {
                var attachmentText = transientFiles.Count == 1
                    ? "1 adjunto temporal"
                    : $"{transientFiles.Count} adjuntos temporales";
                requestText = string.IsNullOrWhiteSpace(requestText)
                    ? $"Adjuntos temporales: {attachmentText}"
                    : $"{requestText}\nAdjuntos temporales: {attachmentText}";
            }

            if (!string.IsNullOrWhiteSpace(req.ProcessCode) &&
                !string.Equals(processCase.DefinitionCode, processDefinition.Code, StringComparison.OrdinalIgnoreCase))
            {
                var processLabel = !string.IsNullOrWhiteSpace(processDefinition.Name)
                    ? processDefinition.Name
                    : processDefinition.Code;

                requestText = string.IsNullOrWhiteSpace(requestText)
                    ? $"Procesar con {processLabel}"
                    : $"Procesar con {processLabel}\n\n{requestText}";
            }

            var final = new FinalResponseResult
            {
                CaseCode = req.CaseCode,
                ResponseText = finalResp.ResultText,
                CreatedDate = DateTime.Now,
                RequestText = requestText,
                MetadataJson = JsonSerializer.Serialize(metadata, options),
                AgentProccessId = agenteProceso.Id
            };

            db.FinalResponseResult.Add(final);
            await db.SaveChangesAsync();

            Usage usage = new()
            {
                PromptTokens = finalResp.PromptTokens,
                CompletionTokens = finalResp.CompletionTokens,
                CreatedDate = DateTime.Now,
                FinalResponseResultId = final.Id
            };
            db.Usage.Add(usage);
            await db.SaveChangesAsync();

            ResponsePromptDto result = new()
            {
                CaseCode = req.CaseCode,
                ResponseText = final.ResponseText,
                MetadataJson = final.MetadataJson,
                ProccessName = processDefinition.Name ?? "",
                AgentName = agenteProceso.Agent.Name ?? "",
                RequestText = final.RequestText ?? "",
                CreatedDate = final.CreatedDate
            };

            return result;
        }
        finally
        {
            await DeleteTransientFilesAsync(transientFiles);
        }
    }


    public AgentProcess? BuscarPromptPorAgenteProceso(PromptRequest req, Process process)
    {

        var query = db.AgentProcesses
            .Include(ap => ap.Agent)
                .ThenInclude(a => a.OPAIModelPrompt)
                .ThenInclude(op => op.PromptCodeNavigation)
            .Include(ap => ap.Agent)
                .ThenInclude(a => a.OPAIModelPrompt)
                .ThenInclude(op => op.TypeAgentNavigation)
            .Include(ap => ap.Agent.AgentConfig)
            .Where(ap => ap.DefinitionCode == process.Code && ap.Agent.IsActive)
            .AsQueryable();

        if (req?.Id > 0)
        {
            var selectedProcess = query.FirstOrDefault(ap => ap.Id == req.Id);
            if (selectedProcess != null)
            {
                return selectedProcess;
            }

            query = query.Where(ap => ap.Id == req.Id);
        }

        var data = query.FirstOrDefault(ap =>
            ap.Agent.OPAIModelPrompt.Any(op =>
                op.TypeAgentNavigation != null &&
                op.TypeAgentNavigation.Code == req!.Origin));

        if (data != null)
        {
            return data;
        }

        return query.FirstOrDefault();
    }

    public async Task<OpenAiResponseDto> CallOpenAiAsync(
            Agent agent,
            string systemContent,
            string userText,
            int dataFileId,
            int stepOrder,
            int maxTokens = 1000,
            double temperature = 0.2,
            double topP = 1.0)
    {
        var messages = new[]
        {
                new { role = "system", content = systemContent },
                new { role = "user",   content = userText     }
            };

        var requestBody = new
        {
            messages,
            max_tokens = maxTokens,
            temperature,
            top_p = topP
        };

        var requestJson = JsonSerializer.Serialize(requestBody);
        var startedAt = DateTime.Now;

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("api-key", agent.AgentConfig.ApiKey);
        var response = await httpClient.PostAsync(
            agent.AgentConfig.EndpointUrl,
            new StringContent(requestJson, Encoding.UTF8, "application/json"));

        var resultJson = await response.Content.ReadAsStringAsync();
        var ai = JsonSerializer.Deserialize<ResultOpenAi>(resultJson);
        var rawText = ai?.choices?.FirstOrDefault()?.message?.content ?? "";
        var matchResult = Regex.Match(rawText, @"```(?:\w*\n)?(.*?)```", RegexOptions.Singleline);
        var cleaned = matchResult.Success
            ? matchResult.Groups[1].Value.Trim()
            : rawText.Trim();

        var finishedAt = DateTime.Now;

        return new OpenAiResponseDto
        {
            DataFileId = dataFileId,
            StepOrder = stepOrder,
            RequestJson = requestJson,
            ResultText = cleaned,
            PromptTokens = ai?.usage?.prompt_tokens ?? 0,
            CompletionTokens = ai?.usage?.completion_tokens ?? 0,
            StartedAt = startedAt,
            FinishedAt = finishedAt
        };
    }

    public async Task<ProcessCase?> ObtenerProcessCase(Guid caseCode)
    {
        return await db.ProcessCase
            .Include(pc => pc.FinalResponseResults).Include(pc => pc.DataFile)
            .FirstOrDefaultAsync(pc => pc.CaseCode == caseCode);
    }

    private static List<AgentProccessButton> GetAgentButtons(List<AgentTypeDto> agentsConfig)
    {
        List<AgentProccessButton> buttons = [];
        var config = agentsConfig.Where(ac => ac.Code.Equals(ConstanteTipoAgente.Botones, StringComparison.OrdinalIgnoreCase))
            .SelectMany(c => c.OPAIModelPrompt)
            .ToList();
        foreach (var button in config)
        {
            var buttonConfig = new AgentProccessButton
            {
                ButtonId = button.ButtonId,
                ClassName = button.ClassName,
                Tittle = button.Tittle,
                Icon = button.Icon,
                IconMenu = button.IconMenu,
                AriaLabel = button.AriaLabel,
                Name = button.NameButton,
                AgentProcessId = button.ModelCodeNavigation?.AgentProcesses
                    .FirstOrDefault(ap => ap.AgentCode == button.ModelCode)?.Id,
            };
            buttons.Add(buttonConfig);
        }

        return buttons;
    }

    public async Task<ViewCaseDetails?> ObtenerDetailsProcessCase(Guid caseCode, IdentityUser? user)
    {
        var proccess = await db.ProcessCase
            .Include(pc => pc.FinalResponseResults).Include(pc => pc.DataFile)
            .FirstOrDefaultAsync(pc => pc.CaseCode == caseCode) ?? throw new NegocioException("No hay información que mostrar");
        var agents = await GetAgentTypesForUserAndProcessAsync(user, proccess.DefinitionCode);
        bool hasChat = false;
        bool hasButton = false;
        List<AgentProccessButton> buttons = [];

        if (agents is not null && agents.Count > 0)
        {
            hasChat = agents.Any(c => c.Code.Equals(ConstanteTipoAgente.Chat, StringComparison.OrdinalIgnoreCase));
            hasButton = agents.Any(c => c.Code.Equals(ConstanteTipoAgente.Botones, StringComparison.OrdinalIgnoreCase));
            if (hasButton)
            {
                buttons = GetAgentButtons(agents);
            }
        }

        var details = new ViewCaseDetails
        {
            HasChat = hasChat,
            HasButton = hasButton,
            ProcessCase = proccess!,
            AgentProccessButtons = buttons
        };

        return details;
    }

    public async Task<ViewPagedProcessCases> ObtenerProcesos(
        IdentityUser? user,
        IList<string>? roles,
        int pageNumber = 1,
        int pageSize = 10,
        int windowDays = 0,
        string? search = null,
        string? status = null,
        string? type = null,
        string? process = null,
        string? period = null)
    {
        var procesosUsuario = await GetProcessesByUser(user, roles);
        pageSize = Math.Clamp(pageSize, 6, 30);
        var searchTerm = (search ?? string.Empty).Trim();
        var statusFilter = (status ?? string.Empty).Trim().ToLowerInvariant();
        var typeFilter = (type ?? string.Empty).Trim().ToLowerInvariant();
        var processFilter = (process ?? string.Empty).Trim();
        var periodFilter = (period ?? string.Empty).Trim().ToLowerInvariant();
        var availableProcesses = procesosUsuario.Processes
            .Select(p => p.ProcessName?.Trim())
            .OfType<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();

        if (procesosUsuario.Processes.Count == 0)
            return new ViewPagedProcessCases
            {
                AvailableProcesses = availableProcesses,
                PageNumber = 1,
                PageSize = pageSize,
                TotalPages = 1,
                WindowDays = Math.Max(windowDays, 0),
                SearchTerm = searchTerm,
                StatusFilter = statusFilter,
                TypeFilter = typeFilter,
                ProcessFilter = processFilter,
                PeriodFilter = periodFilter
            };

        var codigos = procesosUsuario.Processes
            .Where(p => !string.IsNullOrWhiteSpace(p.ProcessId))
            .Select(p => p.ProcessId!)
            .ToList();

        var baseQuery = db.ProcessCase
            .AsNoTracking()
            .Where(x => codigos.Contains(x.DefinitionCode));

        if (windowDays > 0)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-windowDays);
            baseQuery = baseQuery.Where(x => x.StartDate > cutoffDate);
        }

        if (!string.IsNullOrWhiteSpace(processFilter))
        {
            baseQuery = baseQuery.Where(x =>
                x.DefinitionCodeNavigation != null &&
                x.DefinitionCodeNavigation.Name != null &&
                x.DefinitionCodeNavigation.Name == processFilter);
        }

        if (!string.IsNullOrWhiteSpace(periodFilter))
        {
            var utcNow = DateTime.UtcNow;
            if (periodFilter == "today")
            {
                var startOfToday = utcNow.Date;
                baseQuery = baseQuery.Where(x => x.StartDate >= startOfToday);
            }
            else if (periodFilter == "48h")
            {
                var cutoff48h = utcNow.AddHours(-48);
                baseQuery = baseQuery.Where(x => x.StartDate >= cutoff48h);
            }
            else if (periodFilter == "7d")
            {
                var cutoff7d = utcNow.AddDays(-7);
                baseQuery = baseQuery.Where(x => x.StartDate >= cutoff7d);
            }
        }

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            if (statusFilter == "evaluado")
            {
                baseQuery = baseQuery.Where(x => x.FinalResponseResults.Any());
            }
            else if (statusFilter == "pendiente")
            {
                baseQuery = baseQuery.Where(x => !x.FinalResponseResults.Any());
            }
            else if (statusFilter == "error")
            {
                baseQuery = baseQuery.Where(x =>
                    x.FinalResponseResults.Any(r =>
                        (r.ResponseText ?? string.Empty).ToLower().Contains("error")));
            }
        }

        if (!string.IsNullOrWhiteSpace(typeFilter))
        {
            if (typeFilter == "ambulatorio")
            {
                baseQuery = baseQuery.Where(x =>
                    x.FinalResponseResults.Any(r =>
                        (r.ResponseText ?? string.Empty).ToLower().Contains("ambulatorio")));
            }
            else if (typeFilter == "hospitalario")
            {
                baseQuery = baseQuery.Where(x =>
                    x.FinalResponseResults.Any(r =>
                        (r.ResponseText ?? string.Empty).ToLower().Contains("hospitalario")));
            }
            else if (typeFilter == "hospital del dia")
            {
                baseQuery = baseQuery.Where(x =>
                    x.FinalResponseResults.Any(r =>
                        (r.ResponseText ?? string.Empty).ToLower().Contains("hospital del dia") ||
                        (r.ResponseText ?? string.Empty).ToLower().Contains("hospital del d")));
            }
            else if (typeFilter == "no evaluado")
            {
                baseQuery = baseQuery.Where(x => !x.FinalResponseResults.Any());
            }
            else if (typeFilter == "no definido")
            {
                baseQuery = baseQuery.Where(x =>
                    x.FinalResponseResults.Any() &&
                    !x.FinalResponseResults.Any(r =>
                        (r.ResponseText ?? string.Empty).ToLower().Contains("ambulatorio") ||
                        (r.ResponseText ?? string.Empty).ToLower().Contains("hospitalario") ||
                        (r.ResponseText ?? string.Empty).ToLower().Contains("hospital del dia") ||
                        (r.ResponseText ?? string.Empty).ToLower().Contains("hospital del d")));
            }
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var searchPattern = $"%{searchTerm}%";
            var searchCaseCode = searchTerm.StartsWith("NE-", StringComparison.OrdinalIgnoreCase)
                ? searchTerm[3..]
                : searchTerm;
            var searchCasePattern = $"%{searchCaseCode}%";
            var searchLower = searchTerm.ToLowerInvariant();
            var matchesEvaluated = searchLower.Contains("evaluad");
            var matchesPending = searchLower.Contains("pendient");
            var matchesAmbulatory = searchLower.Contains("ambulatorio");
            var matchesHospital = searchLower.Contains("hospitalario");
            var matchesDayHospital = searchLower.Contains("hospital del dia") || searchLower.Contains("hospital del d");
            var matchesUndefined = searchLower.Contains("no definido");

            baseQuery = baseQuery.Where(x =>
                EF.Functions.Like(x.CaseCode.ToString(), searchCasePattern) ||
                EF.Functions.Like(x.DefinitionCode, searchPattern) ||
                (x.DefinitionCodeNavigation != null &&
                 x.DefinitionCodeNavigation.Name != null &&
                 EF.Functions.Like(x.DefinitionCodeNavigation.Name, searchPattern)) ||
                (matchesEvaluated && x.FinalResponseResults.Any()) ||
                (matchesPending && !x.FinalResponseResults.Any()) ||
                (matchesAmbulatory && x.FinalResponseResults.Any(r => (r.ResponseText ?? string.Empty).ToLower().Contains("ambulatorio"))) ||
                (matchesHospital && x.FinalResponseResults.Any(r => (r.ResponseText ?? string.Empty).ToLower().Contains("hospitalario"))) ||
                (matchesDayHospital && x.FinalResponseResults.Any(r =>
                    (r.ResponseText ?? string.Empty).ToLower().Contains("hospital del dia") ||
                    (r.ResponseText ?? string.Empty).ToLower().Contains("hospital del d"))) ||
                (matchesUndefined && x.FinalResponseResults.Any() && !x.FinalResponseResults.Any(r =>
                    (r.ResponseText ?? string.Empty).ToLower().Contains("ambulatorio") ||
                    (r.ResponseText ?? string.Empty).ToLower().Contains("hospitalario") ||
                    (r.ResponseText ?? string.Empty).ToLower().Contains("hospital del dia") ||
                    (r.ResponseText ?? string.Empty).ToLower().Contains("hospital del d"))));
        }

        var totalCount = await baseQuery.CountAsync();
        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);
        pageNumber = Math.Clamp(pageNumber, 1, totalPages);

        var evaluatedCount = await baseQuery.CountAsync(x => x.FinalResponseResults.Any());
        var processCount = procesosUsuario.Processes.Count;

        var items = await baseQuery
            .Include(x => x.FinalResponseResults)
            .Include(x => x.DefinitionCodeNavigation)
            .OrderByDescending(pc => pc.StartDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new ViewPagedProcessCases
        {
            Items = items,
            AvailableProcesses = availableProcesses,
            TotalCount = totalCount,
            EvaluatedCount = evaluatedCount,
            PendingCount = Math.Max(0, totalCount - evaluatedCount),
            ProcessCount = processCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalPages = totalPages,
            WindowDays = windowDays,
            SearchTerm = searchTerm,
            StatusFilter = statusFilter,
            TypeFilter = typeFilter,
            ProcessFilter = processFilter,
            PeriodFilter = periodFilter
        };
    }

    private async Task<List<AgentTypeDto>> GetAgentTypesForUserAndProcessAsync(IdentityUser? user, string processCode)
    {
        if (user == null || string.IsNullOrWhiteSpace(processCode))
            return [];

        // Obtener los AgentProcessId a los que el usuario tiene acceso via PolicyUser -> Policys -> AccessAgentPolicies
        var allowedAgentProcessIds = await db.PolicyUsers
            .Where(pu => pu.UserId == user.Id)
            .SelectMany(pu => pu.Policys.AccessAgentPolicies.Select(aap => aap.AgentProcessId))
            .Distinct()
            .ToListAsync();

        if (allowedAgentProcessIds.Count == 0)
        {
            // Si no tiene policies, devolver vacío (puedes cambiar la lógica para incluir rol/otros accesos)
            return [];
        }

        // Consultar AgentProcesses filtrando por processCode y Agent.IsActive, incluyendo las colecciones necesarias
        var agentProcesses = await db.AgentProcesses
            .Include(ap => ap.Agent)
                .ThenInclude(a => a.OPAIModelPrompt)
                .ThenInclude(op => op.TypeAgentNavigation)
            .Where(ap => ap.DefinitionCode == processCode && ap.Agent.IsActive && allowedAgentProcessIds.Contains(ap.Id))
            .ToListAsync();

        // Mapear a AgentTypeDto: por cada AgentProcess y cada prompt tomar el Catalog (TypeAgentNavigation)
        var result = agentProcesses
            .SelectMany(ap => (ap.Agent.OPAIModelPrompt ?? Enumerable.Empty<OPAIModelPrompt>())
                .Select(op => op.TypeAgentNavigation)
                .Where(cat => cat != null)
                .Select(cat => new AgentTypeDto
                {
                    Code = cat.Code,
                    CatalogId = cat.Id,
                    DefinitionCode = ap.DefinitionCode,
                    AgentCode = ap.AgentCode,
                    OPAIModelPrompt = ap.Agent.OPAIModelPrompt?.ToList()!,
                }))
            .DistinctBy(d => (d.CatalogId, d.AgentCode, d.DefinitionCode))
            .ToList();

        return result;
    }

    public async Task<ViewProcessUser> GetProcessesByUser(IdentityUser? user, IList<string>? roles)
    {

        // Consultar las políticas asociadas al usuario
        var userPolicies = await db.PolicyUsers
            .Include(pu => pu.Policys)
                .ThenInclude(p => p.AccessAgentPolicies)
                .ThenInclude(aap => aap.AgentProcess)
                .ThenInclude(ap => ap!.Process)
            .Where(pu => pu.UserId == user!.Id)
            .ToListAsync();

        // Consultar los procesos relacionados con las políticas del usuario
        var processesFromPolicies = userPolicies
           .SelectMany(pu => pu.Policys.AccessAgentPolicies)
           .Select(aap => new
           {
               aap.AgentProcess?.Process,
               ProcessAgentId = (int?)aap.AgentProcess?.Id // Obtener el ProcessAgentId
           })
           .ToList();

        // Consultar los procesos relacionados con los roles del usuario
        var processesFromRoles = await db.RolProcesses
            .Include(rp => rp.Process)
            .Include(rp => rp.Rol)
            .Where(rp => roles!.Contains(rp.Rol.Name)) // Filtrar por roles del usuario
            .Select(rp => new
            {
                rp.Process,
                ProcessAgentId = (int?)null // No hay un ProcessAgentId en esta consulta
            })
            .ToListAsync();

        List<ViewAgentProcess> allProcesses = [.. processesFromPolicies
            .Concat(processesFromRoles)
            .Where(p => p.Process != null && !string.IsNullOrWhiteSpace(p.Process.Code))
            .GroupBy(p => (p.Process?.Code ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                // Priorizar entradas con ProcessAgentId (vienen de policies), pero permitir procesos visibles solo por rol.
                var chosen = g
                    .OrderByDescending(x => x.ProcessAgentId.HasValue)
                    .ThenBy(x => x.Process!.Name ?? "")
                    .First();

                return new ViewAgentProcess { ProcessId = chosen.Process!.Code, ProcessName = chosen.Process.Name,  Description = chosen.Process.Description,ProcessAgentId = chosen.ProcessAgentId };
            })];


        return new ViewProcessUser { Success = true, Processes = allProcesses };
    }

    // =========================
    // INIT: una sola vez
    // =========================
    private async Task EnsureInitializedAsync()
    {
        if (_initTask != null)
        {
            await _initTask;
            return;
        }

        lock (_initLock)
        {
            _initTask ??= InitOnceAsync();
        }

        await _initTask;
    }

    private async Task InitOnceAsync()
    {
        // 1) Settings 1 vez
        _ocrSetting = await db.OCRSetting.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SettingCode == "DEFAULT" && x.PlatformCode == "AZURE")
            ?? throw new NegocioException("Configuración OCR no encontrada.");

        _blobCfg = await db.AzureBlobConf.AsNoTracking()
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("AzureBlobConf no encontrada.");

        // 2) Client OCR 1 vez
        _docClient = new DocumentIntelligenceClient(
            new Uri(_ocrSetting.Endpoint),
            new AzureKeyCredential(_ocrSetting.ApiKey!));

        // 3) Blob clients 1 vez
        var blobServiceClient = new BlobServiceClient(_blobCfg.ConnectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(_blobCfg.ContainerName);

        // 4) Contenedor 1 vez
        //await CreateContainerIfNeededAsync(_containerClient);
    }

    private static async Task CreateContainerIfNeededAsync(BlobContainerClient containerClient)
    {
        try
        {
            await containerClient.CreateIfNotExistsAsync();
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // carrera / contención: OK
        }
    }

    public async Task<ViewCreateCase> CreateCaseProcess(QueryInput input)
    {
        if (input == null || string.IsNullOrWhiteSpace(input.ProcessCode))
            throw new NegocioException("ProcessCode es obligatorio.");

        if (input.Files == null || input.Files.Count == 0)
            throw new NegocioException("Debe enviar al menos un archivo.");

        await EnsureInitializedAsync();

        var ocrSetting = _ocrSetting!;
        var blobCfg = _blobCfg!; 
        var clientOcr = _docClient!;

        var ocrTasks = input.Files.Select(f =>
            ProcessFileAsync(f, ocrSetting, blobCfg, clientOcr, timeoutMilliseconds: 90000)
        );

        var ocrResults = await Task.WhenAll(ocrTasks);

        var processInfo =  db.Process
            .AsNoTracking()
            .Where(p => p.Code == input.ProcessCode)
            .Select(p => new { p.Code, p.Name })
            .FirstOrDefault()
            ?? throw new NegocioException("Proceso no encontrado.");

        var now = DateTime.UtcNow;
        var caseCode = Guid.NewGuid();

        var processCase = new ProcessCase
        {
            CaseCode = caseCode,
            DefinitionCode = processInfo.Code,
            StartDate = now,
            State = "Started"
        };

        var dataFiles = ocrResults.Select(r => new DataFile
        {
            CaseCode = caseCode,
            IsFileUri = !string.IsNullOrEmpty(r.Url),
            FileUri = r.Url,
            Text = r.Text,
            CreatedDate = now,
            OriginalName = r.OriginalName
        }).ToList();

        processCase.DataFile = dataFiles;
        db.ProcessCase.Add(processCase);
        await db.SaveChangesAsync();

        return new ViewCreateCase
        {
            CaseCode = processCase.CaseCode,
            DefinitionCode = processCase.DefinitionCode,
            StartDate = processCase.StartDate,
            State = processCase.State,
            NameProccess = processInfo.Name ?? ""
        };
    }

    public async Task<List<DataFile>> AddDocumentsToCase(Guid caseCode, IReadOnlyCollection<OcrFile> files)
    {
        if (caseCode == Guid.Empty)
            throw new NegocioException("CaseCode es obligatorio.");

        if (files == null || files.Count == 0)
            throw new NegocioException("Debe enviar al menos un archivo.");

        var processCase = await db.ProcessCase
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CaseCode == caseCode)
            ?? throw new NegocioException("Caso no encontrado.");

        await EnsureInitializedAsync();

        var ocrTasks = files.Select(f =>
            ProcessFileAsync(f, _ocrSetting!, _blobCfg!, _docClient!, timeoutMilliseconds: 90000));

        var ocrResults = await Task.WhenAll(ocrTasks);
        var now = DateTime.UtcNow;

        var newFiles = ocrResults.Select(result => new DataFile
        {
            CaseCode = processCase.CaseCode,
            IsFileUri = !string.IsNullOrEmpty(result.Url),
            FileUri = result.Url,
            Text = result.Text,
            CreatedDate = now,
            OriginalName = result.OriginalName
        }).ToList();

        db.DataFile.AddRange(newFiles);
        await db.SaveChangesAsync();

        return newFiles;
    }

    private async Task<(string Url, string Text, string OriginalName)> ProcessFileAsync(
      OcrFile file,
      OCRSetting ocrSetting,
      AzureBlobConf blobCfg,
      DocumentIntelligenceClient clientOcr,
      int timeoutMilliseconds = 90000)
    {
        using var cts = new CancellationTokenSource(timeoutMilliseconds);

        try
        {
            var blobUrl = await UploadBlobAsync(file, blobCfg, timeoutMilliseconds: 60000);
            var originalName = ResolveOriginalName(file);

            if (ShouldSkipOcr(file))
            {
                var directText = await ReadDirectTextAsync(file, cts.Token);
                await SaveArtifactBestEffortAsync(
                    blobUrl,
                    BuildDirectArtifact(originalName, blobUrl, directText, GetNormalizedExtension(file)),
                    cts.Token);
                return (Url: blobUrl, Text: directText, OriginalName: originalName);
            }

            var operation = await clientOcr.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                ocrSetting.ModelId,
                new Uri(blobUrl),
                cancellationToken: cts.Token);

            await SaveArtifactBestEffortAsync(
                blobUrl,
                BuildAnalyzeArtifact(originalName, blobUrl, ocrSetting.ModelId ?? string.Empty, operation.Value),
                cts.Token);

            return (Url: blobUrl, Text: operation.Value.Content ?? "", OriginalName: originalName);
        }
        catch (TaskCanceledException)
        {
            throw new TimeoutException($"El procesamiento del archivo superó el tiempo límite de {timeoutMilliseconds} ms.");
        }
    }

    private async Task<(string Url, string Text, string OriginalName)> ProcessFileAsync(
     OcrFile file,
     OCRSetting ocrSetting,
     AzureBlobConf blobCfg,
     int timeoutMilliseconds = 90000) // 30 segundos por defecto
    {
        var clientOcr = new DocumentIntelligenceClient(
            new Uri(ocrSetting.Endpoint),
            new AzureKeyCredential(ocrSetting.ApiKey!));

        return await ProcessFileAsync(file, ocrSetting, blobCfg, clientOcr, timeoutMilliseconds);
    }

    public async Task<OcrDocumentArtifactDto?> ObtenerArtefactoDocumentoAsync(string fileUrl)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            return null;

        await EnsureInitializedAsync();

        if (_containerClient == null)
            return null;

        var artifactBlobName = BuildArtifactBlobName(fileUrl);
        if (string.IsNullOrWhiteSpace(artifactBlobName))
            return null;

        var blobClient = _containerClient.GetBlobClient(artifactBlobName);
        var exists = await blobClient.ExistsAsync();
        if (!exists.Value)
            return null;

        var download = await blobClient.DownloadContentAsync();
        return JsonSerializer.Deserialize<OcrDocumentArtifactDto>(download.Value.Content.ToString(), BuildArtifactJsonOptions());
    }

   

    private static async Task WaitForCopyToCompleteAsync(
        BlobClient blobClient,
        int timeoutMs,
        CancellationToken ct)
    {
        var started = DateTime.UtcNow;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var props = await blobClient.GetPropertiesAsync(cancellationToken: ct);
            var status = props.Value.CopyStatus;

            if (status == CopyStatus.Success)
                return;

            if (status is CopyStatus.Failed or CopyStatus.Aborted)
                throw new InvalidOperationException(
                    $"CopyFromUri falló. Status={status}. Description={props.Value.CopyStatusDescription}");

            if ((DateTime.UtcNow - started).TotalMilliseconds > timeoutMs)
                throw new TimeoutException($"CopyFromUri excedió {timeoutMs} ms.");

            await Task.Delay(250, ct);
        }
    }

    private async Task<string> UploadBlobAsync(
    OcrFile file,
    AzureBlobConf blobCfg,
    int timeoutMilliseconds = 60000)
    {
        if (file == null) throw new ArgumentNullException(nameof(file));
        if (blobCfg == null) throw new ArgumentNullException(nameof(blobCfg));

        if (string.IsNullOrWhiteSpace(blobCfg.ConnectionString))
            throw new InvalidOperationException("AzureBlobConf.ConnectionString vacío.");

        if (string.IsNullOrWhiteSpace(blobCfg.ContainerName))
            throw new InvalidOperationException("AzureBlobConf.ContainerName vacío.");

        using var cts = new CancellationTokenSource(timeoutMilliseconds);

        var blobServiceClient = new BlobServiceClient(blobCfg.ConnectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(blobCfg.ContainerName);
        var ext = (file.Extension ?? "").Trim();
        if (!string.IsNullOrEmpty(ext) && !ext.StartsWith(".")) ext = "." + ext;

        var blobName = $"{Guid.NewGuid()}{ext}";
        var blobClient = containerClient.GetBlobClient(blobName);
        var uploadOptions = BuildBlobUploadOptions(ext);

        if (!string.IsNullOrWhiteSpace(file.Content))
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(file.Content);
            }
            catch (FormatException)
            {
                throw new NegocioException("Content no es Base64 válido.");
            }

            await using var ms = new MemoryStream(bytes, writable: false);

            await ExecuteWithRetryAsync(async () =>
            {
                ms.Position = 0;
                await blobClient.UploadAsync(ms, uploadOptions, cancellationToken: cts.Token);
            }, cts.Token);

            return blobClient.Uri.ToString();
        }
        if (!string.IsNullOrWhiteSpace(file.Url))
        {
            await using var remoteStream = await fileDownloader.DownloadUrlToMemoryStreamAsync(file.Url);

            await ExecuteWithRetryAsync(async () =>
            {
                remoteStream.Position = 0;
                await blobClient.UploadAsync(remoteStream, uploadOptions, cancellationToken: cts.Token);
            }, cts.Token);

            return blobClient.Uri.ToString();
        }

        throw new NegocioException("Archivo sin Content ni Url.");
    }

    private async Task ExecuteWithRetryAsync(
        Func<Task> action,
        CancellationToken cancellationToken,
        int maxAttempts = 3,
        int initialDelayMs = 300)
    {
        int delayMs = initialDelayMs;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (Azure.RequestFailedException ex) when (
                attempt < maxAttempts &&
                IsTransientStatus(ex.Status))
            {
                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2; // Exponential backoff
            }
        }
    }

    private static bool IsTransientStatus(int statusCode)
    {
        return statusCode == 408 || // Timeout
               statusCode == 429 || // Too Many Requests
               statusCode == 500 ||
               statusCode == 502 ||
               statusCode == 503 ||
               statusCode == 504;
    }

    private async Task DeleteTransientFilesAsync(IEnumerable<(string Url, string Text, string OriginalName)> transientFiles)
    {
        if (_blobCfg == null)
            return;

        foreach (var file in transientFiles)
        {
            if (string.IsNullOrWhiteSpace(file.Url))
                continue;

            try
            {
                await DeleteBlobIfExistsAsync(file.Url, _blobCfg);
            }
            catch
            {
                // Si el adjunto temporal no se puede limpiar, no rompemos la respuesta al usuario.
            }
        }
    }

    private static async Task DeleteBlobIfExistsAsync(string blobUrl, AzureBlobConf blobCfg)
    {
        if (string.IsNullOrWhiteSpace(blobUrl) || string.IsNullOrWhiteSpace(blobCfg.ConnectionString))
            return;

        var uri = new Uri(blobUrl);
        var path = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(path))
            return;

        var separatorIndex = path.IndexOf('/');
        if (separatorIndex <= 0 || separatorIndex == path.Length - 1)
            return;

        var containerName = path[..separatorIndex];
        var blobName = Uri.UnescapeDataString(path[(separatorIndex + 1)..]);
        if (!containerName.Equals(blobCfg.ContainerName, StringComparison.OrdinalIgnoreCase))
            return;

        var blobServiceClient = new BlobServiceClient(blobCfg.ConnectionString);
        var blobClient = blobServiceClient
            .GetBlobContainerClient(containerName)
            .GetBlobClient(blobName);

        await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots);
    }

    private async Task<string> UploadBlobAsync(
            OcrFile file,
            AzureBlobConf blobCfg)
    {
        var blobServiceClient = new BlobServiceClient(blobCfg.ConnectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(blobCfg.ContainerName);
        await containerClient.CreateIfNotExistsAsync();

        var blobName = $"{Guid.NewGuid()}{file.Extension}";
        var blobClient = containerClient.GetBlobClient(blobName);
        var uploadOptions = BuildBlobUploadOptions(GetNormalizedExtension(file));

        if (string.IsNullOrEmpty(file.Content))
        {
            blobClient.StartCopyFromUri(new Uri(file.Url));
            await using var remoteStream = await fileDownloader.DownloadUrlToMemoryStreamAsync(file.Url);
            await blobClient.UploadAsync(remoteStream, uploadOptions);
        }
        else
        {
            await using var ms = new MemoryStream(Convert.FromBase64String(file.Content));
            await blobClient.UploadAsync(ms, uploadOptions);
        }

        return blobClient.Uri.ToString();
    }

    private static bool ShouldSkipOcr(OcrFile file)
    {
        var extension = GetNormalizedExtension(file);
        return extension is ".xml" or ".html" or ".htm";
    }

    private static string GetNormalizedExtension(OcrFile file)
    {
        var extension = (file.Extension ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(extension) && !string.IsNullOrWhiteSpace(file.FileName))
            extension = Path.GetExtension(file.FileName);

        if (string.IsNullOrWhiteSpace(extension) && !string.IsNullOrWhiteSpace(file.Url))
        {
            try
            {
                extension = Path.GetExtension(new Uri(file.Url).AbsolutePath);
            }
            catch
            {
                extension = Path.GetExtension(file.Url);
            }
        }

        if (!string.IsNullOrWhiteSpace(extension) && !extension.StartsWith(".", StringComparison.Ordinal))
            extension = "." + extension;

        return extension.ToLowerInvariant();
    }

    private static string ResolveOriginalName(OcrFile file)
    {
        if (!string.IsNullOrWhiteSpace(file.FileName))
            return file.FileName;

        if (!string.IsNullOrWhiteSpace(file.Url))
        {
            try
            {
                var nameFromUri = Path.GetFileName(new Uri(file.Url).AbsolutePath);
                if (!string.IsNullOrWhiteSpace(nameFromUri))
                    return Uri.UnescapeDataString(nameFromUri);
            }
            catch
            {
                var fallbackName = Path.GetFileName(file.Url);
                if (!string.IsNullOrWhiteSpace(fallbackName))
                    return fallbackName;
            }
        }

        var extension = GetNormalizedExtension(file);
        return string.IsNullOrWhiteSpace(extension) ? "documento" : $"documento{extension}";
    }

    private async Task<string> ReadDirectTextAsync(OcrFile file, CancellationToken cancellationToken)
    {
        byte[] bytes;

        if (!string.IsNullOrWhiteSpace(file.Content))
        {
            try
            {
                bytes = Convert.FromBase64String(file.Content);
            }
            catch (FormatException)
            {
                throw new NegocioException("Content no es Base64 válido.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(file.Url))
        {
            await using var remoteStream = await fileDownloader.DownloadUrlToMemoryStreamAsync(file.Url);
            bytes = remoteStream.ToArray();
        }
        else
        {
            return string.Empty;
        }

        await using var ms = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static BlobUploadOptions BuildBlobUploadOptions(string? extension)
    {
        return new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = GetContentType(extension)
            }
        };
    }

    private static string GetContentType(string? extension)
    {
        return (extension ?? string.Empty).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            ".xml" => "application/xml",
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream"
        };
    }

    private async Task SaveArtifactBestEffortAsync(string fileUrl, OcrDocumentArtifactDto artifact, CancellationToken cancellationToken)
    {
        try
        {
            await PersistArtifactAsync(fileUrl, artifact, cancellationToken);
        }
        catch
        {
            // Si el sidecar estructurado falla, no interrumpimos la creación del caso.
        }
    }

    private async Task PersistArtifactAsync(string fileUrl, OcrDocumentArtifactDto artifact, CancellationToken cancellationToken)
    {
        if (_containerClient == null)
            return;

        var artifactBlobName = BuildArtifactBlobName(fileUrl);
        if (string.IsNullOrWhiteSpace(artifactBlobName))
            return;

        var blobClient = _containerClient.GetBlobClient(artifactBlobName);
        var json = JsonSerializer.Serialize(artifact, BuildArtifactJsonOptions());
        var payload = Encoding.UTF8.GetBytes(json);

        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        await using var stream = new MemoryStream(payload, writable: false);
        await blobClient.UploadAsync(stream, BuildBlobUploadOptions(".json"), cancellationToken: cancellationToken);
    }

    private static JsonSerializerOptions BuildArtifactJsonOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
    }

    private static string BuildArtifactBlobName(string fileUrl)
    {
        try
        {
            var uri = new Uri(fileUrl);
            var path = uri.AbsolutePath.Trim('/');
            var parts = path.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return string.Empty;

            return Uri.UnescapeDataString(parts[1]) + ".ocr.json";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static OcrDocumentArtifactDto BuildDirectArtifact(string originalName, string blobUrl, string directText, string extension)
    {
        var summary = BuildSummaryFromContent(directText, [], []);

        return new OcrDocumentArtifactDto
        {
            SourceFileName = originalName,
            SourceUrl = blobUrl,
            ModelId = "direct-text",
            SourceKind = extension is ".xml" or ".html" or ".htm" ? "markup-direct" : "direct-text",
            ProcessedAtUtc = DateTime.UtcNow,
            Content = directText,
            Summary = summary
        };
    }

    private static OcrDocumentArtifactDto BuildAnalyzeArtifact(string originalName, string blobUrl, string modelId, AnalyzeResult result)
    {
        var keyValuePairs = (result.KeyValuePairs ?? [])
            .Select(pair => new OcrArtifactKeyValueDto
            {
                Key = pair.Key?.Content ?? string.Empty,
                Value = pair.Value?.Content ?? string.Empty,
                Confidence = pair.Confidence,
                KeyRegions = MapRegions(pair.Key?.BoundingRegions),
                ValueRegions = MapRegions(pair.Value?.BoundingRegions)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key) || !string.IsNullOrWhiteSpace(item.Value))
            .ToList();

        var documents = (result.Documents ?? [])
            .Select(document => new OcrArtifactDocumentDto
            {
                DocumentType = document.DocumentType ?? string.Empty,
                Confidence = document.Confidence,
                Regions = MapRegions(document.BoundingRegions),
                Fields = MapDocumentFields(document.Fields)
            })
            .ToList();

        var paragraphs = (result.Paragraphs ?? [])
            .Select(paragraph => new OcrArtifactParagraphDto
            {
                Role = paragraph.Role.ToString(),
                Content = paragraph.Content ?? string.Empty,
                Regions = MapRegions(paragraph.BoundingRegions)
            })
            .ToList();

        var pages = (result.Pages ?? [])
            .Select(page => new OcrArtifactPageDto
            {
                PageNumber = page.PageNumber,
                Width = page.Width,
                Height = page.Height,
                Unit = page.Unit.ToString(),
                Lines = (page.Lines ?? [])
                    .Select(line => new OcrArtifactLineDto
                    {
                        Content = line.Content ?? string.Empty,
                        Offset = line.Spans?.FirstOrDefault().Offset ?? 0,
                        Length = line.Spans?.FirstOrDefault().Length ?? 0,
                        Polygon = MapPolygon(line.Polygon)
                    })
                    .ToList(),
                Words = (page.Words ?? [])
                    .Select(word => new OcrArtifactWordDto
                    {
                        Content = word.Content ?? string.Empty,
                        Confidence = word.Confidence,
                        Offset = word.Span.Offset,
                        Length = word.Span.Length,
                        Polygon = MapPolygon(word.Polygon)
                    })
                    .ToList()
            })
            .ToList();

        var tables = (result.Tables ?? [])
            .Select(table => new OcrArtifactTableDto
            {
                RowCount = table.RowCount,
                ColumnCount = table.ColumnCount,
                Regions = MapRegions(table.BoundingRegions),
                Cells = (table.Cells ?? [])
                    .Select(cell => new OcrArtifactTableCellDto
                    {
                        RowIndex = cell.RowIndex,
                        ColumnIndex = cell.ColumnIndex,
                        RowSpan = cell.RowSpan ?? 1,
                        ColumnSpan = cell.ColumnSpan ?? 1,
                        Kind = cell.Kind.ToString(),
                        Content = cell.Content ?? string.Empty,
                        Regions = MapRegions(cell.BoundingRegions)
                    })
                    .ToList()
            })
            .ToList();

        return new OcrDocumentArtifactDto
        {
            SourceFileName = originalName,
            SourceUrl = blobUrl,
            ModelId = modelId,
            SourceKind = "ocr-analyze",
            ProcessedAtUtc = DateTime.UtcNow,
            Content = result.Content ?? string.Empty,
            Summary = BuildSummaryFromContent(result.Content ?? string.Empty, keyValuePairs, documents),
            Pages = pages,
            Paragraphs = paragraphs,
            KeyValuePairs = keyValuePairs,
            Tables = tables,
            Documents = documents
        };
    }

    private static OcrArtifactSummaryDto BuildSummaryFromContent(
        string content,
        IReadOnlyCollection<OcrArtifactKeyValueDto> keyValuePairs,
        IReadOnlyCollection<OcrArtifactDocumentDto> documents)
    {
        var documentKind = DetectDocumentKind(content, documents);
        var providerName = ExtractProviderName(keyValuePairs, documents, content);
        var highlights = BuildHighlights(keyValuePairs, documents);
        var tags = BuildSummaryTags(documentKind, providerName, content, documents);

        return new OcrArtifactSummaryDto
        {
            DocumentKind = documentKind,
            ProviderName = providerName,
            SuggestedLabel = BuildSuggestedLabel(documentKind, providerName),
            Tags = tags,
            Highlights = highlights
        };
    }

    private static string BuildSuggestedLabel(string documentKind, string providerName)
    {
        if (!string.IsNullOrWhiteSpace(documentKind) && !string.IsNullOrWhiteSpace(providerName))
            return $"{documentKind} · {providerName}";

        if (!string.IsNullOrWhiteSpace(documentKind))
            return documentKind;

        return !string.IsNullOrWhiteSpace(providerName)
            ? $"Documento · {providerName}"
            : "Documento OCR";
    }

    private static string DetectDocumentKind(string content, IReadOnlyCollection<OcrArtifactDocumentDto> documents)
    {
        var docType = documents
            .Select(x => x.DocumentType)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        if (!string.IsNullOrWhiteSpace(docType))
            return docType;

        var normalized = NormalizeForSearch(content);
        if (normalized.Contains("factura") || normalized.Contains("invoice"))
            return "Factura";
        if (normalized.Contains("epicrisis"))
            return "Epicrisis";
        if (normalized.Contains("autorizacion"))
            return "Autorizacion";
        if (normalized.Contains("resultado") && normalized.Contains("laboratorio"))
            return "Resultado de laboratorio";
        if (normalized.Contains("orden medica") || normalized.Contains("formula medica"))
            return "Orden medica";
        if (normalized.Contains("historia clinica"))
            return "Historia clinica";
        if (normalized.Contains("recibo"))
            return "Recibo";

        return "Documento OCR";
    }

    private static string ExtractProviderName(
        IReadOnlyCollection<OcrArtifactKeyValueDto> keyValuePairs,
        IReadOnlyCollection<OcrArtifactDocumentDto> documents,
        string content)
    {
        var providerTerms = new[] { "prestador", "proveedor", "hospital", "clinica", "ips", "centro medico", "doctor", "medico tratante" };

        var kvValue = keyValuePairs
            .FirstOrDefault(item => providerTerms.Any(term => NormalizeForSearch(item.Key).Contains(term)) && !string.IsNullOrWhiteSpace(item.Value))
            ?.Value;

        if (!string.IsNullOrWhiteSpace(kvValue))
            return kvValue.Trim();

        var fieldValue = documents
            .SelectMany(doc => doc.Fields)
            .FirstOrDefault(field => providerTerms.Any(term => NormalizeForSearch(field.Name).Contains(term)) && !string.IsNullOrWhiteSpace(field.Value))
            ?.Value;

        if (!string.IsNullOrWhiteSpace(fieldValue))
            return fieldValue.Trim();

        var regex = new Regex(@"(?:prestador|proveedor|hospital|clinica|ips)\s*[:\-]\s*(.+)", RegexOptions.IgnoreCase);
        var match = regex.Match(content ?? string.Empty);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static List<string> BuildSummaryTags(
        string documentKind,
        string providerName,
        string content,
        IReadOnlyCollection<OcrArtifactDocumentDto> documents)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(documentKind))
            tags.Add(documentKind);

        if (!string.IsNullOrWhiteSpace(providerName))
            tags.Add($"Prestador: {providerName}");

        var normalized = NormalizeForSearch(content);
        if (normalized.Contains("paciente"))
            tags.Add("Tiene datos de paciente");
        if (normalized.Contains("cie") || normalized.Contains("diagnostico"))
            tags.Add("Tiene diagnosticos");
        if (normalized.Contains("valor total") || normalized.Contains("subtotal") || normalized.Contains("iva"))
            tags.Add("Tiene valores facturados");
        if (normalized.Contains("fecha"))
            tags.Add("Tiene fechas");

        foreach (var docType in documents.Select(x => x.DocumentType).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            tags.Add(docType);
        }

        return tags.Take(10).ToList();
    }

    private static List<OcrArtifactHighlightDto> BuildHighlights(
        IReadOnlyCollection<OcrArtifactKeyValueDto> keyValuePairs,
        IReadOnlyCollection<OcrArtifactDocumentDto> documents)
    {
        var highlights = keyValuePairs
            .Where(item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => new OcrArtifactHighlightDto
            {
                Label = item.Key,
                Value = item.Value,
                PageNumber = item.ValueRegions.FirstOrDefault()?.PageNumber ?? item.KeyRegions.FirstOrDefault()?.PageNumber,
                Confidence = item.Confidence,
                Polygon = item.ValueRegions.FirstOrDefault()?.Polygon ?? item.KeyRegions.FirstOrDefault()?.Polygon ?? []
            })
            .Take(10)
            .ToList();

        if (highlights.Count >= 10)
            return highlights;

        foreach (var field in documents.SelectMany(doc => doc.Fields))
        {
            if (string.IsNullOrWhiteSpace(field.Name) || string.IsNullOrWhiteSpace(field.Value))
                continue;

            if (highlights.Any(h => h.Label.Equals(field.Name, StringComparison.OrdinalIgnoreCase) && h.Value.Equals(field.Value, StringComparison.OrdinalIgnoreCase)))
                continue;

            highlights.Add(new OcrArtifactHighlightDto
            {
                Label = field.Name,
                Value = field.Value,
                PageNumber = field.Regions.FirstOrDefault()?.PageNumber,
                Confidence = field.Confidence,
                Polygon = field.Regions.FirstOrDefault()?.Polygon ?? []
            });

            if (highlights.Count >= 10)
                break;
        }

        return highlights;
    }

    private static string ResolveFieldValue(DocumentField field)
    {
        if (!string.IsNullOrWhiteSpace(field.Content))
            return field.Content;

        if (field.ValueString != null)
            return field.ValueString;
        if (field.ValueDate != null)
            return field.ValueDate.Value.ToString("yyyy-MM-dd");
        if (field.ValueTime != null)
            return field.ValueTime.Value.ToString("HH:mm:ss");
        if (field.ValuePhoneNumber != null)
            return field.ValuePhoneNumber;
        if (field.ValueDouble != null)
            return field.ValueDouble.Value.ToString(CultureInfo.InvariantCulture);
        if (field.ValueInt64 != null)
            return field.ValueInt64.Value.ToString(CultureInfo.InvariantCulture);
        if (field.ValueCurrency != null)
            return field.ValueCurrency.Amount.ToString(CultureInfo.InvariantCulture);
        if (field.ValueBoolean != null)
            return field.ValueBoolean.Value.ToString();
        if (field.ValueSelectionMark != null)
            return field.ValueSelectionMark.ToString();
        if (field.ValueSignature != null)
            return field.ValueSignature.ToString();
        if (field.ValueCountryRegion != null)
            return field.ValueCountryRegion;

        return string.Empty;
    }

    private static string NormalizeForSearch(string value)
    {
        return (value ?? string.Empty)
            .Normalize(NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .Aggregate(new StringBuilder(), (sb, ch) => sb.Append(char.ToLowerInvariant(ch)))
            .ToString();
    }

    private static List<OcrArtifactDocumentFieldDto> MapDocumentFields(DocumentFieldDictionary? fields)
    {
        if (fields == null || fields.Count == 0)
            return [];

        return fields
            .Select(field => new OcrArtifactDocumentFieldDto
            {
                Name = field.Key,
                FieldType = field.Value.FieldType.ToString(),
                Content = field.Value.Content ?? string.Empty,
                Value = ResolveFieldValue(field.Value),
                Confidence = field.Value.Confidence,
                Regions = MapRegions(field.Value.BoundingRegions)
            })
            .ToList();
    }

    private static List<OcrArtifactRegionDto> MapRegions(IReadOnlyList<BoundingRegion>? regions)
    {
        return regions?.Select(region => new OcrArtifactRegionDto
        {
            PageNumber = region.PageNumber,
            Polygon = MapPolygon(region.Polygon)
        }).ToList() ?? [];
    }

    private static List<OcrArtifactPointDto> MapPolygon(IReadOnlyList<float>? polygon)
    {
        if (polygon == null || polygon.Count == 0)
            return [];

        var points = new List<OcrArtifactPointDto>(polygon.Count / 2);
        for (var index = 0; index + 1 < polygon.Count; index += 2)
        {
            points.Add(new OcrArtifactPointDto
            {
                X = polygon[index],
                Y = polygon[index + 1]
            });
        }

        return points;
    }

}
