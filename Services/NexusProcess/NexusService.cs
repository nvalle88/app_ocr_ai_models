using app_ocr_ai_models.Data;
using app_tramites.Extensions;
using app_tramites.Models.Dto;
using app_tramites.Models.ModelAi;
using app_tramites.Models.ViewModel;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Usage = app_tramites.Models.ModelAi.Usage;

namespace app_tramites.Services.NexusProcess;

public class NexusService(OCRDbContext db) : INexusService
{

    public async Task<FinalResponseResult> EjecutarPrompt(PromptRequest req)
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

        // Combinar texto
        var combined = new StringBuilder();
        foreach (var df in dataFiles)
        {
            var fileName = Path.GetFileName(df.FileUri);
            var ext = Path.GetExtension(df.FileUri)?.ToLower().TrimStart('.') ?? "";
            combined.AppendLine($"documento: {fileName}.{ext}---{df.Text}---");
        }


        // Llamada a OpenAI
        var finalResp = await CallOpenAiAsync(
            agenteProceso.Agent!,
            prompt,
            combined.ToString(),
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

        var requestText = string.IsNullOrEmpty(req.Message) ? agenteProceso.Agent.Description : req.Message;
        requestText = $"<div style=\"text-align: right;font-weight:bold;\">{requestText}</div>";

        var final = new FinalResponseResult
        {
            CaseCode = req.CaseCode,
            ResponseText = finalResp.ResultText,
            CreatedDate = DateTime.Now,
            RequestText = requestText,
            MetadataJson = JsonSerializer.Serialize(metadata, options)
        };

        // Guardar resultado
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
        return final;
    }

    //deberia ser un servicio
    public AgentProcess? BuscarPromptPorAgenteProceso(PromptRequest req, Process process)
    {
        
        var data =  db.AgentProcesses
                .Include(ap => ap.Agent)
                .ThenInclude(a => a.OPAIModelPrompt)
                .ThenInclude(op => op.PromptCodeNavigation)
                .Include(ap => ap.Agent.AgentConfig)
                .Where(ap => ap.DefinitionCode == process.Code && ap.Agent.IsActive)
                .FirstOrDefault(ap => ap.Agent.OPAIModelPrompt
                .Any(op => op.TypeAgentNavigation.Code == req.Origin && op.IsDefault));

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

    public async Task<ViewCaseDetails?> ObtenerDetailsProcessCase(Guid caseCode)
    {
        var proccess = await db.ProcessCase
            .Include(pc => pc.FinalResponseResults).Include(pc => pc.DataFile)
            .FirstOrDefaultAsync(pc => pc.CaseCode == caseCode);
        var details = new ViewCaseDetails
        {
            HasChat = true,
            HasButton = true,
            ProcessCase = proccess!
        };

        return details;
    }

    public async Task<List<ProcessCase>?> ObtenerProcesos()
    {
        var today = DateTime.ParseExact("2025-09-12 17:10:00", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return await db.ProcessCase.Include(x => x.FinalResponseResults)
                .Include(x => x.DefinitionCodeNavigation)
                .Where(x => x.StartDate > today)
                                 .OrderByDescending(pc => pc.StartDate)
                                 .ToListAsync();
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
               aap.AgentProcess.Process,
               ProcessAgentId = (int?)aap.AgentProcess.Id // Obtener el ProcessAgentId
           })
           //.Distinct()
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
            //.Distinct()
            .ToListAsync();

        List<ViewAgentProcess> allProcesses = [.. processesFromPolicies
            .Concat(processesFromRoles)
            .Where(p => p.Process != null && !string.IsNullOrWhiteSpace(p.Process.Code))
            .GroupBy(p => (p.Process.Code ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Any(x => x.ProcessAgentId.HasValue))
            .Select(g =>
            {
                // Priorizar entradas con ProcessAgentId (vienen de policies), si hay varias elegir la primera
                var chosen = g
                    .OrderByDescending(x => x.ProcessAgentId.HasValue)
                    .ThenBy(x => x.Process.Name ?? "")
                    .First();

                return new ViewAgentProcess { ProcessId = chosen.Process.Code, ProcessName = chosen.Process.Name,  Description = chosen.Process.Description,ProcessAgentId = chosen.ProcessAgentId };
            })];

        
        return new ViewProcessUser { Success = true, Processes = allProcesses };
        //return  Json(new { success = true, processes = allProcesses });
    }

    public async Task<ViewCreateCase> CreateCaseProcess(QueryInput input)
    {
     

        if (input == null || string.IsNullOrWhiteSpace(input.ProcessCode))
            throw new NegocioException("ProcessCode es obligatorio.");
            //return BadRequest("ProcessCode es obligatorio.");
        var ocrSetting = await db.OCRSetting
            .FirstOrDefaultAsync(x => x.SettingCode == "DEFAULT" && x.PlatformCode == "AZURE") ?? throw new NegocioException("Configuración OCR no encontrada.");
        //return NotFound("Configuración OCR no encontrada.");

        var blobCfg = await db.AzureBlobConf.AsNoTracking().FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("AzureBlobConf no encontrada.");

        var ocrTasks = input.Files.Select(f =>
            ProcessFileAsync(f, ocrSetting, blobCfg)
        );

        var ocrResults = await Task.WhenAll(ocrTasks);

        //el proceso a partir del AgentProcess
        int agentProcessId = Int16.Parse(input.ProcessCode);
        var agentProcess = await db.AgentProcesses
            .Include(ap => ap.Process).ThenInclude(p => p.ProcessStep.OrderBy(s => s.StepOrder))
            .FirstOrDefaultAsync(ap => ap.Id == agentProcessId);

        var nameProccess = agentProcess?.Process.Name;
        var processDef = agentProcess?.Process;
        if (processDef == null)
            throw new NegocioException("Proceso no encontrado.");
        //return NotFound("Proceso no encontrado.");

        var caseCode = Guid.NewGuid();
        var processCase = new ProcessCase
        {
            CaseCode = caseCode,
            DefinitionCode = processDef.Code,
            StartDate = DateTime.Now,
            State = "Started"
        };
        db.ProcessCase.Add(processCase);
        await db.SaveChangesAsync();



        var dataFiles = ocrResults.Select(r => new DataFile
        {
            CaseCode = caseCode,
            IsFileUri = !string.IsNullOrEmpty(r.Url),
            FileUri = r.Url,
            Text = r.Text,
            CreatedDate = DateTime.Now
        }).ToList();
        db.DataFile.AddRange(dataFiles);
        await db.SaveChangesAsync();



        return new ViewCreateCase
        {
            CaseCode = processCase.CaseCode,
            DefinitionCode = processCase.DefinitionCode,
            StartDate = processCase.StartDate,
            State = processCase.State,
            NameProccess = nameProccess ?? ""
        };
        
    }

    private async Task<(string Url, string Text)> ProcessFileAsync(
     OcrFile file,
     OCRSetting ocrSetting,
     AzureBlobConf blobCfg,
     int timeoutMilliseconds = 90000) // 30 segundos por defecto
    {
        using var cts = new CancellationTokenSource(timeoutMilliseconds);

        try
        {
            // Subir blob
            var blobUrl = await UploadBlobAsync(file, blobCfg);

            // Ejecutar OCR con timeout
            var clientOcr = new DocumentIntelligenceClient(
                new Uri(ocrSetting.Endpoint),
                new AzureKeyCredential(ocrSetting.ApiKey));

            var operation = await clientOcr.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                ocrSetting.ModelId,
                new Uri(blobUrl),
                cancellationToken: cts.Token);

            return (Url: blobUrl, Text: operation.Value.Content);
        }
        catch (TaskCanceledException ex)
        {
            throw new TimeoutException($"El procesamiento del archivo superó el tiempo límite de {timeoutMilliseconds} ms.");
        }
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

        await using var ms = new MemoryStream(Convert.FromBase64String(file.Content));
        await blobClient.UploadAsync(ms, overwrite: true);

        return blobClient.Uri.ToString();
    }
}
