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

        var userContent = req.Message ?? "";
        //verificar si hay archivos en req y si hay mensaje       
        if (req.FileUrls != null && req.FileUrls.Count > 0)
        {
            userContent += "\n\nArchivos remitidos por el cliente:\n" + string.Join("\n", req.FileUrls);
        }

        // Combinar texto
        var combined = new StringBuilder();
        foreach (var df in dataFiles)
        {
            var fileName = Path.GetFileName(df.FileUri);
            var ext = Path.GetExtension(df.FileUri)?.ToLower().TrimStart('.') ?? "";
            combined.AppendLine($"documento: {fileName}.{ext}---{df.Text}---");
        }

        var context = string.Empty;
        if (req.Origin.Equals(ConstanteTipoAgente.Chat, StringComparison.OrdinalIgnoreCase))
        {
            context =  userContent + $"\n\nInformación del Caso número NE-{(processCase.CaseCode.ToString()?.Split('-').FirstOrDefault() ?? "")}: Usuario que consulta: {req.Usuario}\n" +
                combined.ToString();
        }
        else
        {
            context = combined.ToString();
        }


        // Llamada a OpenAI
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

        ResponsePromptDto result = new()
        {
            CaseCode = req.CaseCode,
            ResponseText = final!.ResponseText,
            MetadataJson = final.MetadataJson,
            ProccessName = processDefinition.Name ?? "",
            AgentName = agenteProceso!.Agent!.Name ?? "",
            RequestText = final.RequestText ?? "",
            CreatedDate = final.CreatedDate
            
        };
        return result;
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
                .Any(op => op.TypeAgentNavigation.Code == req.Origin));

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

    public async Task<ViewCaseDetails?> ObtenerDetailsProcessCase(Guid caseCode, IdentityUser? user)
    {
        var proccess = await db.ProcessCase
            .Include(pc => pc.FinalResponseResults).Include(pc => pc.DataFile)
            .FirstOrDefaultAsync(pc => pc.CaseCode == caseCode);

        if (proccess == null)
            throw new NegocioException("No hay información que mostrar");

        var agents = await GetAgentTypesForUserAndProcessAsync(user, proccess.DefinitionCode);
        bool hasChat = false;
        bool hasButton = false;

        if (agents is not null && agents.Count > 0)
        {
            hasChat = agents.Any(c => c.Code.Equals(ConstanteTipoAgente.Chat, StringComparison.OrdinalIgnoreCase));
            hasButton = agents.Any(c => c.Code.Equals(ConstanteTipoAgente.Botones, StringComparison.OrdinalIgnoreCase));
        }

        var details = new ViewCaseDetails
        {
            HasChat = hasChat,
            HasButton = hasButton,
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
                    AgentCode = ap.AgentCode
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
            .Include(ap => ap.Process)//.ThenInclude(p => p.ProcessStep.OrderBy(s => s.StepOrder))
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

    private static async Task<(string Url, string Text)> ProcessFileAsync(
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

    private static async Task<string> UploadBlobAsync(
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
