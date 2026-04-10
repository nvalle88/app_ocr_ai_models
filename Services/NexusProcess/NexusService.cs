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


        var agenteProceso = BuscarPromptPorAgenteProceso(req, processDefinition);
        if (agenteProceso == null || agenteProceso.Agent == null) return null!;

        var promptModel = agenteProceso.Agent.OPAIModelPrompt?.First().PromptCodeNavigation;
        var agentPrompt = agenteProceso.Agent.OPAIModelPrompt?.First();
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
            .Include(ap => ap.Agent.AgentConfig)
            .Where(ap => ap.DefinitionCode == process.Code && ap.Agent.IsActive)
            .AsQueryable();

        if (req?.Id > 0)
        {
            query = query.Where(ap => ap.Id == req.Id);
        }

        var data = query.FirstOrDefault(ap =>
            ap.Agent.OPAIModelPrompt.Any(op => op.TypeAgentNavigation.Code == req!.Origin));

        return data;
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

    public async Task<List<ProcessCase>?> ObtenerProcesos(IdentityUser? user, IList<string>? roles)
    {
        //a partir de los roles que tiene el usuario necesito obtener los procesos, para posteriormente filtrar los casos
        var procesosUsuario = await GetProcessesByUser(user, roles);
        if (procesosUsuario.Processes.Count == 0)
            return [];

        var codigos = procesosUsuario.Processes
            .Where(p => !string.IsNullOrWhiteSpace(p.ProcessId))
            .Select(p => p.ProcessId!)
            .ToList();
        var s = DateTime.Now.AddDays(-10).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var today = DateTime.ParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        int pageNumber = 1; // página actual
        int pageSize = 200;  // registros por página

        return await db.ProcessCase
            .AsNoTracking()
            .Include(x => x.FinalResponseResults)
            .Include(x => x.DefinitionCodeNavigation)
            .Where(x => x.StartDate > today && codigos.Contains(x.DefinitionCode))
            .OrderByDescending(pc => pc.StartDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

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
            .Where(g => g.Any(x => x.ProcessAgentId.HasValue))
            .Select(g =>
            {
                // Priorizar entradas con ProcessAgentId (vienen de policies), si hay varias elegir la primera
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
                return (Url: blobUrl, Text: directText, OriginalName: originalName);
            }

            var operation = await clientOcr.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                ocrSetting.ModelId,
                new Uri(blobUrl),
                cancellationToken: cts.Token);

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

}
