#region Using

using app_ocr_ai_models.Utils;
using app_tramites.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Azure.AI.DocumentIntelligence;
using Azure;
using System.Text;
using Azure.Storage.Blobs;
using app_tramites.Models.ViewModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Rendering;
using app_tramites.Models.ModelAi;
using app_ocr_ai_models.Data;
using Microsoft.ApplicationInsights;
using Usage = app_tramites.Models.ModelAi.Usage;
using System;
using AspNetCoreGeneratedDocument;
using Microsoft.AspNetCore.Identity;
using System.Globalization;

#endregion

namespace SmartAdmin.Web.Controllers
{
    [Authorize]
    public class NexusController : Controller
    {
        private readonly OCRDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public NexusController(OCRDbContext context, UserManager<IdentityUser> userManager)
        {
            _userManager = userManager;
            _db = context;
        }

        public async Task<IActionResult> Details(Guid caseCode)
        {
            var processCase = await _db.ProcessCase
                                       .Include(pc => pc.FinalResponseResults).Include(pc => pc.DataFile)
                                       .FirstOrDefaultAsync(pc => pc.CaseCode == caseCode);

            if (processCase == null)
                return NotFound();

            return View(processCase);
        }

        public async Task<IActionResult> Index()
        {
            var vm = new QueryInput { ProcessCode = "A-HOSP" };
            var today = DateTime.ParseExact("2025-08-11 15:20:00", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            var casos = await _db.ProcessCase.Include(x => x.FinalResponseResults).Where(x => x.StartDate > today)
                                 .OrderByDescending(pc => pc.StartDate)
                                 .ToListAsync();
            ViewBag.ProcessCases = casos;
            return View(vm);
        }


        // POST: /Nexus/ProcessPending
        [HttpPost]
        public async Task<IActionResult> ProcessCaseAjax([FromForm] Guid caseCode)
        {
            var final = await EjecutarFinalResponse(caseCode);
            var txt = string.Join(" ", final.ResponseText.ToLower());

            // Detectar tipo de caso
            string resumenCategoria;
            if (txt.Contains("**hospital del día**"))
                resumenCategoria = "Hospital del Día";
            else if (txt.Contains("**hospitalario**"))
                resumenCategoria = "Hospitalario";
            else if (txt.Contains("**ambulatorio**"))
                resumenCategoria = "Ambulatorio";
            else
                resumenCategoria = "No Definido";

            return Json(new { caseCode, typeCase = resumenCategoria });
        }

        // 3. ProcessPending: procesa todos los casos pendientes uno a uno
        [HttpPost]
        public async Task<IActionResult> ProcessPending()
        {
            var pendientes = await _db.ProcessCase
                .Include(pc => pc.FinalResponseResults)
                .Where(pc => !pc.FinalResponseResults.Any())
                .ToListAsync();

            foreach (var pc in pendientes)
            {
                await EjecutarFinalResponse(pc.CaseCode);
            }

            return Json(new { processed = pendientes.Count });
        }

        // 4. EjecutarFinalResponse: lógica de prompt, llamada a OpenAI y guardado
        private async Task<FinalResponseResult> EjecutarFinalResponse(Guid caseCode)
        {
            // Cargar caso, archivos y definición
            var processCase = await _db.ProcessCase
                .Include(pc => pc.DataFile)
                .Include(pc => pc.DefinitionCodeNavigation)
                    .ThenInclude(pd => pd.ProcessStep)
                .FirstOrDefaultAsync(pc => pc.CaseCode == caseCode);
            if (processCase == null) return null;

            var dataFiles = processCase.DataFile.ToList();
            var processDefinition = processCase.DefinitionCodeNavigation;

            // Configuración final
            var finalConfig = await _db.FinalResponseConfig
                .FirstOrDefaultAsync(cfg =>
                    cfg.ProcessCode == processDefinition.Code && cfg.IsEnabled);
            if (finalConfig == null) return null;

            var finalAgent = await _db.Agent
                .Include(a => a.AgentConfig)
                .FirstOrDefaultAsync(a => a.Code == finalConfig.AgentCode);
            if (finalAgent == null) return null;

            // Construir prompt
            string prompt = finalConfig.PromptTemplate
                .Replace("{FileCount}", finalConfig.IncludeFileCount
                    ? dataFiles.Count.ToString() : "")
                .Replace("{StepNames}", finalConfig.IncludeStepNames
                    ? string.Join(", ",
                        processDefinition.ProcessStep
                            .OrderBy(s => s.StepOrder)
                            .Select(s => s.StepName ?? s.StepOrder.ToString()))
                    : "");

            var metadata = !string.IsNullOrWhiteSpace(finalConfig.MetadataJson)
                ? JsonSerializer.Deserialize<FinalResponseMetadata>(finalConfig.MetadataJson)!
                : new FinalResponseMetadata();
            if (!string.IsNullOrWhiteSpace(metadata.CustomInstructions))
                prompt += metadata.CustomInstructions;

            // Combinar texto
            var combined = new StringBuilder();
            if (finalConfig.UseOriginalText)
            {
                foreach (var df in dataFiles)
                    combined.AppendLine(df.Text);
            }
            else
            {
                var orders = finalConfig.IncludedStepOrders == "*"
                    ? processDefinition.ProcessStep.Select(s => s.StepOrder).ToList()
                    : finalConfig.IncludedStepOrders
                        .Split(',').Select(int.Parse).ToList();

                foreach (var stepOrder in orders)
                {
                    combined.AppendLine($"## Paso {stepOrder}");
                    var stepResults = await _db.StepExecution
                        .Where(e => e.CaseCode == caseCode && e.StepOrder == stepOrder)
                        .OrderBy(e => e.DataFileId)
                        .Select(e => e.ResponseContent)
                        .ToListAsync();
                    combined.Append(string.Join(Environment.NewLine, stepResults));
                }
            }
            // Llamada a OpenAI
            var finalResp = await CallOpenAiAsync(
                finalAgent,
                prompt,
                combined.ToString(),
                dataFiles.First().Id,
                stepOrder: 999,
                maxTokens: metadata.MaxTokens ?? 1000,
                temperature: metadata.Temperature ?? 0.2,
                topP: 1.0);

            var final = new FinalResponseResult
            {
                CaseCode = caseCode,
                FileId = null,
                ConfigCode = finalConfig.ConfigCode,
                ResponseText = finalResp.ResultText,
                ExecutionSummary = $"Files:{dataFiles.Count};Steps:{finalConfig.IncludedStepOrders}",
                CreatedDate = DateTime.Now
            };
            // Guardar resultado
            _db.FinalResponseResult.Add(final);
            await _db.SaveChangesAsync();
            return final;
        }


        public class OutputHistory
        {
            public string OriginalText { get; set; } = string.Empty;
            public List<string> GeneratedSteps { get; set; } = new();
        }

        public class FinalResponseMetadata
        {
            public int? MaxTokens { get; set; }
            public double? Temperature { get; set; }
            public string? Language { get; set; }
            public string? CustomInstructions { get; set; }
        }

        public class ChatRequest
        {
            public Guid CaseCode { get; set; }
            public string Message { get; set; } = "";
            public List<string> FileUrls { get; set; } = new();
        }

        // Models/ChatRequest.cs
        //public class ChatRequest
        //{
        //    public Guid CaseCode { get; set; }
        //    public string Message { get; set; } = "";
        //    public List<string> FileUrls { get; set; } = new();
        //}

        // Controllers/NexusController.cs
        [HttpPost]
        public async Task<IActionResult> ChatAjax([FromBody] ChatRequest req)
        {
            if (req == null
                || req.CaseCode == Guid.Empty
                || string.IsNullOrWhiteSpace(req.Message))
                return BadRequest("Datos inválidos.");

            // 1) Recupera el caso y sus archivos
            var processCase = await _db.ProcessCase
                .Include(pc => pc.DataFile)
                .FirstOrDefaultAsync(pc => pc.CaseCode == req.CaseCode);
            if (processCase == null)
                return NotFound("Caso no encontrado.");

            var usuario = await _userManager.GetUserAsync(User);
            // 2) Prepara tu SYSTEM prompt **genérico**, o déjalo en blanco si no lo necesitas
            string systemPrompt =
 @"Eres Nexus, un asistente experto en auditoría que responde de manera clara y profesional.  
Siempre responde en formato Markdown acorde a la pregunta que te hacen.  
Incluye el nombre del usuario que te consulta en tus respuestas para hacerlo más cercano.  
Omite siempre el o los nombres de clientes o pacientes en todas las respuestas, has referencia siempre como el paciente.
Para el nombre de los médicos si puedes responderlo el nombre del doctor o doctora por lo general es Doctor: Nombre en los documentos.
Mantén un tono amigable y de confianza, como si fueras un asesor experto y buen amigo al mismo tiempo.  
Recuerda que eres NEXUS, el mejor aliado en temas de auditoría.";
            // 3) Prepara el contenido del usuario: su pregunta + (opcional) OCR
            var userContent = req.Message;
            // Si quieres añadir texto OCR de los archivos:
            var allText = string.Join(
                "\n\n---\n\n",
                processCase.DataFile.Select(f => f.Text)
            );
            userContent += $"\n\nInformación del Caso número NE-{(processCase.CaseCode.ToString()?.ToString()?.Split('-').FirstOrDefault() ?? "")}: Usuario que consulta: Auditor de Saldusa\n" + allText;

            var config = _db.FinalResponseConfig.FirstOrDefault(x => x.ProcessCode == "A-HOSP" && x.IsEnabled);
            var agent = await _db.Agent.Include(a => a.AgentConfig).FirstOrDefaultAsync(x => x.Code == config.AgentCode);
            // 4) Llamada a OpenAI usando tu método existente
            //    (reutiliza CallOpenAiAsync sin meter PromptTemplate)
            var aiDto = await CallOpenAiAsync(
                agent: agent,
                systemContent: systemPrompt,
                userText: userContent,
                dataFileId: processCase.DataFile.First().Id,
                stepOrder: 0,
                maxTokens: 1000,
                temperature: 0.2,
                topP: 1.0
            );

            // 5) (Opcional) Guardar resultado en BD
            var final = new FinalResponseResult
            {
                CaseCode = req.CaseCode,
                FileId = null,
                ConfigCode = config.ConfigCode,  // o algún marcador
                ResponseText = aiDto.ResultText,
                ExecutionSummary = $"Pregunta usuario directa {usuario.Email}",
                CreatedDate = DateTime.Now
            };
            _db.FinalResponseResult.Add(final);
            await _db.SaveChangesAsync();

            // 6) Devuelves sólo la respuesta
            return Json(new
            {
                response = aiDto.ResultText,
                timestamp = final.CreatedDate.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
            });
        }



        [HttpPost]
        public async Task<IActionResult> CreateCaseProcess([FromBody] QueryInput input)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.ProcessCode))
                return BadRequest("ProcessCode es obligatorio.");

            var processDef = await _db.Process
                .Include(p => p.ProcessStep.OrderBy(s => s.StepOrder))
                .FirstOrDefaultAsync(p => p.Code == input.ProcessCode);
            if (processDef == null)
                return NotFound("Proceso no encontrado.");

            var ocrSetting = await _db.OCRSetting
                .FirstOrDefaultAsync(x => x.SettingCode == "DEFAULT" && x.PlatformCode == "AZURE");
            if (ocrSetting == null)
                return NotFound("Configuración OCR no encontrada.");

            var caseCode = Guid.NewGuid();
            var processCase = new ProcessCase
            {
                CaseCode = caseCode,
                DefinitionCode = processDef.Code,
                StartDate = DateTime.Now,
                State = "Started"
            };
            _db.ProcessCase.Add(processCase);
            await _db.SaveChangesAsync();

            var blobCfg = await _db.AzureBlobConf.AsNoTracking().FirstOrDefaultAsync()
                          ?? throw new InvalidOperationException("AzureBlobConf no encontrada.");

            var ocrTasks = input.Files.Select(f =>
                ProcessFileAsync(f, ocrSetting, blobCfg)
            );
            var ocrResults = await Task.WhenAll(ocrTasks);

            var dataFiles = ocrResults.Select(r => new DataFile
            {
                CaseCode = caseCode,
                IsFileUri = !string.IsNullOrEmpty(r.Url),
                FileUri = r.Url,
                Text = r.Text,
                CreatedDate = DateTime.Now
            }).ToList();
            _db.DataFile.AddRange(dataFiles);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                caseCode = processCase.CaseCode,
                definitionCode = processCase.DefinitionCode,
                startDate = processCase.StartDate,
                state = processCase.State
            });
        }



        private async Task<OpenAiResponseDto> CallOpenAiAsync(
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

        public class OpenAiResponseDto
        {
            public int DataFileId { get; set; }
            public int StepOrder { get; set; }
            public string RequestJson { get; set; } = null!;
            public string ResultText { get; set; } = null!;
            public int PromptTokens { get; set; }
            public int CompletionTokens { get; set; }
            public DateTime StartedAt { get; set; }
            public DateTime FinishedAt { get; set; }
        }


        private async Task<(string Url, string Text)> ProcessFileAsync(
    OcrFile file,
    OCRSetting ocrSetting,
    AzureBlobConf blobCfg)
        {
            // Subir blob
            var blobUrl = await UploadBlobAsync(file, blobCfg);

            // Ejecutar OCR
            var clientOcr = new DocumentIntelligenceClient(
                new Uri(ocrSetting.Endpoint),
                new AzureKeyCredential(ocrSetting.ApiKey));
            var operation = await clientOcr.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                ocrSetting.ModelId,
                new Uri(blobUrl));

            return (Url: blobUrl, Text: operation.Value.Content);
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





        public Stream Base64ToStream(string base64String)
        {
            byte[] bytes = Convert.FromBase64String(base64String);
            return new MemoryStream(bytes);
        }
        public async Task<string> UploadFileAsync(Stream fileStream, string extension)
        {
            var config = await _db.AzureBlobConf
                .FirstOrDefaultAsync();

            var blobServiceClient = new BlobServiceClient(config.ConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(config.ContainerName);

            await containerClient.CreateIfNotExistsAsync();

            var name = $"{Guid.NewGuid()}{extension}";
            var blobClient = containerClient.GetBlobClient(name);
            await blobClient.UploadAsync(fileStream, overwrite: true);

            return blobClient.Uri.ToString();
        }




    }
}
