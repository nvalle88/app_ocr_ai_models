using System.Text;
using System.Text.Json;
using app_ocr_ai_models.Areas.Studio.Models;
using app_ocr_ai_models.Data;
using app_tramites.Models.ModelAi;
using app_tramites.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace app_ocr_ai_models.Areas.Studio.Controllers;

// ============================================================
// REQ-019 — TIPIFICACIÓN de documentos de reembolso.
//   Ejecuta el agente AGENTE_CLASIFICADOR_DOC (prompt CLASIFICADOR_DOC_REEMBOLSO,
//   editable en BD) sobre el OCR del caso PÁGINA POR PÁGINA y persiste el
//   resultado en tablas consultables:
//     DocumentoClasificacion · DocumentoItem · DocumentoTag · DocumentoDiagnostico
//   Con eso el sobre queda dividido por tipo de reembolso (MED/ATE_HOS/PRO/…),
//   con tags por documento y por página, e ítems por rubro.
// ============================================================

/// <summary>Controller de la tipificación/clasificación documental del sobre.</summary>
[Area("Studio")]
[Authorize]
public sealed class ClasificacionController : Controller
{
    private const string PromptCode = "CLASIFICADOR_DOC_REEMBOLSO";
    private const string AgenteCode = "AGENTE_CLASIFICADOR_DOC";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly OCRDbContext _db;
    private readonly AiCompletionServiceFactory _factory;
    private readonly ILogger<ClasificacionController> _logger;
    private readonly Services.Ai.IHomologadorProcedimientos _homologador;

    public ClasificacionController(OCRDbContext db, AiCompletionServiceFactory factory,
        ILogger<ClasificacionController> logger,
        Services.Ai.IHomologadorProcedimientos homologador)
    {
        _homologador = homologador;
        _db      = db      ?? throw new ArgumentNullException(nameof(db));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger  = logger  ?? throw new ArgumentNullException(nameof(logger));
    }

    // GET /Studio/Clasificacion?caseCode=&embed=true
    [HttpGet]
    public async Task<IActionResult> Index(Guid caseCode, bool embed = false)
    {
        ViewData["Embed"] = embed;
        if (caseCode == Guid.Empty)
            return View(new ClasificacionViewModel { Error = "Falta el CaseCode." });

        var docs = await _db.DataFile.AsNoTracking()
            .Where(f => f.CaseCode == caseCode)
            .Select(f => new { f.Id, Paginas = f.DataFilePage.Count })
            .ToListAsync();

        // Reconstruye la vista desde las TABLAS (fuente de verdad consultable)
        var dto = await ReconstruirDesdeBdAsync(caseCode);

        return View(new ClasificacionViewModel
        {
            CaseCode             = caseCode,
            Generada             = dto != null,
            Clasificacion        = dto,
            TotalDocumentos      = docs.Count,
            DocumentosConPaginas = docs.Count(d => d.Paginas > 0),
            GeneradoPor          = User?.Identity?.Name ?? "—",
            GeneradoEn           = await _db.DocumentoClasificacion.AsNoTracking()
                .Where(c => c.DataFileNavigation.CaseCode == caseCode && c.IsCurrent)
                .OrderByDescending(c => c.CreatedDate)
                .Select(c => (DateTime?)c.CreatedDate).FirstOrDefaultAsync()
        });
    }

    // POST /Studio/Clasificacion/Generar
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generar(Guid caseCode, bool embed = false)
    {
        if (caseCode == Guid.Empty)
            return RedirectToAction(nameof(Index), new { caseCode, embed });

        var caso = await _db.ProcessCase
            .Include(pc => pc.DataFile).ThenInclude(f => f.DataFilePage)
            .Include(pc => pc.Notes)
            .FirstOrDefaultAsync(pc => pc.CaseCode == caseCode);

        if (caso == null || caso.DataFile.Count == 0)
        {
            TempData["Error"] = "El caso no existe o no tiene documentos.";
            return RedirectToAction(nameof(Index), new { caseCode, embed });
        }

        var prompt = await _db.OPAIPrompt.AsNoTracking()
            .Where(p => p.Code == PromptCode && p.IsActive)
            .Select(p => p.Content).FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            TempData["Error"] = $"No existe el prompt '{PromptCode}' activo en BD.";
            return RedirectToAction(nameof(Index), new { caseCode, embed });
        }

        var config = await _db.Agent.AsNoTracking()
            .Where(a => a.Code == AgenteCode && a.IsActive)
            .Select(a => new { a.ConfigCode, a.MaxTokens })
            .FirstOrDefaultAsync();
        var opai = await _db.OPAIConfiguration.AsNoTracking()
            .FirstOrDefaultAsync(c => c.IsActive &&
                (config == null ? c.Provider == "Anthropic" : c.Code == config.ConfigCode));
        if (opai == null)
        {
            TempData["Error"] = "No hay configuración IA activa para el clasificador.";
            return RedirectToAction(nameof(Index), new { caseCode, embed });
        }

        // ── Mensaje: contexto + OCR por PÁGINA con marcas [[PAGINA n]] ──
        var userMessage = ConstruirMensaje(caso);

        string texto;
        try
        {
            var svc = _factory.Create(opai);
            var res = await svc.CompleteAsync(new AiCompletionRequest
            {
                SystemPrompt = prompt,
                UserMessage  = userMessage,
                MaxTokens    = config?.MaxTokens ?? 16000,
                Temperature  = 0
            }, HttpContext.RequestAborted);
            texto = res.Text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error clasificando el caso {CaseCode}.", caseCode);
            TempData["Error"] = $"Error al clasificar: {ex.Message}";
            return RedirectToAction(nameof(Index), new { caseCode, embed });
        }

        var json = ExtractJson(texto);
        if (string.IsNullOrWhiteSpace(json))
        {
            TempData["Error"] = "El clasificador no devolvió JSON interpretable. Reintenta.";
            return RedirectToAction(nameof(Index), new { caseCode, embed });
        }

        ClasificacionSobreDto? dto;
        try { dto = JsonSerializer.Deserialize<ClasificacionSobreDto>(json, JsonOpts); }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON de clasificación inválido {CaseCode}.", caseCode);
            TempData["Error"] = "El JSON del clasificador no cumple el esquema.";
            return RedirectToAction(nameof(Index), new { caseCode, embed });
        }

        if (dto != null)
            await PersistirAsync(caseCode, dto, json, HttpContext.RequestAborted);

        return RedirectToAction(nameof(Index), new { caseCode, embed });
    }

    // ── Construcción del mensaje ─────────────────────────────────────────

    /// <summary>
    /// Arma el user message con el contexto del sobre y el OCR de cada documento
    /// PÁGINA POR PÁGINA. Si un documento no tiene páginas (importado antes del
    /// cambio de OCR), se envía su texto completo como [[PAGINA 1]].
    /// </summary>
    private static string ConstruirMensaje(ProcessCase caso)
    {
        var sb = new StringBuilder();
        var ctx = OcrPromptHelper.BuildCaseContext(caso.Notes);
        sb.AppendLine("## Contexto del sobre");
        sb.AppendLine(string.IsNullOrWhiteSpace(ctx) ? "{}" : ctx.Replace("\r", " ").Replace("\n", " "));
        sb.AppendLine();
        sb.AppendLine("## Documentos del sobre (docId :: nombre :: texto OCR por pagina)");

        foreach (var f in caso.DataFile.OrderBy(f => f.CreatedDate))
        {
            sb.AppendLine($"--- docId={f.Id} :: {f.OriginalName} ---");
            var paginas = f.DataFilePage.OrderBy(p => p.PageNumber).ToList();
            if (paginas.Count > 0)
            {
                foreach (var p in paginas)
                {
                    sb.AppendLine($"[[PAGINA {p.PageNumber}]]");
                    sb.AppendLine(string.IsNullOrWhiteSpace(p.Text) ? "(pagina sin texto OCR)" : p.Text);
                }
            }
            else
            {
                sb.AppendLine("[[PAGINA 1]]");
                sb.AppendLine(string.IsNullOrWhiteSpace(f.Text) ? "(sin texto OCR)" : f.Text);
            }
            sb.AppendLine($"--- FIN docId={f.Id} ---").AppendLine();
        }
        return sb.ToString();
    }

    // ── Persistencia en las 5 tablas ─────────────────────────────────────

    /// <summary>
    /// Guarda la clasificación: versiona (IsCurrent) y reemplaza ítems, tags y
    /// diagnósticos del documento. Todo dentro de una transacción.
    /// </summary>
    private async Task PersistirAsync(Guid caseCode, ClasificacionSobreDto dto, string rawJson,
                                      CancellationToken ct = default)
    {
        var idsCaso = await _db.DataFile.Where(f => f.CaseCode == caseCode)
            .Select(f => f.Id).ToListAsync();

        // ── La homologación se resuelve ANTES de abrir la transacción ─────────
        //
        // HomologarAsync y ValidarCorrelacionAsync salen a SQLMIGRACION por VPN.
        // Medido: 816 ms por ciclo completo de validación (el grueso es abrir la
        // conexión, no el IO) y 9,5 s la carga en frío del catálogo Lr05. Hacer
        // esos viajes DENTRO de la transacción alarga su ventana por segundos sin
        // ninguna necesidad: no escriben nada, solo consultan catálogos.
        //
        // Se resuelve todo aquí, se guarda en un diccionario, y la transacción
        // queda reducida a lo que de verdad es: escribir.
        var homologaciones = new Dictionary<(int DocId, int Idx),
                                            (Services.Ai.HomologacionResultado? Homo,
                                             Services.Ai.CorrelacionResultado? Corr)>();

        foreach (var fic in dto.Ficheros)
        {
            if (!idsCaso.Contains(fic.DocId)) continue;

            var dxDoc = fic.Diagnosticos.Select(d => NormalizarCie10(d.Codigo))
                            .Where(x => x != null).Distinct().ToList();
            if (dxDoc.Count == 0)
            {
                dxDoc = dto.Ficheros.SelectMany(f => f.Diagnosticos)
                            .Select(d => NormalizarCie10(d.Codigo))
                            .Where(x => x != null).Distinct().ToList();
            }

            for (var i = 0; i < fic.Procedimientos.Count; i++)
            {
                var pr = fic.Procedimientos[i];

                // Con el token: estos dos viajes van contra SQLMIGRACION por VPN
                // y son los más lentos del paso. Sin propagarlo, cerrar la
                // pestaña no paraba nada — el servidor seguía esperando a una
                // máquina que ya no le importaba a nadie.
                var homo = await _homologador.HomologarAsync(pr.Descripcion, ct);
                Services.Ai.CorrelacionResultado? corr = null;
                if (homo != null)
                {
                    corr = await _homologador.ValidarCorrelacionAsync(
                        homo.NumeroProcedimiento, homo.CodigoBeneficio, dxDoc, ct);
                }
                homologaciones[(fic.DocId, i)] = (homo, corr);
            }
        }

        await using var tx = await _db.Database.BeginTransactionAsync();

        foreach (var fic in dto.Ficheros)
        {
            if (!idsCaso.Contains(fic.DocId)) continue;   // ignora docIds inventados

            // 1) Clasificación versionada: la anterior deja de ser vigente
            var previas = await _db.DocumentoClasificacion
                .Where(c => c.DataFileId == fic.DocId && c.IsCurrent).ToListAsync();
            var version = 1;
            foreach (var p in previas) { p.IsCurrent = false; version = Math.Max(version, p.VersionNumber + 1); }

            _db.DocumentoClasificacion.Add(new DocumentoClasificacion
            {
                DataFileId       = fic.DocId,
                TipoArchivo      = Recorta(fic.TipoArchivo ?? "GENERAL-SOPORTE", 30),
                ListaTipoArchivo = Recorta(string.Join(",", fic.ListaTipoArchivo.Distinct()), 400),
                ValorTotal       = Math.Max(0, fic.ValorTotal),
                EsFacturaValida  = fic.EsFacturaValida,
                NumeroFactura    = Recorta(fic.NumeroFactura, 50),
                ClaveAcceso      = Recorta(fic.ClaveAcceso, 60),
                ModelCode        = AgenteCode,
                RawJson          = rawJson.Length > 60000 ? rawJson[..60000] : rawJson,
                VersionNumber    = version,
                IsCurrent        = true,
                CreatedDate      = DateTime.UtcNow,

                // ── REQ-019m: de quién es la factura y qué es el soporte ──
                TipoSoporte           = Recorta(fic.TipoSoporte, 40),
                ResumenSoporte        = Recorta(fic.ResumenSoporte, 600),
                EmisorRuc             = Recorta(fic.Emisor?.Ruc, 20),
                EmisorNombre          = Recorta(fic.Emisor?.Nombre, 250),
                EmisorNombreComercial = Recorta(fic.Emisor?.NombreComercial, 250),
                EmisorTipo            = Recorta(fic.Emisor?.TipoEstablecimiento, 30),
                EmisorCiudad          = Recorta(fic.Emisor?.Ciudad, 100),
                EmisorPais            = Recorta(fic.Emisor?.Pais, 60),
                NumeroAutorizacion    = Recorta(fic.Factura?.NumeroAutorizacion, 60),
                FechaEmision          = ParseFecha(fic.Factura?.FechaEmision),
                Subtotal              = fic.Factura?.Subtotal,
                Iva                   = fic.Factura?.Iva,
                Moneda                = Recorta(fic.Factura?.Moneda, 10),
                PacienteEdad          = fic.Paciente?.Edad,
                PacienteSexo          = Recorta(fic.Paciente?.Sexo, 10),
                PacienteEsTitular     = fic.Paciente?.EsTitular,
                MedicoNombre          = Recorta(fic.MedicoTratante?.Nombre, 250),
                MedicoEspecialidad    = Recorta(fic.MedicoTratante?.Especialidad, 150),
                MedicoRegistro        = Recorta(fic.MedicoTratante?.Registro, 60),
                FechaAtencion         = ParseFecha(fic.FechaAtencion),

                // ── REQ-019y: el pie fiscal completo ──────────────────────
                // Cada linea del pie pesa distinto al liquidar: los servicios de
                // salud caen en "no objeto de IVA", el descuento se resta antes
                // de cubrir y el servicio no es gasto medico. Con solo subtotal
                // e IVA habia que suponer la base de calculo.
                SubtotalSinImpuestos  = fic.Factura?.SubtotalSinImpuestos,
                Subtotal15            = fic.Factura?.Subtotal15,
                Subtotal5             = fic.Factura?.Subtotal5,
                Subtotal0             = fic.Factura?.Subtotal0,
                SubtotalNoObjetoIva   = fic.Factura?.SubtotalNoObjetoIva,
                SubtotalExentoIva     = fic.Factura?.SubtotalExentoIva,
                Descuentos            = fic.Factura?.Descuentos,
                Ice                   = fic.Factura?.Ice,
                Iva15                 = fic.Factura?.Iva15,
                Iva5                  = fic.Factura?.Iva5,
                ServicioValor         = fic.Factura?.ServicioValor,
                ServicioPorcentaje    = fic.Factura?.ServicioPorcentaje,
                Propina               = fic.Factura?.Propina,

                // ── REQ-019y: el PORQUE de la decision ────────────────────
                // Sin esto, "CON_MED-FACTURA" es un veredicto sin expediente:
                // nadie puede comprobar si se acerto por la razon correcta.
                JustificacionTipo     = Recorta(fic.JustificacionTipo, 1000),
                SenalesTipo           = Recorta(
                                            string.Join(" | ", fic.SenalesTipo
                                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                                .Select(x => x.Trim())), 1000),
                TipoDescartado        = Recorta(fic.TipoDescartado, 30),
                JustificacionDescarte = Recorta(fic.JustificacionDescarte, 600),
                JustificacionFactura  = Recorta(fic.JustificacionFactura, 600),
                JustificacionSoporte  = Recorta(fic.JustificacionSoporte, 600),

                // ── REQ-020j: de quien es el documento ────────────────────
                PacienteNumeroPersona = fic.PacienteNumeroPersona,
                PacienteCoincide      = Recorta(fic.PacienteCoincide, 20),
                PacienteJustificacion = Recorta(fic.PacienteJustificacion, 500)
            });

            // 2) Ítems por rubro (reemplaza)
            // ExecuteDeleteAsync y NO RemoveRange: RemoveRange(IQueryable) enumera,
            // materializa las entidades y emite UN DELETE POR FILA. Con 9 tags nadie
            // lo nota; medido sobre 342 filas son 86.595 ms contra 199 ms del DELETE
            // de conjunto (435x). Regla del área: RemoveRange(IQueryable) queda
            // prohibido sobre tablas hijas de documento.
            await _db.DocumentoItem.Where(i => i.DataFileId == fic.DocId).ExecuteDeleteAsync();
            var orden = 1;
            foreach (var it in fic.Items)
            {
                _db.DocumentoItem.Add(new DocumentoItem
                {
                    DataFileId    = fic.DocId,
                    PageNumber    = it.Pagina,
                    Orden         = orden++,
                    NumeroFactura = Recorta(fic.NumeroFactura, 50),
                    Descripcion   = Recorta(it.Descripcion ?? "(sin descripcion)", 500)!,
                    TipoRubro     = NormalizarRubro(it.TipoRubro),
                    Cantidad      = it.Cantidad,
                    ValorUnitario = it.ValorUnitario,
                    ValorTotal    = it.ValorTotal,
                    CreatedDate   = DateTime.UtcNow
                });
            }

            // 3) Tags de documento y de página (reemplaza)
            await _db.DocumentoTag.Where(t => t.DataFileId == fic.DocId).ExecuteDeleteAsync();
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddTag(int? pagina, string tag, string categoria, string? valor = null)
            {
                if (string.IsNullOrWhiteSpace(tag)) return;
                var key = $"{pagina}|{tag}";
                if (!vistos.Add(key)) return;   // respeta UQ_DocumentoTag_Unico
                _db.DocumentoTag.Add(new DocumentoTag
                {
                    DataFileId  = fic.DocId,
                    PageNumber  = pagina,
                    Tag         = Recorta(tag, 60)!,
                    Categoria   = Recorta(categoria, 30),
                    Origen      = "IA",
                    Valor       = Recorta(valor, 200),
                    CreatedDate = DateTime.UtcNow
                });
            }
            AddTag(null, fic.TipoArchivo ?? "GENERAL-SOPORTE", "TIPO");
            foreach (var t in fic.ListaTipoArchivo) AddTag(null, t, "TIPO");
            if (fic.EsFacturaValida) AddTag(null, "FACTURA_VALIDA", "MARCA", fic.NumeroFactura);
            if (!string.IsNullOrWhiteSpace(fic.ClaveAcceso)) AddTag(null, "TIENE_CLAVE_ACCESO", "MARCA", fic.ClaveAcceso);

            // ── REQ-019m: los tags que de verdad le sirven al operador ──
            // El VALOR del tag es lo que se pinta en el chip: "RUC 0916422173001",
            // "Autorización 2503...", no solo la marca a secas.
            if (!string.IsNullOrWhiteSpace(fic.TipoSoporte))
                AddTag(null, fic.TipoSoporte!, "SOPORTE", fic.ResumenSoporte);
            if (!string.IsNullOrWhiteSpace(fic.Emisor?.Nombre))
                AddTag(null, "EMISOR_IDENTIFICADO", "EMISOR", fic.Emisor!.Nombre);
            if (!string.IsNullOrWhiteSpace(fic.Emisor?.Ruc))
                AddTag(null, "RUC_EMISOR", "EMISOR", fic.Emisor!.Ruc);
            if (!string.IsNullOrWhiteSpace(fic.Emisor?.TipoEstablecimiento))
                AddTag(null, fic.Emisor!.TipoEstablecimiento!, "EMISOR", fic.Emisor.Ciudad);
            if (!string.IsNullOrWhiteSpace(fic.Factura?.NumeroAutorizacion))
                AddTag(null, "AUTORIZACION", "FISCAL", fic.Factura!.NumeroAutorizacion);
            if (!string.IsNullOrWhiteSpace(fic.Factura?.FechaEmision))
                AddTag(null, "FECHA_EMISION", "FISCAL", fic.Factura!.FechaEmision);
            if (fic.Factura?.Iva is > 0)
                AddTag(null, "SUBTOTAL_IVA", "FISCAL",
                    $"sub {fic.Factura!.Subtotal:F2} / IVA {fic.Factura.Iva:F2}");
            if (!string.IsNullOrWhiteSpace(fic.MedicoTratante?.Nombre))
                AddTag(null, "MEDICO_TRATANTE", "CLINICO",
                    string.IsNullOrWhiteSpace(fic.MedicoTratante!.Especialidad)
                        ? fic.MedicoTratante.Nombre
                        : $"{fic.MedicoTratante.Nombre} · {fic.MedicoTratante.Especialidad}");
            if (!string.IsNullOrWhiteSpace(fic.FechaAtencion))
                AddTag(null, "FECHA_ATENCION", "CLINICO", fic.FechaAtencion);

            foreach (var pg in fic.Paginas)
            {
                if (!string.IsNullOrWhiteSpace(pg.Tipo)) AddTag(pg.Pagina, pg.Tipo!, "TIPO");
                if (!string.IsNullOrWhiteSpace(pg.TipoSoporte)) AddTag(pg.Pagina, pg.TipoSoporte!, "SOPORTE");
                foreach (var t in pg.Tags) AddTag(pg.Pagina, t, "MARCA");
                if (pg.TieneFacturaValida) AddTag(pg.Pagina, "FACTURA_VALIDA", "MARCA");
                if (pg.ValorDetectado is > 0)
                    AddTag(pg.Pagina, "TOTAL_DETECTADO", "TOTAL", pg.ValorDetectado.Value.ToString("F2"));
            }

            // 4) Diagnósticos CIE10 normalizados (reemplaza); valor = ValorTotal del archivo (spec §7)
            await _db.DocumentoDiagnostico.Where(d => d.DataFileId == fic.DocId).ExecuteDeleteAsync();
            var dxVistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dx in fic.Diagnosticos)
            {
                var norm = NormalizarCie10(dx.Codigo);
                if (norm == null || !dxVistos.Add(norm)) continue;   // UQ (DataFileId, Codigo)
                _db.DocumentoDiagnostico.Add(new DocumentoDiagnostico
                {
                    DataFileId     = fic.DocId,
                    Codigo         = norm,
                    CodigoOriginal = Recorta(dx.Codigo, 15),
                    Descripcion    = Recorta(dx.Descripcion, 300),
                    ValorAsignado  = fic.ValorTotal > 0 ? fic.ValorTotal : null,
                    CreatedDate    = DateTime.UtcNow
                });
            }
        }

        // ── REQ-019m: procedimientos con su código de liquidación ─────────────
        // El cruce CPT -> código Saludsa es un LOOKUP: se resuelve contra
        // CatalogoCodigoLiquidacion, no se le pregunta al modelo. Si el catálogo
        // se edita mañana, esto sigue funcionando sin re-desplegar.
        var catalogoFilas = await _db.CatalogoCodigoLiquidacion.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.Codigo, x.Rubro })
            .ToListAsync();
        var catalogo = catalogoFilas.ToDictionary(x => x.Codigo, x => x.Rubro);
        // Indice por palabras significativas, para emparejar cuando NO hay CPT
        var catalogoRaices = catalogoFilas
            .Select(x => (x.Codigo, x.Rubro, Raices: RaicesClinicas(x.Rubro)))
            .Where(x => x.Raices.Count > 0)
            .ToList();

        foreach (var fic in dto.Ficheros)
        {
            if (!idsCaso.Contains(fic.DocId)) continue;

            await _db.DocumentoProcedimiento.Where(x => x.DataFileId == fic.DocId).ExecuteDeleteAsync();


            for (var idxPr = 0; idxPr < fic.Procedimientos.Count; idxPr++)
            {
                var pr = fic.Procedimientos[idxPr];
                var codigo = (pr.CodigoCpt ?? string.Empty).Trim();
                string? codLiq = null, rubroLiq = null, origen = null;

                if (codigo.Length > 0 && catalogo.TryGetValue(codigo, out var rubroExacto))
                {
                    // El documento traía el código y está en el catálogo: match duro.
                    codLiq = codigo; rubroLiq = rubroExacto; origen = "CPT";
                }
                else
                {
                    // Casi ninguna factura trae CPT ("COLONOSCOPIA (VCC)"), así que se
                    // empareja por DESCRIPCION contra el catálogo. Es una SUGERENCIA
                    // (queda marcada como tal) — nunca se presenta como código confirmado.
                    var sug = EmparejarPorDescripcion(pr.Descripcion, catalogoRaices);
                    if (sug != null)
                    {
                        codLiq = sug.Value.Codigo; rubroLiq = sug.Value.Rubro; origen = "DESCRIPCION";
                    }
                }

                // Homologación Lr05 + correlación Lr46 ya resueltas ARRIBA, antes
                // de abrir la transacción. Aquí dentro no se sale a la red.
                homologaciones.TryGetValue((fic.DocId, idxPr), out var hc);
                var homo = hc.Homo;
                var corr = hc.Corr;

                _db.DocumentoProcedimiento.Add(new DocumentoProcedimiento
                {
                    DataFileId        = fic.DocId,
                    PageNumber        = pr.Pagina,
                    CodigoCpt         = Recorta(codigo.Length > 0 ? codigo : null, 20),
                    Descripcion       = Recorta(pr.Descripcion ?? "(sin descripcion)", 500)!,
                    CodigoLiquidacion = Recorta(codLiq, 20),
                    RubroLiquidacion  = Recorta(rubroLiq, 250),
                    OrigenMatch       = origen,
                    // Homologación Lr05
                    NumeroProcedimiento   = homo?.NumeroProcedimiento,
                    NombreLr05            = Recorta(homo?.NombreLr05, 400),
                    CodigoBeneficio       = Recorta(homo?.CodigoBeneficio, 10),
                    EsMedicina            = homo?.EsMedicina,
                    TipoMedicina          = Recorta(homo?.TipoMedicina, 20),
                    ScoreHomologacion     = homo?.Score,
                    HomologacionAmbigua   = homo?.Ambigua,
                    // Correlación Lr46
                    EstadoCorrelacion     = corr?.Estado,
                    CorrelacionProb       = corr?.Probabilidad,
                    CorrelacionDx         = Recorta(corr?.DiagnosticoUsado, 10),
                    CorrelacionConfirmada = corr?.Confirmada,
                    UmbralAplicado        = corr?.UmbralAplicado,
                    CreatedDate           = DateTime.UtcNow
                });
            }
        }

        // ── REQ-019m: tipo de atención del SOBRE (versionado) ────────────────
        if (!string.IsNullOrWhiteSpace(dto.TipoAtencion))
        {
            var previasSobre = await _db.ClasificacionSobre
                .Where(x => x.CaseCode == caseCode && x.IsCurrent).ToListAsync();
            var vSobre = 1;
            foreach (var pv in previasSobre) { pv.IsCurrent = false; vSobre = Math.Max(vSobre, pv.VersionNumber + 1); }

            _db.ClasificacionSobre.Add(new ClasificacionSobre
            {
                CaseCode         = caseCode,
                TipoAtencion     = NormalizarTipoAtencion(dto.TipoAtencion),
                Justificacion    = Recorta(dto.JustificacionAtencion, 2000),
                IndicadoresJson  = dto.IndicadoresAtencion.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(dto.IndicadoresAtencion)
                    : null,
                TipoPredominante = Recorta(dto.TipoPredominante, 30),
                TotalSobre       = dto.TotalSobre,
                ModelCode        = AgenteCode,
                VersionNumber    = vSobre,
                IsCurrent        = true,
                CreatedDate      = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    // ── Emparejador procedimiento → código de liquidación ───────────────────
    // Estas mismas reglas están replicadas en tools/nexus_probar_match_liquidacion.py,
    // que las corre contra el catálogo real con casos y respuesta esperada
    // (14/14 al momento de escribir esto). Si se toca una, se toca la otra y se
    // vuelve a correr el banco: es la única forma de cambiarlo sin adivinar.

    private const int LargoRaiz    = 8;   // COLONOSCOPICO / COLONOSCOPIA → COLONOSC
    private const int MinToken     = 5;   // descarta ruido corto
    private const int MinRaizUnica = 7;   // una sola raíz debe ser específica para valer

    private static readonly HashSet<string> PalabrasVacias = new(StringComparer.Ordinal)
    {
        "DE","DEL","LA","EL","LOS","LAS","CON","SIN","POR","PARA","EN","Y","O","A",
        "UN","UNA","SU","AL","MAS","QUE","SE","ES","OTRAS","OTROS","TOTAL",
        "BASE","HONORARIO","HONORARIOS","CODIGO","NIVEL","TIPO",
        "ESTUDIO","PROCEDIMIENTO","INFORME","PACIENTE"
        // OJO: COMPLETO / SIMPLE / MULTIPLE NO son vacías: en el catálogo
        // distinguen rubros ("Chalazión simple" vs "múltiple", "RTU completa").
    };

    /// <summary>
    /// Raíces significativas de un texto clínico, EN ORDEN: sin tildes, en
    /// mayúsculas, sin puntuación, sin muletillas y truncadas a 8 caracteres.
    /// El orden importa: las dos primeras son el núcleo del procedimiento.
    /// </summary>
    private static List<string> RaicesClinicas(string? texto)
    {
        var sinTildes = new string((texto ?? string.Empty)
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
                         != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray()).ToUpperInvariant();

        var raices = new List<string>();
        foreach (var w in System.Text.RegularExpressions.Regex.Split(sinTildes, @"[^A-Z0-9]+"))
        {
            if (w.Length < MinToken || PalabrasVacias.Contains(w)) continue;
            var raiz = w.Length > LargoRaiz ? w[..LargoRaiz] : w;
            if (!raices.Contains(raiz, StringComparer.Ordinal)) raices.Add(raiz);
        }
        return raices;
    }

    /// <summary>
    /// Empareja la descripción de un procedimiento con el catálogo de códigos de
    /// liquidación cuando el documento NO trae CPT (el caso normal: la factura
    /// dice "COLONOSCOPIA (VCC)", sin código).
    ///
    /// Deliberadamente conservador — es peor sugerir un código equivocado que
    /// dejar la celda vacía:
    ///   1. compara RAÍCES de 8 caracteres, no palabras exactas, para que
    ///      COLONOSCOPICO case con COLONOSCOPIA;
    ///   2. un candidato vale con 2+ raíces en común, o con UNA sola si es una de
    ///      las dos primeras de la descripción (el núcleo), tiene 7+ caracteres y
    ///      el rubro del catálogo es corto (≤2 raíces). Esto es lo que evita que
    ///      "RESECCION DE POLIPO CON PINZA DE BIOPSIA" caiga en "Resección cuello
    ///      vesical" por compartir solo el verbo, y que "…CON INTUBACION" caiga en
    ///      "Intubación endotraqueal";
    ///   3. gana el de más raíces en común; a igualdad, el rubro más genérico;
    ///   4. si los dos mejores empatan y son rubros distintos, no se propone nada.
    /// </summary>
    private static (string Codigo, string Rubro)? EmparejarPorDescripcion(
        string? descripcion,
        List<(string Codigo, string Rubro, List<string> Raices)> catalogo)
    {
        var rd = RaicesClinicas(descripcion);
        if (rd.Count == 0) return null;
        var nucleo = rd.Take(2).ToHashSet(StringComparer.Ordinal);

        var candidatos = new List<(int Comunes, int Tamano, string Codigo, string Rubro)>();
        foreach (var (cod, rubro, rc) in catalogo)
        {
            var comunes = rd.Where(x => rc.Contains(x, StringComparer.Ordinal)).ToList();
            if (comunes.Count == 0) continue;

            var vale = comunes.Count >= 2
                       || (nucleo.Contains(comunes[0])
                           && comunes[0].Length >= MinRaizUnica
                           && rc.Count <= 2);
            if (vale) candidatos.Add((comunes.Count, rc.Count, cod, rubro));
        }
        if (candidatos.Count == 0) return null;

        var orden = candidatos
            .OrderByDescending(x => x.Comunes)
            .ThenBy(x => x.Tamano)              // rubro más genérico = apuesta segura
            .ThenBy(x => x.Codigo, StringComparer.Ordinal)
            .ToList();

        var mejor = orden[0];
        if (orden.Count > 1)
        {
            var seg = orden[1];
            var empate = seg.Comunes == mejor.Comunes && seg.Tamano == mejor.Tamano;
            var mismoRubro = string.Equals(seg.Rubro, mejor.Rubro, StringComparison.OrdinalIgnoreCase);
            if (empate && !mismoRubro) return null;   // ambiguo: mejor no proponer
        }
        return (mejor.Codigo, mejor.Rubro);
    }

    /// <summary>
    /// Fecha del modelo (AAAA-MM-DD, o dd/MM/yyyy si se le escapó) a DateTime.
    /// Null si no se puede leer: NO se inventa una fecha.
    /// </summary>
    private static DateTime? ParseFecha(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var formatos = new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "yyyy/MM/dd", "dd-MM-yyyy" };
        if (DateTime.TryParseExact(valor.Trim(), formatos,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var f))
            return f.Date;
        return DateTime.TryParse(valor.Trim(), System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None, out var g) ? g.Date : null;
    }

    /// <summary>Encaja el tipo de atención en el CHECK de la tabla.</summary>
    private static string NormalizarTipoAtencion(string? valor)
    {
        var v = (valor ?? string.Empty).Trim().ToUpperInvariant().Replace(' ', '_');
        return v switch
        {
            "HOSPITALARIO" => "HOSPITALARIO",
            "HOSPITAL_DIA" or "HOSPITAL_DEL_DIA" or "HOSPITALDIA" => "HOSPITAL_DIA",
            "AMBULATORIO" => "AMBULATORIO",
            _ => "DESCONOCIDO"
        };
    }

    /// <summary>Reconstruye el DTO desde las tablas (lo que se ve es lo que está guardado).</summary>
    private async Task<ClasificacionSobreDto?> ReconstruirDesdeBdAsync(Guid caseCode)
    {
        var clas = await _db.DocumentoClasificacion.AsNoTracking()
            .Where(c => c.DataFileNavigation.CaseCode == caseCode && c.IsCurrent)
            .Select(c => new
            {
                c.DataFileId, c.TipoArchivo, c.ListaTipoArchivo, c.ValorTotal,
                c.EsFacturaValida, c.NumeroFactura, c.ClaveAcceso,
                c.TipoSoporte, c.ResumenSoporte,
                c.EmisorRuc, c.EmisorNombre, c.EmisorNombreComercial,
                c.EmisorTipo, c.EmisorCiudad, c.EmisorPais,
                c.NumeroAutorizacion, c.FechaEmision, c.Subtotal, c.Iva, c.Moneda,
                c.PacienteEdad, c.PacienteSexo, c.PacienteEsTitular,
                c.MedicoNombre, c.MedicoEspecialidad, c.MedicoRegistro, c.FechaAtencion,

                // ── REQ-019y: pie fiscal completo ──
                // La proyección es explícita a propósito (evita traer RawJson, que
                // pesa hasta 60 KB por documento), así que las columnas nuevas hay
                // que nombrarlas aquí o no llegan a la vista.
                c.SubtotalSinImpuestos, c.Subtotal15, c.Subtotal5, c.Subtotal0,
                c.SubtotalNoObjetoIva, c.SubtotalExentoIva,
                c.Descuentos, c.Ice, c.Iva15, c.Iva5,
                c.ServicioValor, c.ServicioPorcentaje, c.Propina,

                // ── REQ-019y: el porqué de la decisión ──
                c.JustificacionTipo, c.SenalesTipo, c.TipoDescartado,
                c.JustificacionDescarte, c.JustificacionFactura, c.JustificacionSoporte,

                // ── REQ-020j ──
                c.PacienteNumeroPersona, c.PacienteCoincide, c.PacienteJustificacion,

                Nombre = c.DataFileNavigation.OriginalName
            })
            .ToListAsync();
        if (clas.Count == 0) return null;

        var ids   = clas.Select(c => c.DataFileId).ToList();
        var items = await _db.DocumentoItem.AsNoTracking().Where(i => ids.Contains(i.DataFileId)).ToListAsync();
        var tags  = await _db.DocumentoTag.AsNoTracking().Where(t => ids.Contains(t.DataFileId)).ToListAsync();
        var dxs   = await _db.DocumentoDiagnostico.AsNoTracking().Where(d => ids.Contains(d.DataFileId)).ToListAsync();
        var procs = await _db.DocumentoProcedimiento.AsNoTracking().Where(x => ids.Contains(x.DataFileId)).ToListAsync();
        var sobre = await _db.ClasificacionSobre.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CaseCode == caseCode && x.IsCurrent);

        var dto = new ClasificacionSobreDto();
        foreach (var c in clas)
        {
            var fic = new ClasificacionFicheroDto
            {
                DocId            = c.DataFileId,
                Nombre           = c.Nombre,
                TipoArchivo      = c.TipoArchivo,
                ListaTipoArchivo = (c.ListaTipoArchivo ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                EsFacturaValida  = c.EsFacturaValida,
                NumeroFactura    = c.NumeroFactura,
                ClaveAcceso      = c.ClaveAcceso,
                ValorTotal       = c.ValorTotal,
                Items = items.Where(i => i.DataFileId == c.DataFileId).OrderBy(i => i.Orden)
                    .Select(i => new ItemFacturaDto
                    {
                        Descripcion = i.Descripcion, TipoRubro = i.TipoRubro, Cantidad = i.Cantidad,
                        ValorUnitario = i.ValorUnitario, ValorTotal = i.ValorTotal, Pagina = i.PageNumber
                    }).ToList(),
                Diagnosticos = dxs.Where(d => d.DataFileId == c.DataFileId)
                    .Select(d => new DiagnosticoDto { Codigo = d.Codigo, Descripcion = d.Descripcion }).ToList(),

                // ── REQ-019y: el porque de la decision, tal como se guardo ──
                JustificacionTipo     = c.JustificacionTipo,
                SenalesTipo           = (c.SenalesTipo ?? "")
                                            .Split('|', StringSplitOptions.RemoveEmptyEntries)
                                            .Select(x => x.Trim())
                                            .Where(x => x.Length > 0)
                                            .ToList(),
                TipoDescartado        = c.TipoDescartado,
                JustificacionDescarte = c.JustificacionDescarte,
                JustificacionFactura  = c.JustificacionFactura,
                JustificacionSoporte  = c.JustificacionSoporte,
                PacienteNumeroPersona = c.PacienteNumeroPersona,
                PacienteCoincide      = c.PacienteCoincide,
                PacienteJustificacion = c.PacienteJustificacion,

                // ── REQ-019m ──
                TipoSoporte    = c.TipoSoporte,
                ResumenSoporte = c.ResumenSoporte,
                FechaAtencion  = c.FechaAtencion?.ToString("yyyy-MM-dd"),
                Emisor = string.IsNullOrWhiteSpace(c.EmisorNombre) && string.IsNullOrWhiteSpace(c.EmisorRuc)
                    ? null
                    : new EmisorDto
                      {
                          Ruc = c.EmisorRuc, Nombre = c.EmisorNombre,
                          NombreComercial = c.EmisorNombreComercial,
                          TipoEstablecimiento = c.EmisorTipo,
                          Ciudad = c.EmisorCiudad, Pais = c.EmisorPais
                      },
                Factura = !c.EsFacturaValida ? null : new FacturaDatosDto
                {
                    Numero = c.NumeroFactura, NumeroAutorizacion = c.NumeroAutorizacion,
                    ClaveAcceso = c.ClaveAcceso, FechaEmision = c.FechaEmision?.ToString("yyyy-MM-dd"),
                    Subtotal = c.Subtotal, Iva = c.Iva, Total = c.ValorTotal, Moneda = c.Moneda,
                    // ── REQ-019y: pie fiscal completo ──
                    SubtotalSinImpuestos = c.SubtotalSinImpuestos,
                    Subtotal15 = c.Subtotal15, Subtotal5 = c.Subtotal5, Subtotal0 = c.Subtotal0,
                    SubtotalNoObjetoIva = c.SubtotalNoObjetoIva,
                    SubtotalExentoIva = c.SubtotalExentoIva,
                    Descuentos = c.Descuentos, Ice = c.Ice,
                    Iva15 = c.Iva15, Iva5 = c.Iva5,
                    ServicioValor = c.ServicioValor, ServicioPorcentaje = c.ServicioPorcentaje,
                    Propina = c.Propina
                },
                Paciente = (c.PacienteEdad == null && c.PacienteSexo == null && c.PacienteEsTitular == null)
                    ? null
                    : new PacienteDto { Edad = c.PacienteEdad, Sexo = c.PacienteSexo, EsTitular = c.PacienteEsTitular },
                MedicoTratante = string.IsNullOrWhiteSpace(c.MedicoNombre)
                    ? null
                    : new MedicoTratanteDto
                      {
                          Nombre = c.MedicoNombre, Especialidad = c.MedicoEspecialidad,
                          Registro = c.MedicoRegistro
                      },
                Procedimientos = procs.Where(x => x.DataFileId == c.DataFileId)
                    .Select(x => new ProcedimientoDto
                    {
                        CodigoCpt = x.CodigoCpt, Descripcion = x.Descripcion, Pagina = x.PageNumber,
                        CodigoLiquidacion = x.CodigoLiquidacion, RubroLiquidacion = x.RubroLiquidacion,
                        OrigenMatch = x.OrigenMatch,
                        NumeroProcedimiento = x.NumeroProcedimiento, NombreLr05 = x.NombreLr05,
                        CodigoBeneficio = x.CodigoBeneficio, EsMedicina = x.EsMedicina,
                        TipoMedicina = x.TipoMedicina,
                        ScoreHomologacion = x.ScoreHomologacion,
                        HomologacionAmbigua = x.HomologacionAmbigua,
                        EstadoCorrelacion = x.EstadoCorrelacion, CorrelacionProb = x.CorrelacionProb,
                        CorrelacionDx = x.CorrelacionDx, CorrelacionConfirmada = x.CorrelacionConfirmada,
                        UmbralAplicado = x.UmbralAplicado
                    }).ToList()
            };

            // Páginas reconstruidas desde los tags con PageNumber
            foreach (var g in tags.Where(t => t.DataFileId == c.DataFileId && t.PageNumber != null)
                                  .GroupBy(t => t.PageNumber!.Value).OrderBy(g => g.Key))
            {
                var tipoPag = g.FirstOrDefault(t => t.Categoria == "TIPO")?.Tag;
                fic.Paginas.Add(new PaginaClasificadaDto
                {
                    Pagina             = g.Key,
                    Tipo               = tipoPag,
                    TipoSoporte        = g.FirstOrDefault(t => t.Categoria == "SOPORTE")?.Tag,
                    Tags               = g.Where(t => t.Categoria != "TIPO" && t.Categoria != "SOPORTE")
                                          .Select(t => t.Tag).ToList(),
                    TieneFacturaValida = g.Any(t => t.Tag == "FACTURA_VALIDA"),
                    ValorDetectado     = decimal.TryParse(
                        g.FirstOrDefault(t => t.Tag == "TOTAL_DETECTADO")?.Valor, out var v) ? v : null
                });
            }
            dto.Ficheros.Add(fic);
        }

        // Split por tipo (desde los ítems; si un doc no tiene ítems, cae a su TipoArchivo)
        foreach (var g in items.GroupBy(i => i.TipoRubro))
            dto.SplitPorTipo.Add(new SplitPorTipoDto
            {
                Tipo       = g.Key,
                Valor      = g.Sum(i => i.ValorTotal ?? 0m),
                Documentos = g.Select(i => i.DataFileId).Distinct().ToList()
            });
        if (dto.SplitPorTipo.Count == 0)
            foreach (var g in clas.GroupBy(c => TipoReembolsoUi.Familia(c.TipoArchivo)))
                dto.SplitPorTipo.Add(new SplitPorTipoDto
                {
                    Tipo = g.Key, Valor = g.Sum(x => x.ValorTotal),
                    Documentos = g.Select(x => x.DataFileId).ToList()
                });

        dto.TotalSobre = clas.Sum(c => c.ValorTotal);
        dto.TipoPredominante = clas.OrderByDescending(c => c.ValorTotal)
            .Select(c => c.TipoArchivo).FirstOrDefault();

        // Tipo de atención del sobre, tal como quedó guardado
        if (sobre != null)
        {
            dto.TipoAtencion = sobre.TipoAtencion;
            dto.JustificacionAtencion = sobre.Justificacion;
            if (!string.IsNullOrWhiteSpace(sobre.IndicadoresJson))
            {
                try
                {
                    dto.IndicadoresAtencion = System.Text.Json.JsonSerializer
                        .Deserialize<List<IndicadorAtencionDto>>(sobre.IndicadoresJson!) ?? new();
                }
                catch (System.Text.Json.JsonException) { /* si el JSON viejo no encaja, sin indicadores */ }
            }
        }

        // ResumenDiagnosticos agrupado (spec §7).
        // Se conservan los documentos de origen: al agrupar por código se perdía
        // esa traza y era el único bloque de la pantalla que no podía llevar al
        // PDF, porque no sabía a qué documento anclar la evidencia.
        var nombrePorDoc = clas.ToDictionary(x => x.DataFileId, x => x.Nombre);
        foreach (var g in dxs.GroupBy(d => d.Codigo))
        {
            var docs = g.Select(x => x.DataFileId).Distinct().OrderBy(x => x).ToList();
            dto.ResumenDiagnosticos.Add(new ResumenDiagnosticoDto
            {
                Codigo      = g.Key,
                Descripcion = g.Select(x => x.Descripcion).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                ValorTotal  = g.Sum(x => x.ValorAsignado ?? 0m),
                DocIds      = docs,
                DocNombre   = docs.Count > 0 && nombrePorDoc.TryGetValue(docs[0], out var nom) ? nom : null
            });
        }

        return dto;
    }

    // ── Utilidades ───────────────────────────────────────────────────────

    private static string? Recorta(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);

    private static readonly HashSet<string> RubrosValidos = new(StringComparer.OrdinalIgnoreCase)
        { "MED", "ATE_HOS", "PRO", "CON_MED", "LAB_CLI", "LAB_IMA", "TER", "BEN_ADI", "GENERAL" };

    /// <summary>Fuerza el rubro al dominio del CHECK constraint (evita fallo de inserción).</summary>
    private static string NormalizarRubro(string? rubro)
    {
        var r = TipoReembolsoUi.Familia(rubro);
        return RubrosValidos.Contains(r) ? r.ToUpperInvariant() : "GENERAL";
    }

    /// <summary>CIE10 sin puntos ni espacios, en mayúsculas (spec §2): 'M51.9' ⇒ 'M519'.</summary>
    private static string? NormalizarCie10(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return null;
        var s = new string(codigo.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return string.IsNullOrEmpty(s) ? null : (s.Length > 10 ? s[..10] : s);
    }

    private static string? ExtractJson(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var start = texto.IndexOf('{');
        if (start < 0) return null;
        int depth = 0; bool inStr = false; char prev = '\0';
        for (var i = start; i < texto.Length; i++)
        {
            var c = texto[i];
            if (inStr) { if (c == '"' && prev != '\\') inStr = false; }
            else
            {
                if (c == '"') inStr = true;
                else if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return texto.Substring(start, i - start + 1); }
            }
            prev = c;
        }
        return null;
    }
}
