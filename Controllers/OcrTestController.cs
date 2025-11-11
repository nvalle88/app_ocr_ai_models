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

#endregion

namespace SmartAdmin.Web.Controllers
{
    [Authorize]
    public class OcrTestController : Controller
    {
        private readonly OCRDbContext db;

        public OcrTestController(OCRDbContext context)
        {
            db = context;
        }


        public async Task<IActionResult> Index()
        {
            ViewBag.Process = new SelectList(await db.Process.OrderBy(a => a.Code).Select(a =>
                new SelectListItem
                {
                    Value = a.Code,
                    Text = $"({a.Code}-{a.Description})"
                }).ToListAsync(), "Value", "Text", null);

            return View(new QueryInput());

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


        [HttpPost]
        public async Task<IActionResult> Index([FromBody] QueryInput input)
        {
            try
            {
                // 1. Definir proceso
                var processDefinition = await db.Process
                    .Include(p => p.ProcessStep.OrderBy(s => s.StepOrder))
                    .FirstOrDefaultAsync(p => p.Code == input.ProcessCode);
                if (processDefinition == null)
                    return NotFound("Proceso no encontrado.");

                // 2. Configuración OCR
                var ocrSetting = await db.OCRSetting
                    .FirstOrDefaultAsync(x => x.SettingCode == "DEFAULT" && x.PlatformCode == "AZURE");
                if (ocrSetting == null)
                    return NotFound("Configuración OCR no encontrada.");

                // 3. Crear ProcessCase
                var caseCode = Guid.NewGuid();
                db.ProcessCase.Add(new ProcessCase
                {
                    CaseCode = caseCode,
                    DefinitionCode = processDefinition.Code,
                    StartDate = DateTime.UtcNow,
                    State = "Started"
                });
                await db.SaveChangesAsync();

                // 4. Configuración Azure Blob
                var blobCfg = await db.AzureBlobConf.AsNoTracking().FirstOrDefaultAsync()
                    ?? throw new InvalidOperationException("AzureBlobConf no encontrada.");

                // 5. Procesar OCR paralelo
                var ocrTasks = input.Files.Select(f => ProcessFileAsync(f, ocrSetting, blobCfg));
                var ocrResults = await Task.WhenAll(ocrTasks);

                // 6. Guardar DataFiles
                var dataFiles = ocrResults.Select(r => new DataFile
                {
                    CaseCode = caseCode,
                    IsFileUri = !string.IsNullOrEmpty(r.Url),
                    FileUri = r.Url,
                    Text = r.Text,
                    CreatedDate = DateTime.UtcNow
                }).ToList();
                db.DataFile.AddRange(dataFiles);
                await db.SaveChangesAsync();

                //// 7. Inicializar historial
                //var outputsHistory = dataFiles.ToDictionary(
                //    df => df.Id,
                //    df => new OutputHistory { OriginalText = df.Text }
                //);

                //// 8. Ejecutar pasos
                //foreach (var step in processDefinition.ProcessStep.OrderBy(s => s.StepOrder))
                //{
                //    var agent = await db.Agent
                //        .Include(a => a.OPAIModelPrompt)
                //        .Include(a => a.AgentConfig)
                //        .FirstOrDefaultAsync(a => a.Code == step.ModelCode);
                //    if (agent == null) continue;

                //    var promptCodes = agent.OPAIModelPrompt.Select(p => p.PromptCode).ToList();
                //    var prompts = await db.OPAIPrompt.Where(p => promptCodes.Contains(p.Code)).ToListAsync();
                //    var systemContent = string.Join("\n", prompts.Select(p => p.Content));

                //    if (step.AggregateExecution)
                //    {
                //        // Ejecutar de forma agregada con lógica por documento
                //        var inputsGrouped = outputsHistory.Values.Select(history =>
                //        {
                //            var inputs = new List<string>();

                //            if (step.SourceType != InputSourceType.PreviousSteps)
                //                inputs.Add(history.OriginalText);

                //            if (step.SourceType != InputSourceType.Original)
                //            {
                //                var prev = history.GeneratedSteps;
                //                int cnt = step.StepsToInclude > 0
                //                    ? Math.Min(step.StepsToInclude, prev.Count)
                //                    : prev.Count;
                //                inputs.AddRange(prev.TakeLast(cnt));
                //            }

                //            return inputs;
                //        });

                //        var allInputs = inputsGrouped.SelectMany(x => x).ToList();

                //        // Si no hay entradas, agregar marcador
                //        if (allInputs.Count == 0)
                //            allInputs.Add("Sin entradas disponibles.");

                //        var combinedInput = string.Join("\n", allInputs);

                //        var response = await CallOpenAiAsync(
                //            agent, systemContent, combinedInput,
                //            dataFileId: dataFiles.First().Id, // Agregado, no por documento
                //            step.StepOrder
                //        );

                //        var exec = new StepExecution
                //        {
                //            CaseCode = caseCode,
                //            StepOrder = step.StepOrder,
                //            DataFileId = dataFiles.First().Id,
                //            RequestContent = combinedInput,
                //            ResponseContent = response.ResultText,
                //            Status = "Completed",
                //            StartDate = response.StartedAt,
                //            EndDate = response.FinishedAt,
                //            ModelCode = agent.Code
                //        };
                //        db.StepExecution.Add(exec);
                //        await db.SaveChangesAsync();

                //        var usage = new Usage
                //        {
                //            ExecutionId = exec.ExecutionId,
                //            PromptTokens = response.PromptTokens,
                //            CompletionTokens = response.CompletionTokens,
                //            CreatedDate = DateTime.UtcNow
                //        };
                //        db.Usage.Add(usage);
                //        await db.SaveChangesAsync();

                //        // Guardar resultado en todos los documentos
                //        foreach (var history in outputsHistory.Values)
                //            history.GeneratedSteps.Add(response.ResultText);
                //    }
                //    else
                //    {
                //        // Ejecución por documento
                //        var callTasksDoc = dataFiles.Select(df =>
                //        {
                //            var history = outputsHistory[df.Id];
                //            var parts = new List<string>();

                //            // Siempre incluir original si SourceType lo requiere
                //            if (step.SourceType == InputSourceType.Original || step.SourceType == InputSourceType.Both)
                //                parts.Add(history.OriginalText);

                //            // Incluir previos si SourceType lo requiere
                //            if (step.SourceType == InputSourceType.PreviousSteps || step.SourceType == InputSourceType.Both)
                //            {
                //                var prev = history.GeneratedSteps;
                //                // Tomar solo hasta StepsToInclude últimos, o todos si es 0
                //                int cnt = step.StepsToInclude > 0
                //                    ? Math.Min(step.StepsToInclude, prev.Count)
                //                    : prev.Count;
                //                if (cnt > 0)
                //                    parts.AddRange(prev.TakeLast(cnt));
                //            }

                //            // Si no se añadió nada (caso borde), usar original
                //            if (parts.Count == 0)
                //                parts.Add(history.OriginalText);

                //            var inputText = string.Join("", parts);
                //            return CallOpenAiAsync(
                //                agent, systemContent, inputText,
                //                df.Id, step.StepOrder);
                //        });


                //        var responses = await Task.WhenAll(callTasksDoc);

                //        // Guardar en lote
                //        var executions = responses.Select(res => new StepExecution
                //        {
                //            CaseCode = caseCode,
                //            StepOrder = res.StepOrder,
                //            DataFileId = res.DataFileId,
                //            RequestContent = res.RequestJson,
                //            ResponseContent = res.ResultText,
                //            Status = "Completed",
                //            StartDate = res.StartedAt,
                //            EndDate = res.FinishedAt,
                //            ModelCode = agent.Code
                //        }).ToList();
                //        db.StepExecution.AddRange(executions);
                //        await db.SaveChangesAsync();

                //        var usages = executions.Zip(responses, (exec, res) => new Usage
                //        {
                //            ExecutionId = exec.ExecutionId,
                //            PromptTokens = res.PromptTokens,
                //            CompletionTokens = res.CompletionTokens,
                //            CreatedDate = DateTime.UtcNow
                //        }).ToList();
                //        db.Usage.AddRange(usages);
                //        await db.SaveChangesAsync();

                //        foreach (var res in responses)
                //            outputsHistory[res.DataFileId].GeneratedSteps.Add(res.ResultText);
                //    }
                //}

                //// 9. Generar Nexus
                //var allExec = await db.StepExecution
                //    .Where(e => e.CaseCode == caseCode)
                //    .OrderBy(e => e.StepOrder).ThenBy(e => e.DataFileId)
                //    .ToListAsync();
                var sb = new StringBuilder();
                sb.AppendLine("# Nexus\n");
                //foreach (var grp in allExec.GroupBy(e => e.StepOrder))
                //{
                //    sb.AppendLine($"## Paso {grp.Key}");
                //    foreach (var it in grp)
                //    {
                //        sb.AppendLine($"- **Modelo:** {it.ModelCode}");
                //        sb.AppendLine($"- **Archivo ID:** {it.DataFileId}");
                //        sb.AppendLine($"- **Estado:** {it.Status}");
                //        sb.AppendLine($"- **Inicio:** {it.StartDate:u}");
                //        sb.AppendLine($"- **Fin:** {it.EndDate:u}\n");
                //        sb.AppendLine("```");
                //        sb.AppendLine(it.ResponseContent);
                //        sb.AppendLine("```");
                //        sb.AppendLine();
                //    }
                //}


                var finalConfig = await db.FinalResponseConfig
                    .FirstOrDefaultAsync(cfg => cfg.ProcessCode == processDefinition.Code && cfg.IsEnabled);
                if (finalConfig != null)
                {
                    // Modo configurado: usa FinalResponseConfig
                    var finalAgent = await db.Agent.Include(x=>x.AgentConfig)
                        .FirstOrDefaultAsync(a => a.Code == finalConfig.AgentCode);
                    if (finalAgent != null)
                    {
                        // Construir prompt con placeholders
                        string prompt = finalConfig.PromptTemplate
                            .Replace("{FileCount}", finalConfig.IncludeFileCount ? dataFiles.Count.ToString() : "")
                            .Replace("{StepNames}", finalConfig.IncludeStepNames
                                ? string.Join(", ", processDefinition.ProcessStep.OrderBy(s => s.StepOrder)
                                    .Select(s => s.StepName ?? s.StepOrder.ToString()))
                                : "");

                        // Deserializar metadata JSON
                        var metadata = !string.IsNullOrWhiteSpace(finalConfig.MetadataJson)
                            ? JsonSerializer.Deserialize<FinalResponseMetadata>(finalConfig.MetadataJson)!
                            : new FinalResponseMetadata();
                        if (!string.IsNullOrWhiteSpace(metadata.CustomInstructions))
                            prompt += "" + metadata.CustomInstructions;

                        // Recopilar resultados según configuración y UseOriginalText

                        var combinedResults = new StringBuilder();

                        if (finalConfig.UseOriginalText)
                        {
                            // Usar texto original de cada DataFile
                            foreach (var df in dataFiles)
                            {
                                combinedResults.AppendLine(df.Text);
                            }
                        }
                        else
                        {
                            // Agregar resultados de StepExecution para cada paso incluido
                            var includedOrders = finalConfig.IncludedStepOrders == "*"
                                ? processDefinition.ProcessStep.Select(s => s.StepOrder).ToList()
                                : finalConfig.IncludedStepOrders.Split(',').Select(int.Parse).ToList();

                            foreach (var stepOrder in includedOrders)
                            {
                                combinedResults.AppendLine($"## Paso {stepOrder}");
                                var stepResults = await db.StepExecution
                                    .Where(e => e.CaseCode == caseCode && e.StepOrder == stepOrder)
                                    .OrderBy(e => e.DataFileId)
                                    .Select(e => e.ResponseContent)
                                    .ToListAsync();
                                combinedResults.AppendLine(string.Join("", stepResults));
                            }
                        }

                        // Llamada final a OpenAI
                        var finalResponse = await CallOpenAiAsync(
                            finalAgent,
                            prompt,
                            combinedResults.ToString(),
                            dataFiles.First().Id,
                            stepOrder: 999,
                            maxTokens: metadata.MaxTokens ?? 1000,
                            temperature: metadata.Temperature ?? 0.2,
                            topP: 1.0
                        );

                        // Guardar resultado final
                        db.FinalResponseResult.Add(new FinalResponseResult
                        {
                            CaseCode = caseCode,
                            ResponseText = finalResponse.ResultText,
                            CreatedDate = DateTime.UtcNow
                        });
                        await db.SaveChangesAsync();

                        sb.AppendLine("## Resumen Final");
                        sb.AppendLine("```");
                        sb.AppendLine(finalResponse.ResultText);
                        sb.AppendLine("```");
                    }
                }
                else
                {
                    // Modo por defecto: tomar todas las ejecuciones del último paso
                    var lastStepOrder = processDefinition.ProcessStep.Max(s => s.StepOrder);
                    var lastResponses = await db.StepExecution
                        .Where(e => e.CaseCode == caseCode && e.StepOrder == lastStepOrder)
                        .OrderBy(e => e.DataFileId)
                        .ToListAsync();

                    sb.AppendLine("## Respuestas del último paso");
                    sb.AppendLine("```");
                    foreach (var resp in lastResponses)
                    {
                        sb.AppendLine($"- Archivo {resp.DataFileId}: {resp.ResponseContent}");
                    }
                    sb.AppendLine("```");
                }

                // 11. Retornar Nexus + resumen final
                return Ok(new { caseCode, result = sb.ToString() });

            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
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
            var startedAt = DateTime.UtcNow;

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

            var finishedAt = DateTime.UtcNow;

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
            var config = await db.AzureBlobConf
                .FirstOrDefaultAsync();

            var blobServiceClient = new BlobServiceClient(config.ConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(config.ContainerName);

            await containerClient.CreateIfNotExistsAsync();

            var name = $"{Guid.NewGuid()}{extension}";
            var blobClient = containerClient.GetBlobClient(name);
            await blobClient.UploadAsync(fileStream, overwrite: true);

            return blobClient.Uri.ToString();
        }


        public async Task<IActionResult> Manage(string id)
        {
            try
            {
                ViewBag.accion = string.IsNullOrEmpty(id) == true ? "Crete" : "Edit";

                if (!string.IsNullOrEmpty(id))
                {
                    var record = await db.OCRPlatform.FirstOrDefaultAsync(c => c.PlatformCode == id);
                    if (record == null)
                        return this.RedirectTo($"{Mensaje.Error}|{Mensaje.RecordNotFound}");

                    return View(record);
                }
                return View(new OCRPlatform());
            }
            catch (Exception)
            {
                return this.RedirectTo($"{Mensaje.Error}|{Mensaje.ErrorLoadData}");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Manage(OCRPlatform platform)
        {
            try
            {
                ViewBag.accion = string.IsNullOrEmpty(platform.PlatformCode) == true ? "create" : "Edit";
                if (!ModelState.IsValid)
                {
                    TempData["Mensaje"] = $"{Mensaje.Error}|{Mensaje.FixForm}";
                    return View(platform);
                }
                var code = platform.PlatformCode.ToUpper().Trim();
                var currentPlatform = await db.OCRPlatform.Where(c => c.PlatformCode.ToUpper().Trim() == code).FirstOrDefaultAsync();

                if (currentPlatform == null)
                {
                    platform.CreatedAt = DateTime.Now;
                    await db.OCRPlatform.AddAsync(platform);
                }
                else
                {
                    currentPlatform.Name = platform.Name;
                    currentPlatform.PricePerPage = platform.PricePerPage;
                    currentPlatform.Description = platform.Description;
                    currentPlatform.IsActive = platform.IsActive;
                    currentPlatform.MaxFileSizeMB = platform.MaxFileSizeMB;
                    currentPlatform.LanguageSupport = platform.LanguageSupport;
                    db.OCRPlatform.Update(currentPlatform);

                }
                var d = await db.SaveChangesAsync();
                return this.RedirectTo($"{Mensaje.MessaggeOK}|{Mensaje.Satisfactory}");

            }
            catch (Exception ex)
            {
                return this.RedirectTo($"{Mensaje.Error}|{Mensaje.Excepcion}");
            }
        }


        [HttpGet]
        public async Task<JsonResult> Delete(string id)
        {
            try
            {
                var record = await db.OCRPlatform.FindAsync(id);
                if (record == null)
                {
                    return Json(new
                    {
                        Estado = Constantes.ErrorState,
                        Mensaje = Mensaje.RecordNotFound
                    });
                }

                db.OCRPlatform.Remove(record);
                await db.SaveChangesAsync();
                this.TempData["Mensaje"] = $"{Mensaje.MessaggeOK}|  {Mensaje.Satisfactory}";
                return Json(new
                {
                    Estado = Constantes.OKState,
                    Mensaje = Mensaje.Satisfactory
                });

            }
            catch (Exception ex)
            {
                return Json(new
                {
                    Estado = Constantes.ErrorState,
                    Mensaje = ex.Message
                });
            }
        }
    }
}
