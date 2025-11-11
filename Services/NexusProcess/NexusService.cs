using app_ocr_ai_models.Data;
using app_tramites.Models.Dto;
using app_tramites.Models.ModelAi;
using app_tramites.Models.ViewModel;
using Microsoft.EntityFrameworkCore;
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

        //según el origen de la llamada hay que buscar el pront asociado al AgenteProceso (por defecto, chat ó botones)
        //aqui toca ver que AgenteProceso tiene marcado por defecto
        var agenteProceso = BuscarPromptPorAgenteProceso(req, processDefinition);
        if (agenteProceso == null || agenteProceso.Agent == null) return null!;

        //en el agentePrompt aumentar un campo para poner la metadata que antes estaba en FinalResoponseConfig

        //de aqui en adelante esto ya no va y empezar a realizar con el prompt y agente encontrado todos los reemplazos

        //hay que ver si el prompt tiene esos marcadores en agenteProceso.Agent?.OPAIModelPrompt?.First().PromptCodeNavigation y si no los tiene no hacer nada            

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
        //lógica para buscar el prompt asociado al AgenteProceso
        if (req.Origin == 2)//chat
        {
            return null;
        }
        else if (req.Origin == 3) //botones
        {
            return null;
        }
        else
        {
            return db.AgentProcesses
                 .Include(ap => ap.Agent)
                 .ThenInclude(a => a.OPAIModelPrompt)
                 .ThenInclude(op => op.PromptCodeNavigation)
                 .Include(ap => ap.Agent.AgentConfig)
                 .Where(ap => ap.DefinitionCode == process.Code && ap.Agent.IsActive)
                 .FirstOrDefault(ap => ap.Agent.OPAIModelPrompt
                 .Any(op => op.TypeAgent == 1 && op.IsDefault));

        }
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
}
