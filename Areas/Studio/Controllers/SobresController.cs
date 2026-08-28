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
        // GET /Studio/Sobres/Bandeja — bandeja de casos (análisis realizados)
        // ----------------------------------------------------------------

        /// <summary>Lista los casos OCR ya trabajados (sobre, cliente, estado, docs, resolución) con filtros.</summary>
        [HttpGet]
        public async Task<IActionResult> Bandeja(string? search, string? estado, int page = 1)
        {
            const string CTX  = app_tramites.Services.Ai.OcrPromptHelper.ContextoSobreNoteTitle;
            const string RESO = app_tramites.Services.Ai.OcrPromptHelper.ResolucionReembolsoNoteTitle;
            const int pageSize = 15;
            if (page < 1) page = 1;

            // Sin .Include(): lo que hace falta de las relaciones se pide en la
            // proyección de más abajo. Un .Include() aquí obligaría a materializar
            // el grafo completo (documentos con su texto OCR, todas las notas) para
            // luego usar tres campos.
            var q = _db.ProcessCase.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(estado))
                q = q.Where(pc => pc.State == estado);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();

                // El EXISTS sobre Notes se resuelve APARTE, no dentro del OR.
                //
                // Con las tres condiciones unidas por OR, SQL Server no puede
                // resolver el EXISTS como semi-join y lo evalúa fila por fila como
                // un booleano: medido, 94.064 lecturas lógicas y ~3,5 s para
                // devolver cero resultados. El mismo EXISTS solo, contra la misma
                // tabla y sin ningún índice nuevo, son 16 lecturas. Resolviendo
                // primero los CaseCode y pasándolos como lista:
                //
                //      94.064 -> 110 lecturas lógicas, 265 ms   (855x menos)
                //
                // No hace falta ningún índice: el problema era la forma de la
                // consulta, no el esquema.
                var idsPorNota = await _db.Note.AsNoTracking()
                    .Where(n => n.Title == CTX && n.Detail != null && n.Detail.Contains(s))
                    .Select(n => n.CaseCode)
                    .Distinct()
                    .ToListAsync();

                q = q.Where(pc =>
                    pc.DefinitionCode.Contains(s)
                    || (pc.DefinitionCodeNavigation != null && pc.DefinitionCodeNavigation.Name.Contains(s))
                    || idsPorNota.Contains(pc.CaseCode));
            }

            var total = await q.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            if (page > totalPages) page = totalPages;

            // PROYECCIÓN, no .Include(). La Bandeja necesita exactamente tres cosas
            // de las relaciones: el CONTEO de documentos (ni una columna de ellos),
            // el Detail de la nota ContextoSobre y el de ResolucionReembolso.
            //
            // Con .Include(pc => pc.DataFile) EF traía la columna Text COMPLETA de
            // cada documento: media 13.718 caracteres, máximo medido 2,69 MB en un
            // solo valor, 129 MB de LOB en la tabla. Y con .Include(pc => pc.Notes)
            // traía TODAS las notas del caso, incluidas ResolucionReembolso (~7 KB),
            // AuditoriaMedicina (~5,5 KB) y ExpedienteDocumental (~3,4 KB), cuando
            // solo se usan dos de ellas y solo su Detail.
            //
            // Proyectando, el listado deja de depender del tamaño de los documentos.
            var rows = await q.OrderByDescending(pc => pc.StartDate)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(pc => new
                {
                    pc.CaseCode,
                    pc.DefinitionCode,
                    ProcesoNombre = pc.DefinitionCodeNavigation != null ? pc.DefinitionCodeNavigation.Name : null,
                    pc.State,
                    pc.StartDate,
                    NumDocumentos = pc.DataFile.Count,
                    Contexto = pc.Notes.Where(n => n.Title == CTX)
                                       .OrderByDescending(n => n.CreatedAt)
                                       .Select(n => n.Detail).FirstOrDefault(),
                    Resolucion = pc.Notes.Where(n => n.Title == RESO)
                                         .OrderByDescending(n => n.CreatedAt)
                                         .Select(n => n.Detail).FirstOrDefault()
                })
                .ToListAsync();

            var casos = rows.Select(pc =>
            {
                var item = new CasoListItem
                {
                    CaseCode       = pc.CaseCode,
                    DefinitionCode = pc.DefinitionCode,
                    ProcesoNombre  = pc.ProcesoNombre,
                    Estado         = pc.State ?? string.Empty,
                    NumDocumentos  = pc.NumDocumentos,
                    StartDate      = pc.StartDate
                };
                ParseContexto(pc.Contexto, item);
                ParseResolucion(pc.Resolucion, item);
                return item;
            }).ToList();

            var estados = (await _db.ProcessCase.AsNoTracking().Select(pc => pc.State).Distinct().ToListAsync())
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).OrderBy(x => x).ToList();
            var conReso = await _db.Note.AsNoTracking().Where(n => n.Title == RESO).Select(n => n.CaseCode).Distinct().CountAsync();

            return View(new BandejaViewModel
            {
                Casos = casos, Search = search, Estado = estado,
                Page = page, TotalPages = totalPages, Total = total,
                TotalCasos = await _db.ProcessCase.CountAsync(),
                ConResolucion = conReso,
                EstadosDisponibles = estados
            });
        }

        /// <summary>
        /// Consolida la identidad del cliente de DOS fuentes, en este orden:
        ///  1) la nota ContextoSobre - determinista, lo que trajo la importacion;
        ///  2) la ficha del Expediente - extraida por IA de los documentos.
        /// La primera manda; la segunda solo rellena lo que falte. Con esto la
        /// cabecera del workspace muestra al cliente en TODOS los pasos.
        /// </summary>
        private static ContextoClienteVm ArmarContextoCliente(IEnumerable<Note> notas)
        {
            var vm = new ContextoClienteVm();
            var lista = notas.ToList();

            static string? Campo(System.Text.Json.JsonElement raiz, params string[] claves)
            {
                foreach (var k in claves)
                {
                    foreach (var pr in raiz.EnumerateObject())
                    {
                        if (!string.Equals(pr.Name, k, StringComparison.OrdinalIgnoreCase)) continue;
                        var v = pr.Value.ValueKind == System.Text.Json.JsonValueKind.String
                            ? pr.Value.GetString()
                            : pr.Value.ToString();
                        if (!string.IsNullOrWhiteSpace(v) && v != "null") return v;
                    }
                }
                return null;
            }

            // 1) ContextoSobre
            var ctx = lista.Where(n => n.Title == "ContextoSobre")
                           .OrderByDescending(n => n.CreatedAt)
                           .Select(n => n.Detail).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(ctx))
            {
                try
                {
                    using var d = System.Text.Json.JsonDocument.Parse(ctx);
                    if (d.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        var r = d.RootElement;
                        vm.Titular = Campo(r, "nombreTitular", "titular", "cliente");
                        vm.Cedula = Campo(r, "cedula", "identificacion");
                        vm.NumeroSobre = Campo(r, "numeroSobre", "sobre");
                        vm.Origen = Campo(r, "origen");
                        vm.Contrato = Campo(r, "numeroContrato", "contrato");
                        vm.Producto = Campo(r, "codigoProducto", "producto");
                    }
                }
                catch (System.Text.Json.JsonException) { }
            }

            // 2) Ficha del Expediente (rellena huecos)
            var exp = lista.Where(n => n.Title == "ExpedienteDocumental")
                           .OrderByDescending(n => n.CreatedAt)
                           .Select(n => n.Detail).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(exp))
            {
                try
                {
                    using var d = System.Text.Json.JsonDocument.Parse(exp);

                    // El nodo se llama "fichaCliente" (asi lo emite el agente); se
                    // acepta tambien "ficha" y se busca sin distinguir mayusculas,
                    // porque TryGetProperty SI distingue y nos dejaba la barra a medias.
                    System.Text.Json.JsonElement f = default;
                    var hayFicha = false;
                    if (d.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        foreach (var pr in d.RootElement.EnumerateObject())
                        {
                            if ((pr.Name.Equals("fichaCliente", StringComparison.OrdinalIgnoreCase)
                                 || pr.Name.Equals("ficha", StringComparison.OrdinalIgnoreCase))
                                && pr.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                f = pr.Value;
                                hayFicha = true;
                                break;
                            }
                        }
                    }

                    if (hayFicha)
                    {
                        vm.Titular ??= Campo(f, "nombre");
                        vm.Cedula ??= Campo(f, "cedula");
                        vm.Contrato ??= Campo(f, "contrato");
                        vm.Producto ??= Campo(f, "producto");
                        vm.Prestador ??= Campo(f, "prestador");
                        vm.Diagnosticos ??= Campo(f, "diagnosticos");
                        vm.FechaAtencion ??= Campo(f, "fechaAtencion");
                        var tot = Campo(f, "totalFacturado");
                        if (decimal.TryParse(tot, System.Globalization.NumberStyles.Any,
                                             System.Globalization.CultureInfo.InvariantCulture, out var v))
                        {
                            vm.TotalFacturado = v;
                        }
                    }
                }
                catch (System.Text.Json.JsonException) { }
            }

            return vm;
        }

        private static void ParseContexto(string? json, CasoListItem item)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return;
                string? G(string k)
                {
                    foreach (var p in doc.RootElement.EnumerateObject())
                        if (string.Equals(p.Name, k, StringComparison.OrdinalIgnoreCase))
                            return p.Value.ValueKind == System.Text.Json.JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
                    return null;
                }
                item.NumeroSobre = G("numeroSobre");
                item.Origen      = G("origen");
                var titular = G("nombreTitular");
                var ced     = G("cedula");
                item.Cliente = !string.IsNullOrWhiteSpace(titular) ? titular
                             : (!string.IsNullOrWhiteSpace(ced) ? $"Céd. {ced}" : null);
            }
            catch (System.Text.Json.JsonException) { }
        }

        private static void ParseResolucion(string? json, CasoListItem item)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            item.TieneResolucion = true;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                string? estado = null; double? conf = null;
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var p in doc.RootElement.EnumerateObject())
                    {
                        if (string.Equals(p.Name, "estadoPropuesto", StringComparison.OrdinalIgnoreCase)) estado = p.Value.GetString();
                        else if (string.Equals(p.Name, "confianza", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == System.Text.Json.JsonValueKind.Number) conf = p.Value.GetDouble();
                        else if (string.Equals(p.Name, "_raw", StringComparison.OrdinalIgnoreCase)) estado = "CONTROL_HUMANO";
                    }
                }
                (item.ResolucionLabel, item.ResolucionCss) = (estado?.ToUpperInvariant(), conf) switch
                {
                    (_, < 0.5)            => ("Control humano", "label-default"),
                    ("LIQUIDA_AUTO", _)   => ("Liquida", "label-success"),
                    ("SEMI", _)           => ("Parcial", "label-warning"),
                    ("NEGATIVA", _)       => ("Negativa", "label-danger"),
                    ("CONTROL_HUMANO", _) => ("Control humano", "label-default"),
                    _                     => ("Generada", "label-info")
                };
            }
            catch (System.Text.Json.JsonException) { item.ResolucionLabel = "Generada"; item.ResolucionCss = "label-info"; }
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

            // ── 3b. REQ-019: contexto estructurado del sobre (el ticket Zendesk
            //   trae número de sobre y CÉDULA — la cédula habilita la cadena de tools
            //   por identidad y el guardián anti-IDOR).
            _db.Note.Add(new Note
            {
                CaseCode  = newCase.CaseCode,
                Title     = app_tramites.Services.Ai.OcrPromptHelper.ContextoSobreNoteTitle,
                Detail    = System.Text.Json.JsonSerializer.Serialize(new
                {
                    origen      = "Zendesk",
                    ticketId    = ticketId,
                    numeroSobre = ticket.NumeroSobre,
                    cedula      = ticket.CedulaBeneficiario,
                    asunto      = ticket.Subject
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "import-zendesk"
            });
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
                    List<app_tramites.Models.ViewModel.PaginaOcr> paginasOcr = new();

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

                        // REQ-019: versión detallada → conserva el OCR por página
                        var ocrRes = await _ingest.ProcessFileDetailedAsync(ocrFile);
                        fileUrl = ocrRes.Url;
                        ocrText = ocrRes.Text;
                        paginasOcr = ocrRes.Paginas;
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

                    // REQ-019: OCR POR PÁGINA (habilita tags y clasificación por página)
                    var paginas = OcrPaginaPersistencia.Materializar(dataFile.Id, paginasOcr);
                    if (paginas.Count > 0)
                    {
                        _db.DataFilePage.AddRange(paginas);
                        await _db.SaveChangesAsync();
                    }
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

            var archivos = caso.DataFile.OrderBy(f => f.CreatedDate).ToList();

            // -- Estado REAL de cada paso, leido de la base --------------------
            // El workspace deja de ser 7 pestanas sueltas: es un proceso con
            // pasos que se marcan hechos cuando existe su resultado guardado.
            var tipificado = await _db.DocumentoClasificacion.AsNoTracking()
                .AnyAsync(x => x.DataFileNavigation.CaseCode == caseCode && x.IsCurrent);

            var titulos = caso.Notes.Where(n => n.Title != null).Select(n => n.Title!).ToHashSet();

            var pasos = new List<PasoProcesoVm>
            {
                new() { Numero = 1, Tab = "tab-docs", Titulo = "Documentos", Icono = "fa-file-text-o",
                        Entrega = "OCR por pagina de cada documento del sobre",
                        Hecho = archivos.Count > 0 },
                new() { Numero = 2, Tab = "tab-tipificacion", Titulo = "Tipificacion", Icono = "fa-tags",
                        Entrega = "Que tipo de reembolso es cada documento y cada hoja",
                        Hecho = tipificado,
                        UrlGenerar = Url.Action("Generar", "Clasificacion", new { area = "Studio" }) },
                new() { Numero = 3, Tab = "tab-expediente", Titulo = "Expediente", Icono = "fa-sitemap",
                        Entrega = "Ficha del cliente y arbol de evidencia (factura -> justificantes)",
                        Hecho = titulos.Contains("ExpedienteDocumental"),
                        UrlGenerar = Url.Action("Generar", "Expediente", new { area = "Studio" }) },
                new() { Numero = 4, Tab = "tab-auditoria", Titulo = "Auditoria medica", Icono = "fa-user-md",
                        Entrega = "Revision clinica: pertinencia, correlacion y hallazgos",
                        Hecho = titulos.Contains("AuditoriaMedicina"),
                        UrlGenerar = Url.Action("Generar", "Auditoria", new { area = "Studio" }) },
                new() { Numero = 5, Tab = "tab-reso", Titulo = "Resolucion", Icono = "fa-gavel",
                        Entrega = "Liquidacion regla por regla (usa la auditoria) + carta al cliente",
                        Hecho = titulos.Contains("ResolucionReembolso"),
                        UrlGenerar = Url.Action("Generar", "Resolucion", new { area = "Studio" }) },
            };

            var vm = new CasoImportadoViewModel
            {
                Caso = caso,
                Archivos = archivos,
                Notas = caso.Notes.OrderBy(n => n.CreatedAt).ToList(),
                ProcessCode = caso.DefinitionCode,
                Mensaje = TempData["Mensaje"]?.ToString(),
                Cliente = ArmarContextoCliente(caso.Notes),
                Pasos = pasos
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

                    // REQ-019: versión detallada → conserva el OCR por página
                    var ocrRes = await _ingest.ProcessFileDetailedAsync(ocrFile);

                    var dataFile = new DataFile
                    {
                        IsFileUri = true,
                        FileUri = ocrRes.Url,
                        Text = ocrRes.Text,
                        CaseCode = request.CaseCode,
                        CreatedDate = DateTime.UtcNow,
                        OriginalName = archivo.FileName
                    };
                    _db.DataFile.Add(dataFile);
                    await _db.SaveChangesAsync();
                    dataFileIds.Add(dataFile.Id);

                    // REQ-019: OCR POR PÁGINA
                    var paginas = OcrPaginaPersistencia.Materializar(dataFile.Id, ocrRes.Paginas);
                    if (paginas.Count > 0)
                    {
                        _db.DataFilePage.AddRange(paginas);
                        await _db.SaveChangesAsync();
                    }
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
            // ── REQ-019 UX: criterio ÚNICO con detección automática ──────────
            //   "NA-2612551" / "NE123..."  → número de sobre (exacto)
            //   10 dígitos                 → cédula (requiere AnioNacimiento)
            //   otros dígitos              → se intenta como número de sobre
            //   texto                      → nombre del cliente (LIKE)
            var criterio = vm.Criterio?.Trim();
            string? numeroSobre = null, cedula = null, nombre = null;

            if (!string.IsNullOrWhiteSpace(criterio))
            {
                var soloDigitos = criterio.All(char.IsDigit);
                if (System.Text.RegularExpressions.Regex.IsMatch(criterio, @"^[A-Za-z]{1,4}-?\d+$"))
                    numeroSobre = criterio;
                else if (soloDigitos && criterio.Length == 10)
                    cedula = criterio;
                else if (soloDigitos)
                    numeroSobre = criterio;
                else
                    nombre = criterio;
            }
            else
            {
                // Compatibilidad con los campos separados (llamadas antiguas)
                numeroSobre = string.IsNullOrWhiteSpace(vm.NumeroSobre) ? null : vm.NumeroSobre.Trim();
                cedula      = string.IsNullOrWhiteSpace(vm.Cedula)      ? null : vm.Cedula.Trim();
                criterio    = numeroSobre ?? cedula ?? string.Empty;
            }

            if (numeroSobre == null && cedula == null && nombre == null)
            {
                vm.ProcesosDisponibles = await _db.Process
                    .Where(p => p.IsActive == true)
                    .OrderBy(p => p.Name)
                    .ToListAsync();
                ModelState.AddModelError(string.Empty,
                    "Escribe un número de sobre (NA-…), una cédula o el nombre del cliente.");
                return View("ImportarArmonix", vm);
            }

            IReadOnlyList<app_ocr_ai_models.Services.Documents.ArmonixSobreResueltoDto> sobres;
            try
            {
                sobres = await _armonix.BuscarSobresAsync(numeroSobre, cedula, nombre, vm.AnioNacimiento);
            }
            catch (ArgumentException aex)
            {
                // Criterio incompleto (p. ej. cédula sin año de nacimiento, nombre muy corto):
                // volver al formulario con el mensaje, conservando lo escrito.
                vm.ProcesosDisponibles = await _db.Process
                    .Where(p => p.IsActive == true)
                    .OrderBy(p => p.Name)
                    .ToListAsync();
                ModelState.AddModelError(string.Empty, aex.Message);
                return View("ImportarArmonix", vm);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[T22 RW] Error al buscar sobre Armonix con criterio {Criterio}.", criterio);
                var resultado = new BusquedaSobreArmonixViewModel
                {
                    CriterioBuscado = criterio ?? string.Empty,
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
                    CriterioBuscado = criterio ?? string.Empty,
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
                CriterioBuscado = criterio ?? string.Empty,
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
                    NumeroPersonaPaciente = s.NumeroPersonaPaciente,
                    ValorPresentado       = s.ValorPresentado
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
            string processCode,
            string? nombreTitular = null)
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

            // ── 2b. REQ-019: persistir el CONTEXTO ESTRUCTURADO del sobre como Note.
            //   Sin esto, el agente IA queda "ciego": pierde contrato/producto/región/persona
            //   que ya conocemos aquí, y las tools de coberturas/deducible no pueden encadenarse.
            _db.Note.Add(new Note
            {
                CaseCode  = newCase.CaseCode,
                Title     = app_tramites.Services.Ai.OcrPromptHelper.ContextoSobreNoteTitle,
                Detail    = System.Text.Json.JsonSerializer.Serialize(new
                {
                    origen                = "Armonix",
                    numeroSobre           = numeroSobre?.Trim(),
                    numeroContrato        = numeroContrato?.Trim(),
                    codigoProducto        = codigoProducto?.Trim(),
                    codigoRegion          = codigoRegion?.Trim(),
                    numeroPersonaPaciente = numeroPersonaPaciente?.Trim(),
                    nombreTitular         = nombreTitular?.Trim()
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "import-armonix"
            });
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
