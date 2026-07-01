using app_ocr_ai_models.Areas.Studio.Models;
using app_ocr_ai_models.Data;
using app_ocr_ai_models.Services;
using app_ocr_ai_models.Services.Documents;
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
    // REQ-019 T22 — Origen Armonix añadido como tercera fuente.
    // Aislado: no toca NexusController/OcrTestController/HomeController
    // ni sus vistas.  Reutiliza IZendeskClient (T3), IOcrIngestService (T2)
    // y ArmonixDocumentProvider (T22).
    // ============================================================

    /// <summary>
    /// Controller del Área Studio para gestionar la importación de sobres
    /// como Casos OCR (<see cref="ProcessCase"/> + <see cref="DataFile"/>).
    /// Soporta tres fuentes: archivo cargado, Zendesk y Armonix.
    /// </summary>
    [Area("Studio")]
    [Authorize]
    public class SobresController : Controller
    {
        private readonly IZendeskClient _zendesk;
        private readonly IOcrIngestService _ingest;
        private readonly ArmonixDocumentProvider _armonix;
        private readonly OCRDbContext _db;
        private readonly ILogger<SobresController> _logger;

        /// <summary>
        /// Constructor con inyección de dependencias.
        /// </summary>
        /// <param name="zendesk">Cliente Zendesk multi-cuenta.</param>
        /// <param name="ingest">Servicio de ingesta OCR/Blob.</param>
        /// <param name="armonix">Proveedor documental Armonix (T22).</param>
        /// <param name="db">Contexto EF de la base de datos OCR.</param>
        /// <param name="logger">Logger de la aplicación.</param>
        public SobresController(
            IZendeskClient zendesk,
            IOcrIngestService ingest,
            ArmonixDocumentProvider armonix,
            OCRDbContext db,
            ILogger<SobresController> logger)
        {
            _zendesk = zendesk;
            _ingest  = ingest;
            _armonix = armonix;
            _db      = db;
            _logger  = logger;
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
        [ValidateAntiForgeryToken] // REQ-019: CSRF fix — alineado con los demás POST del controller
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
        // REQ-019 T22 RW — Acciones para el origen Armonix (flujo simplificado)
        // ----------------------------------------------------------------

        // GET /Studio/Sobres/ImportarArmonix

        /// <summary>
        /// Muestra el formulario simplificado para buscar un sobre en Armonix.
        /// El operador solo ingresa el número de sobre o la cédula del afiliado;
        /// los identificadores de contrato se resuelven automáticamente.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ImportarArmonix()
        {
            var procesos = await _db.Process
                .Where(p => p.IsActive == true)
                .OrderBy(p => p.Name)
                .ToListAsync();

            var vm = new ImportarSobreArmonixViewModel
            {
                ProcessCode         = procesos.FirstOrDefault()?.Code ?? string.Empty,
                ProcesosDisponibles = procesos
            };
            return View(vm);
        }

        // POST /Studio/Sobres/BuscarArmonix

        /// <summary>
        /// Llama a <c>BuscarSobre</c> de api-armonix para resolver automáticamente
        /// los identificadores de contrato a partir del número de sobre o cédula.
        /// Si devuelve 1 sobre, pasa directamente a listar documentos.
        /// Si devuelve varios, muestra tabla de selección.
        /// Si devuelve 0, muestra mensaje claro.
        /// </summary>
        /// <param name="vm">Datos del formulario (solo numeroSobre o cedula + processCode).</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BuscarArmonix(ImportarSobreArmonixViewModel vm)
        {
            if (string.IsNullOrWhiteSpace(vm.NumeroSobre) && string.IsNullOrWhiteSpace(vm.Cedula))
            {
                var procesos = await _db.Process
                    .Where(p => p.IsActive == true)
                    .OrderBy(p => p.Name)
                    .ToListAsync();
                vm.ProcesosDisponibles = procesos;
                ModelState.AddModelError(string.Empty, "Debe ingresar el número de sobre o la cédula del afiliado.");
                return View("ImportarArmonix", vm);
            }

            var criterio = !string.IsNullOrWhiteSpace(vm.NumeroSobre)
                ? vm.NumeroSobre.Trim()
                : vm.Cedula!.Trim();

            IReadOnlyList<app_ocr_ai_models.Services.Documents.ArmonixSobreResueltoDto> sobres;
            try
            {
                sobres = await _armonix.BuscarSobresAsync(
                    string.IsNullOrWhiteSpace(vm.NumeroSobre) ? null : vm.NumeroSobre.Trim(),
                    string.IsNullOrWhiteSpace(vm.Cedula)      ? null : vm.Cedula.Trim());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[T22 RW] Error al buscar sobre Armonix con criterio {Criterio}.", criterio);
                var resultado = new BusquedaSobreArmonixViewModel
                {
                    CriterioBuscado = criterio,
                    ProcessCode     = vm.ProcessCode,
                    Error           = $"Error al consultar Armonix: {ex.Message}"
                };
                return View("BusquedaSobreArmonix", resultado);
            }

            // Sin resultados
            if (sobres.Count == 0)
            {
                var resultado = new BusquedaSobreArmonixViewModel
                {
                    CriterioBuscado = criterio,
                    ProcessCode     = vm.ProcessCode
                };
                return View("BusquedaSobreArmonix", resultado);
            }

            // Un solo sobre: ir directamente a listar documentos
            if (sobres.Count == 1)
            {
                return await MostrarDocumentosDeSobre(sobres[0], vm.ProcessCode);
            }

            // Varios sobres: mostrar tabla de selección
            var seleccion = new BusquedaSobreArmonixViewModel
            {
                CriterioBuscado = criterio,
                ProcessCode     = vm.ProcessCode,
                Sobres = sobres.Select(s => new SobreArmonixResueltoViewModel
                {
                    NumeroSobre           = s.NumeroSobre,
                    NombreTitular         = s.NombreTitular,
                    EstadoSobre           = s.EstadoSobre,
                    FechaRecepcion        = s.FechaRecepcion,
                    CodigoRegion          = s.CodigoRegion,
                    CodigoProducto        = s.CodigoProducto,
                    NumeroContrato        = s.NumeroContrato,
                    NumeroPersonaPaciente = s.NumeroPersonaPaciente
                }).ToList()
            };
            return View("BusquedaSobreArmonix", seleccion);
        }

        // POST /Studio/Sobres/ConfirmarSobreArmonix
        // Acción intermediaria cuando el operador elige un sobre de la tabla de selección.

        /// <summary>
        /// Recibe el sobre elegido de la tabla de selección y procede a listar
        /// sus documentos en Armonix.
        /// </summary>
        /// <param name="numeroSobre">Número del sobre seleccionado.</param>
        /// <param name="numeroContrato">Número de contrato resuelto.</param>
        /// <param name="codigoProducto">Código de producto resuelto.</param>
        /// <param name="codigoRegion">Código de región resuelto.</param>
        /// <param name="numeroPersonaPaciente">Número de persona/paciente resuelto.</param>
        /// <param name="nombreTitular">Nombre del titular para mostrar.</param>
        /// <param name="processCode">Código del Process destino.</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarSobreArmonix(
            string numeroSobre,
            string numeroContrato,
            string codigoProducto,
            string codigoRegion,
            string numeroPersonaPaciente,
            string nombreTitular,
            string processCode)
        {
            var sobre = new app_ocr_ai_models.Services.Documents.ArmonixSobreResueltoDto
            {
                NumeroSobre           = numeroSobre           ?? string.Empty,
                NumeroContrato        = numeroContrato        ?? string.Empty,
                CodigoProducto        = codigoProducto        ?? string.Empty,
                CodigoRegion          = codigoRegion          ?? string.Empty,
                NumeroPersonaPaciente = numeroPersonaPaciente ?? string.Empty,
                NombreTitular         = nombreTitular         ?? string.Empty
            };
            return await MostrarDocumentosDeSobre(sobre, processCode);
        }

        // POST /Studio/Sobres/ImportarDesdeArmonix

        /// <summary>
        /// Importa los documentos del sobre desde Armonix como un <see cref="ProcessCase"/> nuevo.
        /// Los identificadores de contrato provienen de los campos hidden resueltos por <c>BuscarSobre</c>.
        /// Descarga el binario base64, ejecuta OCR con <see cref="IOcrIngestService"/>
        /// y crea los <see cref="DataFile"/> correspondientes.
        /// </summary>
        /// <param name="numeroSobre">Número del sobre de reembolso (ya resuelto).</param>
        /// <param name="numeroContrato">Número de contrato resuelto.</param>
        /// <param name="codigoProducto">Código de producto resuelto.</param>
        /// <param name="codigoRegion">Código de región resuelto.</param>
        /// <param name="numeroPersonaPaciente">Número de persona/paciente resuelto.</param>
        /// <param name="processCode">Código del Process destino.</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportarDesdeArmonix(
            string numeroSobre,
            string numeroContrato,
            string codigoProducto,
            string codigoRegion,
            string numeroPersonaPaciente,
            string processCode)
        {
            // ── 1. Validar el Process
            var process = await _db.Process.FindAsync(processCode);
            if (process == null)
            {
                TempData["Error"] = $"El proceso '{processCode}' no existe.";
                return RedirectToAction(nameof(ImportarArmonix));
            }

            // ── 2. Crear el ProcessCase
            var newCase = new ProcessCase
            {
                CaseCode       = Guid.NewGuid(),
                DefinitionCode = processCode,
                StartDate      = DateTime.UtcNow,
                State          = "Started"
            };
            _db.ProcessCase.Add(newCase);
            await _db.SaveChangesAsync();

            // ── 3. Importar documentos desde Armonix → OCR → DataFile
            var filter = new SobreDocumentosFilter
            {
                NumeroSobre           = numeroSobre?.Trim()           ?? string.Empty,
                NumeroContrato        = numeroContrato?.Trim(),
                CodigoProducto        = codigoProducto?.Trim(),
                CodigoRegion          = codigoRegion?.Trim(),
                NumeroPersonaPaciente = numeroPersonaPaciente?.Trim()
            };

            ImportarDocumentosResult importResult;
            try
            {
                importResult = await _armonix.ImportarDocumentosAsync(filter, newCase, _db);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[T22 RW] Error al importar documentos Armonix para sobre {NumeroSobre}.", numeroSobre);
                newCase.State = "ImportError";
                await _db.SaveChangesAsync();
                TempData["Error"] = $"Error al importar desde Armonix: {ex.Message}";
                return RedirectToAction(nameof(ImportarArmonix));
            }

            // ── 4. Actualizar estado del caso según resultado
            if (importResult.DataFileIds.Count == 0)
            {
                newCase.State = importResult.Advertencias.Count > 0
                    ? "ImportedWithWarnings"
                    : "ImportedEmpty";
                await _db.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Caso), new { caseCode = newCase.CaseCode });
        }

        // ── Helper privado: llama BuscarDocumentos y devuelve la vista de documentos

        /// <summary>
        /// Llama a Armonix para listar los documentos de un sobre ya resuelto
        /// y devuelve la vista de previsualización.
        /// </summary>
        private async Task<IActionResult> MostrarDocumentosDeSobre(
            app_ocr_ai_models.Services.Documents.ArmonixSobreResueltoDto sobre,
            string processCode)
        {
            var resultado = new VistaDocumentosArmonixViewModel
            {
                NumeroSobre           = sobre.NumeroSobre,
                NombreTitular         = sobre.NombreTitular,
                NumeroContrato        = sobre.NumeroContrato,
                CodigoProducto        = sobre.CodigoProducto,
                CodigoRegion          = sobre.CodigoRegion,
                NumeroPersonaPaciente = sobre.NumeroPersonaPaciente,
                ProcessCode           = processCode
            };

            try
            {
                var filter = new SobreDocumentosFilter
                {
                    NumeroSobre           = sobre.NumeroSobre,
                    NumeroContrato        = sobre.NumeroContrato,
                    CodigoProducto        = sobre.CodigoProducto,
                    CodigoRegion          = sobre.CodigoRegion,
                    NumeroPersonaPaciente = sobre.NumeroPersonaPaciente
                };

                var documentos = await _armonix.ListarDocumentosAsync(filter);
                resultado.DocumentosDisponibles = documentos;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[T22 RW] Error al listar documentos Armonix para sobre {NumeroSobre}.", sobre.NumeroSobre);
                resultado.Error = $"Error al consultar Armonix: {ex.Message}";
            }

            return View("VistaDocumentosArmonix", resultado);
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
