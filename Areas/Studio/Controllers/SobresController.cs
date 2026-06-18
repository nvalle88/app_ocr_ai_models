using app_ocr_ai_models.Areas.Studio.Models;
using app_ocr_ai_models.Data;
using app_ocr_ai_models.Services;
using app_ocr_ai_models.Services.Zendesk;
using app_tramites.Models.ModelAi;
using app_tramites.Models.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace app_ocr_ai_models.Areas.Studio.Controllers
{
    // ============================================================
    // REQ-019 T4 — Área Studio: flujo Importar sobre → Caso OCR.
    // Aislado: no toca NexusController/OcrTestController/HomeController
    // ni sus vistas.  Reutiliza IZendeskClient (T3) e IOcrIngestService (T2).
    // ============================================================

    /// <summary>
    /// Controller del Área Studio para gestionar la importación de sobres Zendesk
    /// como Casos OCR (<see cref="ProcessCase"/> + <see cref="DataFile"/>).
    /// </summary>
    [Area("Studio")]
    [Authorize]
    public class SobresController : Controller
    {
        private readonly IZendeskClient _zendesk;
        private readonly IOcrIngestService _ingest;
        private readonly OCRDbContext _db;
        private readonly ILogger<SobresController> _logger;

        /// <summary>
        /// Constructor con inyección de dependencias.
        /// </summary>
        /// <param name="zendesk">Cliente Zendesk multi-cuenta.</param>
        /// <param name="ingest">Servicio de ingesta OCR/Blob.</param>
        /// <param name="db">Contexto EF de la base de datos OCR.</param>
        /// <param name="logger">Logger de la aplicación.</param>
        public SobresController(
            IZendeskClient zendesk,
            IOcrIngestService ingest,
            OCRDbContext db,
            ILogger<SobresController> logger)
        {
            _zendesk = zendesk;
            _ingest = ingest;
            _db = db;
            _logger = logger;
        }

        // ----------------------------------------------------------------
        // GET /Studio/Sobres/Index  — Formulario de búsqueda
        // ----------------------------------------------------------------

        /// <summary>
        /// Muestra el formulario para buscar/importar un sobre Zendesk.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var procesos = await _db.Process
                .Where(p => p.IsActive == true)
                .OrderBy(p => p.Name)
                .ToListAsync();

            var vm = new ImportarSobreViewModel
            {
                ProcessCode = procesos.FirstOrDefault()?.Code ?? string.Empty,
                ProcesosDisponibles = procesos
            };
            return View(vm);
        }

        // ----------------------------------------------------------------
        // POST /Studio/Sobres/Buscar  — Busca el ticket en Zendesk
        // ----------------------------------------------------------------

        /// <summary>
        /// Busca un ticket por número de sobre o cédula y muestra los resultados
        /// para que el usuario confirme cuál importar.
        /// </summary>
        /// <param name="vm">Datos del formulario de búsqueda.</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Buscar(ImportarSobreViewModel vm)
        {
            var resultado = new BusquedaSobreResultViewModel
            {
                ProcessCode = vm.ProcessCode,
                NumeroSobreBuscado = vm.NumeroSobre
            };

            if (string.IsNullOrWhiteSpace(vm.NumeroSobre))
            {
                resultado.Error = "Debe ingresar el número de sobre para buscar.";
                return View("BusquedaResultado", resultado);
            }

            try
            {
                var resultados = await _zendesk.BuscarPorNumeroSobreAsync(vm.NumeroSobre.Trim());
                resultado.Resultados = resultados;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al buscar sobre {NumeroSobre} en Zendesk.", vm.NumeroSobre);
                resultado.Error = $"Error al conectar con Zendesk: {ex.Message}";
            }

            return View("BusquedaResultado", resultado);
        }

        // ----------------------------------------------------------------
        // GET /Studio/Sobres/Confirmar?ticketId=&cuenta=&processCode=
        // Muestra el detalle del ticket + adjuntos antes de importar
        // ----------------------------------------------------------------

        /// <summary>
        /// Muestra el detalle del ticket Zendesk y sus adjuntos antes de confirmar
        /// la importación como Caso OCR.
        /// </summary>
        /// <param name="ticketId">ID del ticket en Zendesk.</param>
        /// <param name="cuenta">Cuenta Zendesk donde reside el ticket.</param>
        /// <param name="processCode">Código del Process al que se asignará el caso.</param>
        [HttpGet]
        public async Task<IActionResult> Confirmar(long ticketId, ZendeskCuenta cuenta, string processCode)
        {
            try
            {
                var ticket = await _zendesk.LeerTicketAsync(ticketId, cuenta);
                if (ticket == null)
                {
                    TempData["Error"] = $"Ticket #{ticketId} no encontrado en la cuenta {cuenta}.";
                    return RedirectToAction(nameof(Index));
                }

                var comentarios = await _zendesk.ObtenerComentariosAsync(ticketId, cuenta);

                var vm = new ConfirmarImportViewModel
                {
                    Ticket = ticket,
                    Cuenta = cuenta,
                    Comentarios = comentarios,
                    ProcessCode = processCode
                };
                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al leer ticket #{TicketId} en cuenta {Cuenta}.", ticketId, cuenta);
                TempData["Error"] = $"Error al leer el ticket: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // ----------------------------------------------------------------
        // POST /Studio/Sobres/Importar
        // Ejecuta la importación real: descarga adjuntos → OCR → ProcessCase + DataFile
        // ----------------------------------------------------------------

        /// <summary>
        /// Importa el ticket Zendesk como un <see cref="ProcessCase"/> nuevo,
        /// ejecutando OCR sobre cada adjunto y creando los <see cref="DataFile"/> correspondientes.
        /// Los comentarios del ticket se persisten como <see cref="Note"/> del caso.
        /// </summary>
        /// <param name="ticketId">ID del ticket en Zendesk.</param>
        /// <param name="cuenta">Cuenta Zendesk donde reside el ticket.</param>
        /// <param name="processCode">Código del Process (definición de caso).</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Importar(long ticketId, ZendeskCuenta cuenta, string processCode)
        {
            // ── 1. Validar que el Process existe
            var process = await _db.Process.FindAsync(processCode);
            if (process == null)
            {
                TempData["Error"] = $"El proceso '{processCode}' no existe.";
                return RedirectToAction(nameof(Index));
            }

            // ── 2. Leer el ticket + comentarios desde Zendesk
            ZendeskTicketDto ticket;
            IReadOnlyList<ZendeskComentarioDto> comentarios;
            try
            {
                var t = await _zendesk.LeerTicketAsync(ticketId, cuenta);
                if (t == null)
                {
                    TempData["Error"] = $"Ticket #{ticketId} no encontrado en cuenta {cuenta}.";
                    return RedirectToAction(nameof(Index));
                }
                ticket = t;
                comentarios = await _zendesk.ObtenerComentariosAsync(ticketId, cuenta);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al leer ticket #{TicketId} para importar.", ticketId);
                TempData["Error"] = $"Error al leer el ticket en Zendesk: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }

            // ── 3. Crear ProcessCase
            var newCase = new ProcessCase
            {
                CaseCode = Guid.NewGuid(),
                DefinitionCode = processCode,
                StartDate = DateTime.UtcNow,
                State = "Started"
            };
            _db.ProcessCase.Add(newCase);
            await _db.SaveChangesAsync();

            // ── 4. Persistir comentarios como Notes del caso
            var notas = new List<Note>();
            foreach (var comentario in comentarios)
            {
                if (string.IsNullOrWhiteSpace(comentario.Body))
                {
                    continue;
                }

                var nota = new Note
                {
                    CaseCode = newCase.CaseCode,
                    Title = $"Comentario Zendesk #{comentario.Id} ({(comentario.IsPublic ? "público" : "privado")})",
                    Detail = comentario.Body,
                    CreatedAt = comentario.CreatedAt.UtcDateTime,
                    CreatedBy = $"zendesk-import|ticket:{ticketId}|author:{comentario.AuthorId}"
                };
                notas.Add(nota);
                _db.Note.Add(nota);
            }
            if (notas.Count > 0)
            {
                await _db.SaveChangesAsync();
            }

            // ── 5. Descargar adjuntos + OCR + crear DataFile
            var advertencias = new List<string>();
            var dataFileIds = new List<int>();

            var todosLosAdjuntos = comentarios
                .SelectMany(c => c.Attachments)
                .ToList();

            foreach (var adjunto in todosLosAdjuntos)
            {
                try
                {
                    var extension = Path.GetExtension(adjunto.FileName);
                    if (string.IsNullOrEmpty(extension))
                    {
                        extension = ObtenerExtensionPorMime(adjunto.ContentType);
                    }

                    var fileUrl = string.Empty;
                    var ocrText = string.Empty;

                    // Descargar y procesar con OCR
                    try
                    {
                        await using var ocrStream = await _zendesk.DescargarAdjuntoAsync(adjunto.ContentUrl, cuenta);
                        using var ms = new MemoryStream();
                        await ocrStream.CopyToAsync(ms);
                        var base64 = Convert.ToBase64String(ms.ToArray());

                        var ocrFile = new OcrFile
                        {
                            FileName = adjunto.FileName,
                            Content = base64,
                            Extension = extension
                        };

                        var (blobUrl, texto) = await _ingest.ProcessFileAsync(ocrFile);
                        fileUrl = blobUrl;
                        ocrText = texto;
                    }
                    catch (Exception ocrEx)
                    {
                        _logger.LogWarning(ocrEx, "OCR falló para adjunto {FileName}; se sube sólo el blob.", adjunto.FileName);
                        advertencias.Add($"OCR no disponible para '{adjunto.FileName}': {ocrEx.Message}");

                        // Fallback: subir stream sin OCR
                        await using var rawStream = await _zendesk.DescargarAdjuntoAsync(adjunto.ContentUrl, cuenta);
                        fileUrl = await _ingest.UploadFileAsync(rawStream, extension);
                    }

                    var dataFile = new DataFile
                    {
                        IsFileUri = true,
                        FileUri = fileUrl,
                        Text = ocrText,
                        CaseCode = newCase.CaseCode,
                        CreatedDate = DateTime.UtcNow,
                        OriginalName = adjunto.FileName
                    };
                    _db.DataFile.Add(dataFile);
                    await _db.SaveChangesAsync();
                    dataFileIds.Add(dataFile.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error al procesar adjunto {FileName} del ticket #{TicketId}.", adjunto.FileName, ticketId);
                    advertencias.Add($"No se pudo procesar '{adjunto.FileName}': {ex.Message}");
                }
            }

            // ── 6. Actualizar estado del caso según resultado
            if (todosLosAdjuntos.Count == 0)
            {
                newCase.State = "ImportedEmpty";
                await _db.SaveChangesAsync();
            }
            else if (advertencias.Count > 0 && dataFileIds.Count == 0)
            {
                newCase.State = "ImportedWithWarnings";
                await _db.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Caso), new { caseCode = newCase.CaseCode });
        }

        // ----------------------------------------------------------------
        // GET /Studio/Sobres/Caso/{caseCode}
        // Vista del caso importado con sus DataFiles
        // ----------------------------------------------------------------

        /// <summary>
        /// Muestra el Caso OCR importado con todos sus <see cref="DataFile"/> y notas.
        /// </summary>
        /// <param name="caseCode">Identificador del caso.</param>
        [HttpGet]
        public async Task<IActionResult> Caso(Guid caseCode)
        {
            var caso = await _db.ProcessCase
                .Include(c => c.DataFile)
                .Include(c => c.Notes)
                .FirstOrDefaultAsync(c => c.CaseCode == caseCode);

            if (caso == null)
            {
                TempData["Error"] = $"Caso {caseCode} no encontrado.";
                return RedirectToAction(nameof(Index));
            }

            var vm = new CasoImportadoViewModel
            {
                Caso = caso,
                Archivos = caso.DataFile.OrderBy(f => f.CreatedDate).ToList(),
                Notas = caso.Notes.OrderBy(n => n.CreatedAt).ToList(),
                ProcessCode = caso.DefinitionCode,
                Mensaje = TempData["Mensaje"]?.ToString()
            };
            return View(vm);
        }

        // ----------------------------------------------------------------
        // POST /Studio/Sobres/AdjuntarDocumentos  (JSON API)
        // Permite adjuntar más documentos manualmente a un caso ya importado
        // ----------------------------------------------------------------

        /// <summary>
        /// Adjunta documentos adicionales (en base64) a un caso ya existente.
        /// Cada archivo pasa por <see cref="IOcrIngestService"/> y se crea un <see cref="DataFile"/>.
        /// </summary>
        /// <param name="request">Solicitud con CaseCode y lista de archivos base64.</param>
        [HttpPost]
        public async Task<IActionResult> AdjuntarDocumentos([FromBody] AdjuntarDocumentosRequest request)
        {
            if (request.Archivos.Count == 0)
            {
                return Ok(new AdjuntarDocumentosResponse
                {
                    Ok = false,
                    Error = "No se enviaron archivos."
                });
            }

            var caso = await _db.ProcessCase.FindAsync(request.CaseCode);
            if (caso == null)
            {
                return Ok(new AdjuntarDocumentosResponse
                {
                    Ok = false,
                    Error = $"Caso {request.CaseCode} no encontrado."
                });
            }

            var dataFileIds = new List<int>();
            var advertencias = new List<string>();

            foreach (var archivo in request.Archivos)
            {
                try
                {
                    var ocrFile = new OcrFile
                    {
                        FileName = archivo.FileName,
                        Content = archivo.ContentBase64,
                        Extension = archivo.Extension
                    };

                    var (blobUrl, texto) = await _ingest.ProcessFileAsync(ocrFile);

                    var dataFile = new DataFile
                    {
                        IsFileUri = true,
                        FileUri = blobUrl,
                        Text = texto,
                        CaseCode = request.CaseCode,
                        CreatedDate = DateTime.UtcNow,
                        OriginalName = archivo.FileName
                    };
                    _db.DataFile.Add(dataFile);
                    await _db.SaveChangesAsync();
                    dataFileIds.Add(dataFile.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error al adjuntar documento {FileName} al caso {CaseCode}.", archivo.FileName, request.CaseCode);
                    advertencias.Add($"'{archivo.FileName}': {ex.Message}");
                }
            }

            return Ok(new AdjuntarDocumentosResponse
            {
                Ok = dataFileIds.Count > 0,
                DataFileIds = dataFileIds,
                Advertencias = advertencias
            });
        }

        // ----------------------------------------------------------------
        // Utilidades privadas
        // ----------------------------------------------------------------

        /// <summary>
        /// Determina una extensión de archivo a partir del MIME type cuando el nombre
        /// del archivo no trae extensión.
        /// </summary>
        /// <param name="contentType">MIME type del adjunto.</param>
        /// <returns>Extensión con punto (p. ej. ".pdf"), o ".bin" por defecto.</returns>
        private static string ObtenerExtensionPorMime(string contentType) =>
            contentType?.ToLowerInvariant() switch
            {
                "application/pdf" => ".pdf",
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/tiff" => ".tiff",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
                "application/msword" => ".doc",
                _ => ".bin"
            };
    }
}
