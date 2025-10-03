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
using Azure.Identity;
using Azure.Messaging;
using Azure;

using Azure.Identity;


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




        // GET Nexus/Notes/GetNotes?caseCode=NE-123
        [HttpGet("GetNotes")]
        public async Task<IActionResult> GetNotes(string caseCode)
        {
            if (string.IsNullOrWhiteSpace(caseCode))
                return BadRequest(new { success = false, error = "caseCode requerido" });

            var notes = await _db.Note
                .Where(n => n.CaseCode ==Guid.Parse(caseCode))
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new
                {
                    id = n.NoteId,
                    caseCode = n.CaseCode,
                    title = n.Title,
                    detail = n.Detail,
                    createdAt = n.CreatedAt,
                    createdBy = n.CreatedBy
                })
                .ToListAsync();

            return Ok(notes);
        }

        // DTO para AddNote
        public class AddNoteDto
        {
            public int id { get; set; } // 0 => nuevo, >0 => editar
            public string caseCode { get; set; }
            public string title { get; set; }
            public string detail { get; set; }
        }

        // POST Nexus/Notes/AddNote
        [HttpPost("AddNote")]
        public async Task<IActionResult> AddNote([FromBody] AddNoteDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.caseCode))
                return BadRequest(new { success = false, error = "Payload inválido" });

            dto.title = (dto.title ?? "").Trim();
            dto.detail = (dto.detail ?? "").Trim();

            if (dto.id > 0)
            {
                var existing = await _db.Note.FindAsync(dto.id);
                if (existing == null) return NotFound(new { success = false, error = "Nota no encontrada" });

                existing.Title = dto.title;
                existing.Detail = dto.detail;
                // opcional: mantener updatedAt/updatedBy si lo necesitas
                await _db.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    note = new
                    {
                        id = existing.NoteId,
                        caseCode = existing.CaseCode,
                        title = existing.Title,
                        detail = existing.Detail,
                        createdAt = existing.CreatedAt,
                        createdBy = existing.CreatedBy
                    }
                });
            }
            else
            {
                var note = new Note
                {
                    CaseCode =Guid.Parse(dto.caseCode),
                    Title = dto.title,
                    Detail = dto.detail,
                    CreatedAt = DateTime.Now,
                    CreatedBy = User?.Identity?.Name // opcional, puede ser null
                    
                };

                _db.Note.Add(note);
                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (Exception ex)
                {

                    throw;
                }

                return Ok(new
                {
                    success = true,
                    note = new
                    {
                        id = note.NoteId,
                        caseCode = note.CaseCode,
                        title = note.Title,
                        detail = note.Detail,
                        createdAt = note.CreatedAt,
                        createdBy = note.CreatedBy
                    }
                });
            }
        }

        // DTO para DeleteNote
        public class DeleteDto
        {
            public int id { get; set; }
        }

        // POST Nexus/Notes/DeleteNote
        [HttpPost("DeleteNote")]
        public async Task<IActionResult> DeleteNote([FromBody] DeleteDto dto)
        {
            if (dto == null || dto.id <= 0) return BadRequest(new { success = false, error = "id inválido" });

            var note = await _db.Note.FindAsync(dto.id);
            if (note == null) return NotFound(new { success = false, error = "Nota no encontrada" });

            _db.Note.Remove(note);
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }


        public class SubmitFeedbackDto
        {
            public string CaseCode { get; set; }
            public bool? Helped { get; set; }
            public string Comment { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> SubmitFeedback([FromBody] SubmitFeedbackDto dto)
        {
            if (dto == null) return BadRequest(new { success = false, message = "Payload inválido" });

            // Validación básica
            if (string.IsNullOrWhiteSpace(dto.CaseCode))
            {
                return BadRequest(new { success = false, message = "caseCode es requerido" });
            }

            try
            {
                var user = await _userManager.GetUserAsync(User); // Usuario logeado
                var userName = user?.UserName ?? "Sistema";
                var entity = new CaseReview
                {
                    CaseCode = Guid.Parse(dto.CaseCode),
                    Answer = dto.Helped.Value,
                    ReviewText = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment,
                    CreatedAt = DateTime.Now,
                    CreatedBy = userName,

                };

                _db.CaseReview.Add(entity);
                await _db.SaveChangesAsync();

                return Ok(new { success = true, message = "Reseña guardada" });
            }
            catch (DbUpdateException dbEx)
            {
                // Log aquí si tienes logger
                return StatusCode(500, new { success = false, message = "Error guardando en la base de datos" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
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

        public async Task<IActionResult> Details1(Guid caseCode)
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
            var today = DateTime.ParseExact("2025-09-12 17:10:00", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

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
                {
                    var fileName = System.IO.Path.GetFileName(df.FileUri);
                    var ext = System.IO.Path.GetExtension(df.FileUri)?.ToLower().TrimStart('.') ?? "";
                    combined.AppendLine($"documento: {fileName}.{ext}---{df.Text}---");
                }
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
                maxTokens: metadata.MaxTokens ?? 100000,
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
            public string Origin { get; set; }
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
                )
                return BadRequest("Datos inválidos.");

            // 1) Recupera el caso y sus archivos
            var processCase = await _db.ProcessCase
                .Include(pc => pc.DataFile)
                .FirstOrDefaultAsync(pc => pc.CaseCode == req.CaseCode);
            if (processCase == null)
                return NotFound("Caso no encontrado.");

            var usuario = await _userManager.GetUserAsync(User);

            // 2) Lógica para elegir prompt / agent según req.Origin
            //    Si Origin está vacío -> usar el prompt / agente por defecto ("chat-nexus")
            OPAIPrompt prompt = null;
            Agent agent = null;
            FinalResponseConfig config = null;
            OPAIModelPrompt promtModel = null;

            if (string.IsNullOrWhiteSpace(req.Origin))
            {
                // comportamiento actual por defecto
                // Updated line to handle possible null value by using the null-coalescing operator
                config = _db.FinalResponseConfig.FirstOrDefault(x => x.ProcessCode == "A-HOSP" && x.IsEnabled)
                         ?? throw new InvalidOperationException("FinalResponseConfig not found for ProcessCode 'A-HOSP'.");
                // Updated line to handle possible null value by using the null-coalescing operator
                prompt = await _db.OPAIPrompt.FirstOrDefaultAsync(x => x.Code == "chat-nexus")
                         ?? throw new InvalidOperationException("Prompt with code 'chat-nexus' not found.");

                // Updated the line to handle possible null value by using the null-coalescing operator
                config = _db.FinalResponseConfig.FirstOrDefault(x => x.ProcessCode == "A-HOSP" && x.IsEnabled)
                         ?? throw new InvalidOperationException("FinalResponseConfig not found for ProcessCode 'A-HOSP'.");
                // Updated line to handle possible null value by using the null-coalescing operator
                agent = await _db.Agent.Include(a => a.AgentConfig).FirstOrDefaultAsync(x => x.Code == config.AgentCode)
                        ?? throw new InvalidOperationException($"Agent not found for AgentCode '{config.AgentCode}'.");

            }
            else
            {
                // Intentar localizar un prompt con el código enviado en Origin
                prompt = await _db.OPAIPrompt.FirstOrDefaultAsync(x => x.Code == req.Origin);

                if (prompt != null)
                {
                    // Encontramos un prompt específico: usamos su contenido
                    // (aún usamos el config/agent por defecto salvo que tengas mapping adicional)
                    config = _db.FinalResponseConfig.FirstOrDefault(x => x.ProcessCode == "A-HOSP" && x.IsEnabled);
                    promtModel = _db.OPAIModelPrompt.FirstOrDefault(x => x.PromptCode ==prompt.Code);
                    agent = await _db.Agent.Include(a => a.AgentConfig).FirstOrDefaultAsync(x => x.Code == promtModel.ModelCode);
                }
                else
                {
                    // Si no hay prompt, intentar buscar un Agent con ese código
                    agent = await _db.Agent.Include(a => a.AgentConfig).FirstOrDefaultAsync(x => x.Code == req.Origin);
                    if (agent != null)
                    {
                        // Si existe un Agent con ese código, intentar obtener la FinalResponseConfig relacionado
                        config = _db.FinalResponseConfig.FirstOrDefault(x => x.AgentCode == agent.Code && x.IsEnabled)
                                 ?? _db.FinalResponseConfig.FirstOrDefault(x => x.ProcessCode == "A-HOSP" && x.IsEnabled);
                    }
                    else
                    {
                        // Fallback: usamos el prompt/agent por defecto
                        prompt = await _db.OPAIPrompt.FirstOrDefaultAsync(x => x.Code == "chat-nexus");
                        config = _db.FinalResponseConfig.FirstOrDefault(x => x.ProcessCode == "A-HOSP" && x.IsEnabled);
                        agent = await _db.Agent.Include(a => a.AgentConfig).FirstOrDefaultAsync(x => x.Code == config.AgentCode);
                    }
                }
            }

            // 3) Construir prompts para la llamada a OpenAI
            string systemPrompt = prompt?.Content ?? "Sistema por defecto ... (revisa configuraciones)";
            var userContent = req.Message;






            // Adjuntamos el texto de los DataFile del caso
            var combined = new StringBuilder();

            foreach (var df in processCase.DataFile)
            {
                var fileName = System.IO.Path.GetFileName(df.FileUri);
                var ext = System.IO.Path.GetExtension(df.FileUri)?.ToLower().TrimStart('.') ?? "";
                combined.AppendLine($"documento: {fileName}.{ext}---{df.Text}---");
            }

            var allText = combined.ToString();

            // Si el cliente envió file URLs explícitas, podemos anexarlas o hacer algo con ellas:
            if (req.FileUrls != null && req.FileUrls.Any())
            {
                userContent += "\n\nArchivos remitidos por el cliente:\n" + string.Join("\n", req.FileUrls);
            }

            userContent += $"\n\nInformación del Caso número NE-{(processCase.CaseCode.ToString()?.Split('-').FirstOrDefault() ?? "")}: Usuario que consulta: Auditor de Saldusa\n"
                           + allText;

            var metadata = !string.IsNullOrWhiteSpace(config.MetadataJson)
                ? JsonSerializer.Deserialize<FinalResponseMetadata>(config.MetadataJson)!
                : new FinalResponseMetadata();


            var aiDto = await CallOpenAiAsync(
                agent: agent,
                systemContent: systemPrompt,
                userText: userContent,
                dataFileId: processCase.DataFile.First().Id,
                stepOrder: 0,
                maxTokens: metadata.MaxTokens ?? 16000,
                temperature: 0.2,
                topP: 1.0
            );

            // 5) Guardar resultado en BD (igual que antes)
            var final = new FinalResponseResult
            {
                CaseCode = req.CaseCode,
                FileId = null,
                ConfigCode = config?.ConfigCode ?? "DEFAULT",
                ResponseText = aiDto.ResultText,
                ExecutionSummary = $"Pregunta usuario directa {usuario?.Email}",
                CreatedDate = DateTime.Now
            };
            _db.FinalResponseResult.Add(final);
            await _db.SaveChangesAsync();

            // 6) Devolver la respuesta
            return Json(new
            {
                response = aiDto.ResultText,
                timestamp = final.CreatedDate.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
            });
        }




        [HttpPost]
        public async Task<IActionResult> CreateCaseProcess([FromBody] QueryInput input)
        {
            try
            {

                if (input == null || string.IsNullOrWhiteSpace(input.ProcessCode))
                    return BadRequest("ProcessCode es obligatorio.");
                var ocrSetting = await _db.OCRSetting
                    .FirstOrDefaultAsync(x => x.SettingCode == "DEFAULT" && x.PlatformCode == "AZURE");
                if (ocrSetting == null)
                    return NotFound("Configuración OCR no encontrada.");

                var blobCfg = await _db.AzureBlobConf.AsNoTracking().FirstOrDefaultAsync()
                  ?? throw new InvalidOperationException("AzureBlobConf no encontrada.");

                var ocrTasks = input.Files.Select(f =>
                    ProcessFileAsync(f, ocrSetting, blobCfg)
                );

                var ocrResults = await Task.WhenAll(ocrTasks);

                var processDef = await _db.Process
                    .Include(p => p.ProcessStep.OrderBy(s => s.StepOrder))
                    .FirstOrDefaultAsync(p => p.Code == input.ProcessCode);
                if (processDef == null)
                    return NotFound("Proceso no encontrado.");

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
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    error = true,
                    message = ex.Message,
                });
            }
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
