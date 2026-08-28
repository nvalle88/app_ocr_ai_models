using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using app_ocr_ai_models.Areas.Studio.Models;
using app_ocr_ai_models.Data;
using app_ocr_ai_models.Services.Ai;
using app_tramites.Services.NexusProcess;
using app_tramites.Models.ModelAi;
using System.Text;
using app_tramites.Services.Ai;
using app_tramites.Services.Ai.Tools;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ModeloVista = app_tramites.Models.ViewModel;

namespace app_ocr_ai_models.Areas.Studio.Controllers;

// =============================================================================
// REQ-020 — El portal del afiliado: el otro rol del Studio.
//
// La misma maquinaria del auditor, vista desde el lado de quien paga. El
// afiliado entra con su cédula, elige uno de sus contratos, adjunta lo que
// tiene, y el sistema le dice qué se cubre, en qué porcentaje, qué no y por
// qué — con el término del contrato explicado, no escondido.
//
// Tres decisiones de fondo:
//
//  1. Los contratos NO se le preguntan al modelo. Se consultan por API
//     (PortalClienteService) y se guarda la foto de lo que devolvió, para que
//     mañana se sepa con qué datos se decidió.
//
//  2. El caso que abre el afiliado es un caso normal del Studio. Así el auditor
//     lo ve en su bandeja sin ninguna integración extra, y la tipificación, el
//     expediente y la auditoría funcionan igual.
//
//  3. La explicación al cliente se genera aparte y se guarda. No se rehace en
//     cada recarga: cuesta dinero y, sobre todo, debe ser estable — que el
//     mismo expediente diga hoy una cosa y mañana otra destruye la confianza.
// =============================================================================

[Area("Studio")]
[Authorize]
public sealed class ClienteController : Controller
{
    /// <summary>El proceso bajo el que se abren los casos del portal.</summary>
    private const string ProcesoPortal = "PORTAL_CLIENTE";
    /// <summary>El agente que traduce el expediente al idioma del afiliado.</summary>
    private const string AgentePortal  = "AGENTE_PORTAL_CLIENTE";

    private readonly OCRDbContext _db;
    private readonly PortalClienteService _portal;
    private readonly INexusService _nexus;
    private readonly AiCompletionServiceFactory _factory;
    private readonly IToolExecutor? _toolExecutor;
    private readonly ILogger<ClienteController> _log;
    private readonly Services.IBuscadorFacturaRepetida _repetidas;
    private readonly Services.IEvaluadorDeCobertura? _cobertura;

    public ClienteController(
        OCRDbContext db,
        PortalClienteService portal,
        INexusService nexus,
        AiCompletionServiceFactory factory,
        ILogger<ClienteController> log,
        Services.IBuscadorFacturaRepetida repetidas,
        Services.IEvaluadorDeCobertura? cobertura = null,
        IToolExecutor? toolExecutor = null)
    {
        _repetidas    = repetidas;
        _cobertura    = cobertura;
        _db           = db;
        _portal       = portal;
        _nexus        = nexus;
        _factory      = factory;
        _toolExecutor = toolExecutor;
        _log          = log;
    }

    // ─────────────────────────────────────────────────────────────────────
    // GET /Studio/Cliente — la puerta: la cédula
    // ─────────────────────────────────────────────────────────────────────
    [HttpGet]
    public IActionResult Index() => View(new ClienteIdentificacionVm());

    // ─────────────────────────────────────────────────────────────────────
    // POST /Studio/Cliente/Contratos — cédula → sus contratos
    // ─────────────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Contratos(ClienteIdentificacionVm vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Cedula))
        {
            vm.Error = "Escriba su número de cédula.";
            return View(nameof(Index), vm);
        }

        // El caso se abre ANTES de consultar para que la llamada quede colgada
        // de él y el afiliado pueda ver qué se consultó, igual que el auditor.
        var caso = await AbrirCasoAsync();

        var res = await _portal.BuscarContratosAsync(vm.Cedula, caso.CaseCode);
        if (!res.EsOk)
        {
            // El caso queda huérfano; se cierra para no ensuciar la bandeja del
            // auditor con intentos que nunca llegaron a nada.
            await DescartarCasoAsync(caso.CaseCode);
            vm.Error = res.Mensaje;
            return View(nameof(Index), vm);
        }

        return View(new ClienteContratosVm
        {
            Cedula    = res.Cedula!,
            CaseCode  = caso.CaseCode,
            Contratos = res.Contratos
        });
    }

    // ─────────────────────────────────────────────────────────────────────
    // POST /Studio/Cliente/Elegir — se queda con un contrato y abre la solicitud
    // ─────────────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Elegir(Guid caseCode, string cedula, string numeroContrato, string contratoJson)
    {
        if (caseCode == Guid.Empty || string.IsNullOrWhiteSpace(numeroContrato))
            return RedirectToAction(nameof(Index));

        var c = LeerContrato(contratoJson);

        var sol = await _db.SolicitudCliente.FirstOrDefaultAsync(x => x.CaseCode == caseCode);
        if (sol == null)
        {
            sol = new SolicitudCliente { CaseCode = caseCode, CreatedDate = DateTime.UtcNow };
            _db.SolicitudCliente.Add(sol);
        }

        sol.Cedula         = PortalClienteService.NormalizarCedula(cedula);
        sol.NumeroContrato = numeroContrato;
        sol.CodigoProducto = c?.Producto;
        sol.CodigoRegion   = c?.Region;
        sol.CodigoPlan     = c?.CodigoPlan;
        sol.NombrePlan     = c?.NombreComercial ?? c?.NombrePlan;
        sol.NombreTitular  = c?.TitularNombre;
        sol.NumeroPersona  = c?.TitularNumero;
        sol.ContratoJson   = contratoJson;
        // La lista completa se guarda para poder repintar el selector sin
        // volver a llamar a la API.
        sol.BeneficiariosJson = c is { Beneficiarios.Count: > 0 }
            ? JsonSerializer.Serialize(c.Beneficiarios)
            : null;
        sol.Estado         = "ADJUNTANDO";
        sol.ModifiedDate   = DateTime.UtcNow;

        // El contexto del sobre es lo que consumen la tipificación y la
        // resolución; sin él, el caso del portal no sabría de quién es.
        var ctx = await _db.Note.FirstOrDefaultAsync(
            n => n.CaseCode == caseCode && n.Title == OcrPromptHelper.ContextoSobreNoteTitle);
        var detalle = JsonSerializer.Serialize(new
        {
            origen                = "Portal del afiliado",
            numeroSobre           = (string?)null,
            numeroContrato        = numeroContrato,
            codigoProducto        = c?.Producto,
            codigoRegion          = c?.Region,
            numeroPersonaPaciente = c?.TitularNumero,
            nombreTitular         = c?.TitularNombre,
            cedula                = sol.Cedula
        });

        if (ctx == null)
        {
            _db.Note.Add(new Note
            {
                CaseCode  = caseCode,
                Title     = OcrPromptHelper.ContextoSobreNoteTitle,
                Detail    = detalle,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = User?.Identity?.Name ?? "portal"
            });
        }
        else
        {
            ctx.Detail = detalle;
        }

        await _db.SaveChangesAsync();

        // Con un solo beneficiario no se le hace elegir: se asigna y sigue.
        var soloUno = c?.Beneficiarios.Count == 1 ? c.Beneficiarios[0] : null;
        if (soloUno != null)
        {
            await FijarBeneficiarioAsync(caseCode, soloUno);
            return RedirectToAction(nameof(Solicitud), new { caseCode });
        }

        return RedirectToAction(nameof(Beneficiario), new { caseCode });
    }

    // ─────────────────────────────────────────────────────────────────────
    // GET /Studio/Cliente/Beneficiario — ¿para quién es este reembolso?
    //
    // Este paso no es un formalismo. El deducible consumido, la carencia y las
    // preexistencias son de la PERSONA, no del contrato: elegir mal liquida el
    // gasto de un hijo contra el deducible del padre y se salta la carencia de
    // quien de verdad se atendió.
    // ─────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Beneficiario(Guid caseCode)
    {
        var sol = await _db.SolicitudCliente.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CaseCode == caseCode);
        if (sol == null) return RedirectToAction(nameof(Index));

        var contrato = LeerContrato(sol.ContratoJson);
        var lista    = LeerBeneficiariosGuardados(sol.BeneficiariosJson)
                       ?? contrato?.Beneficiarios
                       ?? new List<BeneficiarioAfiliado>();

        if (lista.Count == 0)
        {
            // Sin lista no se puede elegir; se sigue con el titular y se avisa.
            TempData["Aviso"] = "No pudimos traer la lista de beneficiarios de su contrato. "
                              + "Seguimos con el titular; si el gasto es de otra persona, indíquelo al confirmar.";
            return RedirectToAction(nameof(Solicitud), new { caseCode });
        }

        return View(new ClienteBeneficiarioVm
        {
            CaseCode       = caseCode,
            NumeroContrato = sol.NumeroContrato,
            NombrePlan     = sol.NombrePlan,
            Elegido        = sol.NumeroPersona,
            Beneficiarios  = lista
        });
    }

    // ─────────────────────────────────────────────────────────────────────
    // POST /Studio/Cliente/ElegirBeneficiario
    // ─────────────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ElegirBeneficiario(Guid caseCode, int numeroPersona)
    {
        var sol = await _db.SolicitudCliente.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CaseCode == caseCode);
        if (sol == null) return RedirectToAction(nameof(Index));

        var lista = LeerBeneficiariosGuardados(sol.BeneficiariosJson)
                    ?? LeerContrato(sol.ContratoJson)?.Beneficiarios
                    ?? new List<BeneficiarioAfiliado>();

        var elegido = lista.FirstOrDefault(b => b.NumeroPersona == numeroPersona);
        if (elegido == null)
        {
            TempData["Error"] = "Elija a quién va dirigido el reembolso.";
            return RedirectToAction(nameof(Beneficiario), new { caseCode });
        }

        await FijarBeneficiarioAsync(caseCode, elegido);
        return RedirectToAction(nameof(Solicitud), new { caseCode });
    }

    /// <summary>
    /// Fija el beneficiario en la solicitud Y en el contexto del sobre. Lo
    /// segundo es lo que hace que valga "para todo": la tipificación, la
    /// auditoría y la resolución leen ese contexto, así que a partir de aquí
    /// todo el pipeline trabaja contra la persona correcta.
    /// </summary>
    private async Task FijarBeneficiarioAsync(Guid caseCode, BeneficiarioAfiliado b)
    {
        var sol = await _db.SolicitudCliente.FirstOrDefaultAsync(x => x.CaseCode == caseCode);
        if (sol == null) return;

        // La lista completa viaja al contexto para que el clasificador pueda
        // decir a QUIÉN corresponde cada factura, no sólo si es del elegido.
        var todas = LeerBeneficiariosGuardados(sol.BeneficiariosJson)
                    ?? LeerContrato(sol.ContratoJson)?.Beneficiarios
                    ?? new List<BeneficiarioAfiliado> { b };

        sol.NumeroPersona        = b.NumeroPersona;
        sol.NombreBeneficiario   = b.NombreCompleto;
        sol.CedulaBeneficiario   = b.Documento;
        sol.RelacionBeneficiario = b.Relacion;
        sol.EdadBeneficiario     = b.Edad;
        sol.GeneroBeneficiario   = b.Genero;
        sol.DeducibleCubierto    = b.DeducibleCubierto;
        sol.EnCarencia           = b.EnCarencia;
        sol.DiasFinCarencia      = b.DiasFinCarencia;
        sol.TienePreexistencias  = b.Preexistencias > 0;
        sol.ModifiedDate         = DateTime.UtcNow;

        // El contexto del sobre: la cédula de acceso NO se toca (es la identidad
        // que autoriza y con la que trabaja el guardián anti-IDOR); lo que cambia
        // es de quién es el gasto.
        var ctx = await _db.Note.FirstOrDefaultAsync(
            n => n.CaseCode == caseCode && n.Title == OcrPromptHelper.ContextoSobreNoteTitle);

        var detalle = JsonSerializer.Serialize(new
        {
            origen                = "Portal del afiliado",
            numeroSobre           = (string?)null,
            numeroContrato        = sol.NumeroContrato,
            codigoProducto        = sol.CodigoProducto,
            codigoRegion          = sol.CodigoRegion,
            numeroPersonaPaciente = b.NumeroPersona,
            nombreTitular         = sol.NombreTitular,
            cedula                = sol.Cedula,

            // De quién es el gasto y en qué condiciones está esa persona.
            beneficiario = new
            {
                numeroPersona     = b.NumeroPersona,
                nombre            = b.NombreCompleto,
                cedula            = b.Documento,
                relacion          = b.Relacion,
                edad              = b.Edad,
                genero            = b.Genero,
                deducibleCubierto = b.DeducibleCubierto,
                // Son DOS carencias, no una: se puede estar fuera de la
                // ambulatoria y dentro de la hospitalaria a la vez. La
                // hospitalaria se leia del contrato y NO se le mandaba al
                // agente, asi que no podia aplicarla aunque quisiera.
                enCarenciaAmbulatoria  = b.EnCarencia,
                enCarenciaHospitalaria = b.EnCarenciaHosp,
                diasFinCarencia        = b.DiasFinCarencia,
                preexistencias    = b.Preexistencias
            },

            // TODAS las personas del contrato. Es el otro lado de la
            // comparación: sin esta lista, una factura de un hijo legítimo y
            // una de un tercero ajeno son indistinguibles para el clasificador.
            personasDelContrato = todas.Select(x => new
            {
                numeroPersona = x.NumeroPersona,
                nombre        = x.NombreCompleto,
                cedula        = x.Documento,
                relacion      = x.Relacion,
                edad          = x.Edad,
                genero        = x.Genero
            }).ToList()
        });

        if (ctx == null)
        {
            _db.Note.Add(new Note
            {
                CaseCode  = caseCode,
                Title     = OcrPromptHelper.ContextoSobreNoteTitle,
                Detail    = detalle,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = User?.Identity?.Name ?? "portal"
            });
        }
        else
        {
            ctx.Detail = detalle;
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>Los beneficiarios que se guardaron al elegir el contrato.</summary>
    private static List<BeneficiarioAfiliado>? LeerBeneficiariosGuardados(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<BeneficiarioAfiliado>>(json);
        }
        catch (JsonException) { return null; }
    }

    // ─────────────────────────────────────────────────────────────────────
    // GET /Studio/Cliente/Solicitud/{caseCode} — adjuntar y ver el estado
    // ─────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Solicitud(Guid caseCode)
    {
        var vm = await ArmarSolicitudAsync(caseCode);
        if (vm == null) return RedirectToAction(nameof(Index));
        return View(vm);
    }

    // ─────────────────────────────────────────────────────────────────────
    // POST /Studio/Cliente/Adjuntar — el afiliado sube sus documentos
    // ─────────────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(80_000_000)]          // ~80 MB por tanda: un sobre grande cabe
    public async Task<IActionResult> Adjuntar(Guid caseCode, List<IFormFile> archivos, decimal? valorPresentado)
    {
        var sol = await _db.SolicitudCliente.FirstOrDefaultAsync(x => x.CaseCode == caseCode);
        if (sol == null) return RedirectToAction(nameof(Index));

        if (valorPresentado.HasValue)
        {
            sol.ValorPresentado = valorPresentado;
            sol.ModifiedDate    = DateTime.UtcNow;
        }

        if (archivos == null || archivos.Count == 0)
        {
            TempData["Error"] = "Elija al menos un archivo para adjuntar.";
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Solicitud), new { caseCode });
        }

        // Lo que de verdad sube la gente, no lo que nos gustaría que subiera:
        //   .heic  el formato POR DEFECTO de la cámara del iPhone. Sin él, todo
        //          afiliado con iPhone queda fuera en la puerta.
        //   .xml   la factura electrónica. Medido: 50 ficheros, 49 legibles.
        //          Trae RUC, número, clave de acceso y el desglose fiscal ya
        //          estructurado — es mejor fuente que cualquier foto.
        var permitidas = new[]
        {
            ".pdf", ".jpg", ".jpeg", ".png", ".tif", ".tiff",
            ".heic", ".heif", ".webp",
            ".xml"
        };
        var paraSubir  = new List<ModeloVista.OcrFile>();
        var rechazados = new List<string>();

        foreach (var f in archivos)
        {
            if (f.Length == 0) continue;

            var ext = System.IO.Path.GetExtension(f.FileName)?.ToLowerInvariant() ?? "";
            if (!permitidas.Contains(ext))
            {
                // Se dice QUÉ archivo y POR QUÉ: "formato no soportado" a secas
                // deja al afiliado sin saber cuál de los ocho falló.
                rechazados.Add($"{f.FileName}: no admitimos el formato {ext}. "
                              + "Puede subir PDF, fotos (JPG, PNG, HEIC) o el XML de la factura electrónica.");
                continue;
            }

            using var ms = new System.IO.MemoryStream();
            await f.CopyToAsync(ms);
            paraSubir.Add(new ModeloVista.OcrFile
            {
                FileName  = f.FileName,
                Extension = ext.TrimStart('.'),
                Content   = Convert.ToBase64String(ms.ToArray()),
                Url       = string.Empty
            });
        }

        if (paraSubir.Count > 0)
        {
            try
            {
                var nuevos = await _nexus.AddDocumentsToCase(caseCode, paraSubir);
                TempData["Ok"] = nuevos.Count == 1
                    ? "Recibimos su documento."
                    : $"Recibimos sus {nuevos.Count} documentos.";
                sol.Estado = "ADJUNTANDO";

                // Identificar QUÉ es cada documento no puede depender de que el
                // afiliado adivine que hay un botón: al volver, la pantalla lanza
                // la tipificación sola. Sin esto la tarjeta se queda en "lo
                // estamos leyendo" para siempre y no clasifica nada.
                TempData["Clasificar"] = true;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[Portal] Falló el adjunto del caso {Caso}", caseCode);
                TempData["Error"] = "No pudimos procesar los archivos. Inténtelo de nuevo.";
            }
        }

        if (rechazados.Count > 0)
            TempData["Aviso"] = "No se pudieron adjuntar: " + string.Join("; ", rechazados);

        sol.ModifiedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Solicitud), new { caseCode });
    }


    // ─────────────────────────────────────────────────────────────────────
    // POST /Studio/Cliente/Eliminar — quitar un documento subido por error
    //
    // Sin esto, una foto equivocada se queda en el expediente para siempre y
    // ensucia la resolución: el sistema la tipifica, le asigna un valor y la
    // suma. Poder deshacer no es una comodidad, es parte de que el resultado
    // sea correcto.
    // ─────────────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Eliminar(Guid caseCode, int docId)
    {
        var sol = await _db.SolicitudCliente.FirstOrDefaultAsync(x => x.CaseCode == caseCode);
        if (sol == null) return RedirectToAction(nameof(Index));

        // Que el documento sea DE ESTE CASO: el docId viaja por el formulario y
        // sin esta comprobación se podría borrar el documento de otro afiliado.
        var doc = await _db.DataFile
            .FirstOrDefaultAsync(d => d.Id == docId && d.CaseCode == caseCode);

        if (doc == null)
        {
            TempData["Error"] = "Ese documento ya no está en su solicitud.";
            return RedirectToAction(nameof(Solicitud), new { caseCode });
        }

        var nombre = doc.OriginalName;

        // Todo lo que el pipeline dedujo de él se va con él. Si quedara la
        // clasificación huérfana, el valor de un documento borrado seguiría
        // sumando en los totales.
        await _db.DocumentoClasificacion.Where(c => c.DataFileId == docId).ExecuteDeleteAsync();
        await _db.DocumentoItem.Where(i => i.DataFileId == docId).ExecuteDeleteAsync();
        await _db.DocumentoTag.Where(t => t.DataFileId == docId).ExecuteDeleteAsync();
        await _db.DocumentoDiagnostico.Where(d => d.DataFileId == docId).ExecuteDeleteAsync();
        await _db.DocumentoProcedimiento.Where(x => x.DataFileId == docId).ExecuteDeleteAsync();
        await _db.DataFilePage.Where(pg => pg.DataFileId == docId).ExecuteDeleteAsync();

        // Las ejecuciones apuntan al documento por FK. NO se borran: son la
        // prueba de qué se consultó y con qué, y perderlas dejaría el caso sin
        // rastro. Se les suelta el documento — la columna admite null desde
        // REQ-020b justamente porque una ejecución puede no operar sobre
        // ninguno. Sin esto, el DELETE choca con FK_StepExecution_DataFile y el
        // afiliado recibe un error 500 al pulsar «Quitar».
        await _db.StepExecution
            .Where(se => se.DataFileId == docId)
            .ExecuteUpdateAsync(x => x.SetProperty(se => se.DataFileId, (int?)null));

        _db.DataFile.Remove(doc);

        // Lo ya resuelto deja de valer: se calculó contando este documento.
        var previas = await _db.Note
            .Where(n => n.CaseCode == caseCode
                        && (n.Title == "ResolucionReembolso"
                            || n.Title == "AuditoriaMedicina"
                            || n.Title == "ExpedienteDocumental"))
            .ToListAsync();
        if (previas.Count > 0) _db.Note.RemoveRange(previas);

        sol.ExplicacionJson  = null;
        sol.Estado           = "ADJUNTANDO";
        sol.DatosConfirmados = false;
        sol.ModifiedDate     = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        TempData["Ok"] = $"Quitamos «{nombre}» de su solicitud. "
                       + "Tendrá que volver a revisar su reembolso para recalcularlo.";
        return RedirectToAction(nameof(Solicitud), new { caseCode });
    }

    // ─────────────────────────────────────────────────────────────────────
    // POST /Studio/Cliente/Confirmar — el afiliado da por buenos los datos leídos
    // ─────────────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirmar(Guid caseCode)
    {
        var sol = await _db.SolicitudCliente.FirstOrDefaultAsync(x => x.CaseCode == caseCode);
        if (sol == null) return RedirectToAction(nameof(Index));

        sol.DatosConfirmados  = true;
        sol.FechaConfirmacion = DateTime.UtcNow;
        sol.Estado            = "CONFIRMADA";
        sol.ModifiedDate      = DateTime.UtcNow;

        // Queda constancia en el expediente: si después se discute qué se
        // presentó, la confirmación del afiliado está fechada.
        _db.Note.Add(new Note
        {
            CaseCode  = caseCode,
            Title     = "ConfirmacionAfiliado",
            Detail    = JsonSerializer.Serialize(new
            {
                confirmadoEn    = sol.FechaConfirmacion,
                cedula          = sol.Cedula,
                numeroContrato  = sol.NumeroContrato,
                valorPresentado = sol.ValorPresentado
            }),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = User?.Identity?.Name ?? "portal"
        });

        await _db.SaveChangesAsync();
        TempData["Ok"] = "Gracias. Sus datos quedaron confirmados y su solicitud pasa a revisión.";
        return RedirectToAction(nameof(Solicitud), new { caseCode });
    }

    // ─────────────────────────────────────────────────────────────────────
    // POST /Studio/Cliente/Explicar — "¿qué me cubren y por qué?"
    //
    // Se ejecuta el agente del portal CON sus herramientas: las coberturas y el
    // deducible se consultan, no se suponen. Al afiliado se le puede decir "no
    // lo sé todavía", pero nunca un porcentaje inventado.
    // ─────────────────────────────────────────────────────────────────────
    // ── Lo que el agente está haciendo AHORA MISMO ───────────────────────────
    //
    // Los pasos del portal tardan entre veinte y noventa segundos, y por dentro
    // el agente consulta el SRI, los reclamos de producción, el convenio del
    // prestador y las coberturas del plan. Nada de eso se veía: el afiliado
    // miraba un rótulo fijo y recibía un veredicto sin haber visto el trabajo.
    //
    // Mientras el paso corre, el navegador pregunta aquí cada segundo y medio y
    // va pintando lo que ya se hizo. No es una animación: cada línea existe
    // porque hay una llamada registrada en ToolInvocation. Si el agente no
    // consulta nada, aquí no aparece nada — que también es la verdad.
    //
    // `desde` es el último id ya pintado, para no repetir ni traer de más.
    [HttpGet]
    public async Task<IActionResult> Progreso(Guid caseCode, long desde = 0)
    {
        // Sin esto, cualquiera con un caseCode ajeno leería el avance de otro.
        var mio = await _db.SolicitudCliente.AnyAsync(x => x.CaseCode == caseCode);
        if (!mio) return Json(new { pasos = Array.Empty<object>(), ultimo = desde });

        // Las invocaciones del caso, por sus ejecuciones. Se piden en crudo y se
        // narran fuera del SQL: la traducción es una regla, no una consulta.
        var crudas = await (
            from ti in _db.ToolInvocation
            join se in _db.StepExecution on ti.ExecutionId equals se.ExecutionId
            where se.CaseCode == caseCode && ti.InvocationId > desde
            orderby ti.InvocationId
            select new
            {
                ti.InvocationId, ti.ToolCode, ti.ResponseJson,
                ti.IsError, ti.StartDate, ti.EndDate
            }).Take(40).ToListAsync();

        var pasos = crudas
            .Select(c => NarradorDeTools.Narrar(
                c.InvocationId, c.ToolCode, c.ResponseJson, c.IsError, c.StartDate, c.EndDate))
            .ToList();

        return Json(new
        {
            pasos,
            ultimo = crudas.Count > 0 ? crudas[^1].InvocationId : desde
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Explicar(Guid caseCode)
    {
        var sol = await _db.SolicitudCliente.FirstOrDefaultAsync(x => x.CaseCode == caseCode);
        if (sol == null) return RedirectToAction(nameof(Index));

        var caso = await _db.ProcessCase
            .Include(c => c.DataFile)
            .Include(c => c.Notes)
            .FirstOrDefaultAsync(c => c.CaseCode == caseCode);
        if (caso == null) return RedirectToAction(nameof(Index));

        if (caso.DataFile.Count == 0)
        {
            TempData["Aviso"] = "Adjunte primero sus documentos y le decimos qué le cubre su plan.";
            return RedirectToAction(nameof(Solicitud), new { caseCode });
        }

        var agent = await _db.Agent
            .Include(a => a.AgentConfig)
            .Include(a => a.OPAIModelTool).ThenInclude(mt => mt.ToolCodeNavigation)
            .FirstOrDefaultAsync(a => a.Code == AgentePortal && a.IsActive);

        if (agent?.AgentConfig == null)
        {
            TempData["Error"] = "El asistente no está disponible en este momento.";
            return RedirectToAction(nameof(Solicitud), new { caseCode });
        }

        var config = agent.AgentConfig;

        // Lo que ya sabemos del expediente, resumido para el agente. Se le da
        // masticado a propósito: cuanto menos tenga que deducir del OCR crudo,
        // menos margen hay de que se invente algo.
        var clas = await _db.DocumentoClasificacion.AsNoTracking()
            .Where(c => c.DataFileNavigation.CaseCode == caseCode && c.IsCurrent)
            .Select(c => new
            {
                c.DataFileId, c.TipoArchivo, c.TipoSoporte, c.EsFacturaValida,
                c.ValorTotal, c.EmisorNombre, c.EmisorRuc, c.NumeroFactura,
                c.FechaEmision, c.SubtotalSinImpuestos, c.Descuentos, c.ServicioValor,
                Nombre = c.DataFileNavigation.OriginalName
            })
            .ToListAsync();

        var dxs = await _db.DocumentoDiagnostico.AsNoTracking()
            .Where(d => clas.Select(c => c.DataFileId).Contains(d.DataFileId))
            .Select(d => new { d.Codigo, d.Descripcion })
            .Distinct()
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("## El plan del afiliado");
        sb.AppendLine($"Contrato {sol.NumeroContrato} · producto {sol.CodigoProducto} · región {sol.CodigoRegion}");
        sb.AppendLine($"Plan: {sol.NombrePlan} (código {sol.CodigoPlan})");
        sb.AppendLine($"Titular: {sol.NombreTitular} · cédula {sol.Cedula} · persona {sol.NumeroPersona}");
        var contrato = LeerContrato(sol.ContratoJson);
        if (contrato != null)
        {
            sb.AppendLine($"Cobertura máxima anual: {contrato.CoberturaMaxima:N2}");
            sb.AppendLine($"Deducible del contrato: {contrato.DeducibleTotal:N2}");
            sb.AppendLine($"Estado del contrato: {contrato.Estado}");
            if (contrato.EsMoroso == true) sb.AppendLine("ATENCIÓN: el contrato figura con cuotas pendientes.");
            if (contrato.TieneImpedimento == true)
                sb.AppendLine($"ATENCIÓN: impedimento registrado — {contrato.MotivoImpedimento}");
        }
        sb.AppendLine();

        sb.AppendLine("## Lo que presentó");
        if (sol.ValorPresentado.HasValue)
            sb.AppendLine($"El afiliado declaró haber gastado {sol.ValorPresentado:N2}.");
        foreach (var c in clas)
        {
            sb.AppendLine($"- docId {c.DataFileId} | {c.Nombre}");
            sb.AppendLine($"  tipo {c.TipoArchivo}{(string.IsNullOrWhiteSpace(c.TipoSoporte) ? "" : " / " + c.TipoSoporte)}"
                        + $" | factura válida: {(c.EsFacturaValida ? "sí" : "no")} | valor {c.ValorTotal:N2}");
            if (!string.IsNullOrWhiteSpace(c.EmisorNombre))
                sb.AppendLine($"  emitida por {c.EmisorNombre} (RUC {c.EmisorRuc}) el {c.FechaEmision:yyyy-MM-dd}, factura {c.NumeroFactura}");
            if (c.Descuentos.HasValue || c.ServicioValor.HasValue)
                sb.AppendLine($"  descuentos {c.Descuentos:N2} · servicio {c.ServicioValor:N2} (no son gasto médico)");
        }
        if (dxs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Diagnósticos leídos");
            foreach (var d in dxs) sb.AppendLine($"- {d.Codigo} {d.Descripcion}");
        }

        sb.AppendLine();
        sb.AppendLine("## Solicitud");
        sb.AppendLine("Explícale a esta persona qué le cubre su plan de lo que presentó, en qué");
        sb.AppendLine("porcentaje, qué no le cubre y por qué, y qué documentos le faltan. Consulta");
        sb.AppendLine("las coberturas y el deducible con las herramientas antes de dar cualquier");
        sb.AppendLine("porcentaje. Devuelve únicamente el JSON del formato de salida.");

        var aiRequest = new AiCompletionRequest
        {
            SystemPrompt = OcrPromptHelper.ResolveSystemPrompt(agent),
            UserMessage  = sb.ToString(),
            MaxTokens    = agent.MaxTokens ?? 6000,
            Temperature  = agent.Temperature,
            ThinkingMode = agent.ThinkingMode
        };

        var enabledTools = agent.OPAIModelTool
            .Where(mt => mt.IsEnabled && mt.ToolCodeNavigation?.IsActive == true)
            .Select(mt => mt.ToolCodeNavigation!)
            .ToList();
        var conTools = enabledTools.Count > 0 && _toolExecutor != null
                       && string.Equals(config.Provider, "Anthropic", StringComparison.OrdinalIgnoreCase);

        string texto;
        try
        {
            var svc = _factory.Create(config);
            if (conTools)
            {
                var exec = new StepExecution
                {
                    CaseCode       = caseCode,
                    StepOrder      = 1,
                    DataFileId     = caso.DataFile.FirstOrDefault()?.Id,
                    ModelCode      = agent.Code,
                    RequestContent = aiRequest.UserMessage,
                    Status         = "Running",
                    StartDate      = DateTime.UtcNow,
                    EndpointUrl    = config.EndpointUrl
                };
                _db.StepExecution.Add(exec);
                await _db.SaveChangesAsync();

                var ctx = new ToolsContext
                {
                    AvailableTools = enabledTools,
                    AgentCode      = agent.Code,
                    ExecutionId    = exec.ExecutionId,
                    CaseIdentity   = sol.Cedula,
                    ToolChoice     = agent.ToolChoice
                };
                var res = await svc.CompleteWithToolsAsync(aiRequest, ctx, _toolExecutor!, HttpContext.RequestAborted);
                texto = res.Text;

                exec.ResponseContent = texto;
                exec.Status  = "Completed";
                exec.EndDate = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
            else
            {
                var res = await svc.CompleteAsync(aiRequest, HttpContext.RequestAborted);
                texto = res.Text;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[Portal] No se pudo explicar la cobertura del caso {Caso}", caseCode);
            TempData["Error"] = "No pudimos preparar su explicación ahora mismo. Inténtelo en unos minutos.";
            return RedirectToAction(nameof(Solicitud), new { caseCode });
        }

        sol.ExplicacionJson = ExtraerJson(texto) ?? texto;
        sol.Estado          = "ANALIZADA";
        sol.ModifiedDate    = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Solicitud), new { caseCode });
    }

    /// <summary>
    /// Saca el JSON de la respuesta. El modelo a veces lo envuelve en una valla
    /// de código o le pone un preámbulo; si no hay nada parseable se devuelve
    /// null y se guarda el texto tal cual, que al menos es revisable.
    /// </summary>
    private static string? ExtraerJson(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Trim();

        var valla = t.IndexOf("```", StringComparison.Ordinal);
        if (valla >= 0)
        {
            var ini = t.IndexOf('\n', valla);
            var fin = t.IndexOf("```", valla + 3, StringComparison.Ordinal);
            if (ini > 0 && fin > ini) t = t[(ini + 1)..fin].Trim();
        }

        var a = t.IndexOf('{');
        var b = t.LastIndexOf('}');
        if (a < 0 || b <= a) return null;

        var candidato = t[a..(b + 1)];
        try { using var _ = JsonDocument.Parse(candidato); return candidato; }
        catch (JsonException) { return null; }
    }

    // ── Interno ──────────────────────────────────────────────────────────

    private async Task<ProcessCase> AbrirCasoAsync()
    {
        var caso = new ProcessCase
        {
            CaseCode       = Guid.NewGuid(),
            DefinitionCode = ProcesoPortal,
            StartDate      = DateTime.UtcNow,
            State          = "Started"
        };
        _db.ProcessCase.Add(caso);
        await _db.SaveChangesAsync();
        return caso;
    }

    /// <summary>
    /// Cierra un caso que no llegó a usarse. Se marca en vez de borrarse: las
    /// ejecuciones y las invocaciones ya colgadas de él son la prueba de qué se
    /// consultó, y borrarlas dejaría el intento sin rastro.
    /// </summary>
    private async Task DescartarCasoAsync(Guid caseCode)
    {
        var caso = await _db.ProcessCase.FirstOrDefaultAsync(x => x.CaseCode == caseCode);
        if (caso == null) return;
        caso.State   = "Descartado";
        caso.EndDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private static ContratoAfiliado? LeerContrato(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var e = doc.RootElement;
            string? T(string p) => e.TryGetProperty(p, out var v)
                ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())
                : null;
            int? N(string p) => int.TryParse(T(p), out var n) ? n : null;

            var tit = e.TryGetProperty("Titular", out var t) && t.ValueKind == JsonValueKind.Object ? t : default;
            string? TT(string p) => tit.ValueKind == JsonValueKind.Object && tit.TryGetProperty(p, out var v)
                ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())
                : null;

            return new ContratoAfiliado
            {
                Numero          = T("Numero"),
                Region          = T("Region"),
                Producto        = T("Producto"),
                CodigoPlan      = T("CodigoPlan"),
                NombrePlan      = T("NombrePlan"),
                NombreComercial = T("NombreComercialPlanApp") ?? T("NombreComercialPlan"),
                Estado          = T("Estado"),
                Nivel           = N("Nivel"),
                CoberturaMaxima = decimal.TryParse(T("CoberturaMaxima"), out var cm) ? cm : null,
                DeducibleTotal  = decimal.TryParse(T("DeducibleTotal"), out var dt) ? dt : null,
                TitularNombre   = $"{TT("Nombres")} {TT("Apellidos")}".Trim(),
                TitularNumero   = int.TryParse(TT("Numero"), out var tn) ? tn : null,
                Beneficiarios   = LeerBeneficiariosDelContrato(e),
                Crudo           = json
            };
        }
        catch (JsonException) { return null; }
    }


    /// <summary>
    /// Convierte el JSON del agente en el modelo que pinta la vista. Es
    /// deliberadamente tolerante: si el modelo se deja un campo, se pierde ese
    /// campo y no la pantalla entera.
    /// </summary>
    private static ExplicacionClienteVm? LeerExplicacion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;

            string? T(JsonElement e, string p) =>
                e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v)
                    ? (v.ValueKind == JsonValueKind.String ? v.GetString()
                       : v.ValueKind == JsonValueKind.Number ? v.ToString() : null)
                    : null;

            decimal? D(JsonElement e, string p) =>
                e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v)
                && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;

            int? I(JsonElement e, string p) =>
                e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v)
                && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

            var vm = new ExplicacionClienteVm
            {
                Saludo                = T(r, "saludo"),
                ResumenUnaLinea       = T(r, "resumenUnaLinea"),
                TotalPresentado       = D(r, "totalPresentado"),
                TotalEstimadoCubierto = D(r, "totalEstimadoCubierto"),
                SiguientePaso         = T(r, "siguientePaso")
            };

            if (r.TryGetProperty("confirmar", out var cf) && cf.ValueKind == JsonValueKind.Array)
                foreach (var x in cf.EnumerateArray())
                    vm.Confirmar.Add(new ConfirmarDatoVm
                    {
                        Campo = T(x, "campo"), Valor = T(x, "valor"),
                        DocId = I(x, "docId"), TextoEvidencia = T(x, "textoEvidencia")
                    });

            void Lineas(string prop, List<LineaCoberturaVm> destino)
            {
                if (!r.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
                foreach (var x in arr.EnumerateArray())
                    destino.Add(new LineaCoberturaVm
                    {
                        Concepto = T(x, "concepto"), Valor = D(x, "valor"),
                        Porcentaje = D(x, "porcentaje"), PorQue = T(x, "porQue"),
                        Termino = T(x, "termino"), QueHacer = T(x, "queHacer"),
                        DocId = I(x, "docId"), TextoEvidencia = T(x, "textoEvidencia")
                    });
            }
            Lineas("cubierto", vm.Cubierto);
            Lineas("noCubierto", vm.NoCubierto);

            if (r.TryGetProperty("faltantes", out var fl) && fl.ValueKind == JsonValueKind.Array)
                foreach (var x in fl.EnumerateArray())
                    vm.Faltantes.Add(new FaltanteVm
                    {
                        Documento = T(x, "documento"), PorQue = T(x, "porQue"),
                        ComoConseguirlo = T(x, "comoConseguirlo"),
                        Bloquea = x.ValueKind == JsonValueKind.Object
                                  && x.TryGetProperty("bloquea", out var b)
                                  && b.ValueKind == JsonValueKind.True
                    });

            if (r.TryGetProperty("avisos", out var av) && av.ValueKind == JsonValueKind.Array)
                foreach (var x in av.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.String)
                        vm.Avisos.Add(x.GetString() ?? string.Empty);

            return vm;
        }
        catch (JsonException)
        {
            // Si el agente no devolvió JSON válido, la pantalla sigue funcionando
            // con la parte determinista: documentos leídos, totales y faltantes.
            return null;
        }
    }


    /// <summary>
    /// Traduce la nota ResolucionReembolso al modelo del afiliado. NO recalcula
    /// nada: cada cifra sale tal cual de la nota que produjo el pipeline, que es
    /// la misma que ve el auditor. Los faltantes y las alertas se toman de la
    /// auditoría médica, que es quien los detecta.
    /// </summary>
    /// <summary>
    /// Los emparejamientos factura → respaldo que dejó el expediente.
    ///
    /// Devuelve un diccionario respaldo → (factura, por qué). Se lee con
    /// tolerancia: un expediente ilegible no puede tumbar la pantalla del
    /// afiliado, simplemente se queda sin árbol y las tarjetas salen sueltas
    /// como antes.
    /// </summary>
    private static Dictionary<int, (int Factura, string? PorQue)> LeerVinculos(string? expedienteJson)
    {
        var mapa = new Dictionary<int, (int, string?)>();
        if (string.IsNullOrWhiteSpace(expedienteJson)) return mapa;

        try
        {
            using var doc = JsonDocument.Parse(expedienteJson);
            if (!doc.RootElement.TryGetProperty("vinculos", out var vs)
                || vs.ValueKind != JsonValueKind.Array) return mapa;

            foreach (var v in vs.EnumerateArray())
            {
                if (v.ValueKind != JsonValueKind.Object) continue;
                if (!v.TryGetProperty("dePrincipal", out var pr) || !pr.TryGetInt32(out var factura)) continue;
                if (!v.TryGetProperty("aJustificante", out var ju) || !ju.TryGetInt32(out var respaldo)) continue;

                var detalle = v.TryGetProperty("detalle", out var dt) && dt.ValueKind == JsonValueKind.String
                    ? dt.GetString() : null;

                // Un respaldo cuelga de UNA factura: si el modelo lo repitiera,
                // manda el primero y no se duplica la tarjeta en el árbol.
                if (!mapa.ContainsKey(respaldo)) mapa[respaldo] = (factura, detalle);
            }
        }
        catch (JsonException) { /* sin árbol, pero la pantalla vive */ }

        return mapa;
    }

    private static ResolucionClienteVm? LeerResolucion(string? resolucionJson, string? auditoriaJson)
    {
        if (string.IsNullOrWhiteSpace(resolucionJson)) return null;

        ResolucionClienteVm vm;
        try
        {
            using var doc = JsonDocument.Parse(resolucionJson);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;

            decimal D(JsonElement e, string p) =>
                e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v)
                && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : 0m;

            // Igual que D pero distinguiendo "no vino" de "vino en cero". Para
            // el valor a pagar la diferencia importa: cero es un resultado
            // legítimo —el deducible se lo comió entero— y no puede tratarse
            // como campo ausente.
            decimal? DN(JsonElement e, string p) =>
                e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v)
                && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : (decimal?)null;

            string? T(JsonElement e, string p) =>
                e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v)
                    ? (v.ValueKind == JsonValueKind.String ? v.GetString()
                       : v.ValueKind == JsonValueKind.Number ? v.ToString() : null)
                    : null;

            vm = new ResolucionClienteVm
            {
                EstadoPropuesto = T(r, "estadoPropuesto"),
                Confianza = r.TryGetProperty("confianza", out var cf)
                            && cf.ValueKind == JsonValueKind.Number
                            && cf.TryGetDecimal(out var cd) ? cd : null
            };

            if (r.TryGetProperty("totales", out var tot) && tot.ValueKind == JsonValueKind.Object)
            {
                vm.TotalPresentado    = D(tot, "presentado");
                vm.TotalCubierto      = D(tot, "cubierto");
                vm.TotalCopago        = D(tot, "copago");
                vm.TotalDeducible     = D(tot, "deducible");
                vm.TotalNoCubierto    = D(tot, "noCubierto");
                vm.TotalEstimadoPagar = DN(tot, "estimadoPagar");
                vm.TotalPendiente     = D(tot, "pendiente");
            }

            if (r.TryGetProperty("items", out var its) && its.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in its.EnumerateArray())
                {
                    var item = new ItemResolucionVm
                    {
                        Descripcion     = T(it, "descripcion"),
                        Procedimiento   = T(it, "procedimiento"),
                        ValorPresentado = D(it, "valorPresentado"),
                        ValorCubierto   = D(it, "valorCubierto"),
                        ValorCopago     = D(it, "valorCopago"),
                        ValorDeducible  = D(it, "valorDeducible"),
                        ValorNoCubierto = D(it, "valorNoCubierto"),
                        // "cubierto" es una cadena de tres estados
                        // (TOTAL / PARCIAL / NO_CUBIERTO), no un booleano.
                        Estado          = T(it, "cubierto") ?? "NO_CUBIERTO",
                        Motivo          = T(it, "motivo"),
                        ReglaAplicada   = T(it, "reglaAplicada")
                    };
                    if (it.TryGetProperty("evidencia", out var ev) && ev.ValueKind == JsonValueKind.Array)
                        foreach (var x in ev.EnumerateArray())
                            if (x.ValueKind == JsonValueKind.String)
                                item.Evidencia.Add(x.GetString() ?? string.Empty);
                    vm.Items.Add(item);
                }
            }

            if (r.TryGetProperty("reglasEvaluadas", out var rgs) && rgs.ValueKind == JsonValueKind.Array)
                foreach (var g in rgs.EnumerateArray())
                    vm.Reglas.Add(new ReglaClienteVm
                    {
                        Familia   = T(g, "familia"),
                        Regla     = T(g, "regla"),
                        Resultado = T(g, "resultado") ?? string.Empty,
                        Detalle   = T(g, "detalle")
                    });

            if (r.TryGetProperty("observaciones", out var obs) && obs.ValueKind == JsonValueKind.Array)
                foreach (var x in obs.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.String)
                        vm.Observaciones.Add(x.GetString() ?? string.Empty);
        }
        catch (JsonException)
        {
            // Una resolución ilegible no debe tumbar la pantalla: el afiliado
            // sigue viendo sus documentos y el estado del proceso.
            return null;
        }

        // Faltantes y alertas: los detecta la auditoría médica, no la resolución.
        if (!string.IsNullOrWhiteSpace(auditoriaJson))
        {
            try
            {
                using var da = JsonDocument.Parse(auditoriaJson);
                var a = da.RootElement;
                if (a.ValueKind == JsonValueKind.Object)
                {
                    if (a.TryGetProperty("faltantes", out var fa) && fa.ValueKind == JsonValueKind.Array)
                        foreach (var x in fa.EnumerateArray())
                            if (x.ValueKind == JsonValueKind.String)
                                vm.Faltantes.Add(x.GetString() ?? string.Empty);

                    if (a.TryGetProperty("alertas", out var al) && al.ValueKind == JsonValueKind.Array)
                        foreach (var x in al.EnumerateArray())
                        {
                            if (x.ValueKind != JsonValueKind.Object) continue;
                            // Solo las que el afiliado puede accionar o debe conocer;
                            // el detalle clínico fino es cosa del auditor.
                            var titulo = x.TryGetProperty("titulo", out var tt) ? tt.GetString() : null;
                            var det    = x.TryGetProperty("detalle", out var dd) ? dd.GetString() : null;
                            var acc    = x.TryGetProperty("accionSugerida", out var aa) ? aa.GetString() : null;
                            var texto  = string.Join(" ", new[] { titulo, det, acc }
                                            .Where(y => !string.IsNullOrWhiteSpace(y)));
                            if (texto.Length > 0) vm.Alertas.Add(texto);
                        }
                }
            }
            catch (JsonException) { /* la auditoría es complemento, no bloquea */ }
        }

        return vm;
    }


    /// <summary>
    /// Relee los beneficiarios del JSON guardado del contrato. Es el mismo
    /// contenido que parseó el servicio al consultarlo; aquí solo se recupera.
    /// </summary>
    private static List<BeneficiarioAfiliado> LeerBeneficiariosDelContrato(JsonElement contrato)
    {
        var lista = new List<BeneficiarioAfiliado>();
        if (contrato.ValueKind != JsonValueKind.Object) return lista;
        if (!contrato.TryGetProperty("Beneficiarios", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return lista;

        foreach (var b in arr.EnumerateArray())
        {
            if (b.ValueKind != JsonValueKind.Object) continue;
            string? S(string prop) => b.TryGetProperty(prop, out var v)
                ? (v.ValueKind == JsonValueKind.String ? v.GetString()
                   : v.ValueKind == JsonValueKind.Number ? v.ToString() : null)
                : null;
            int? I(string prop) => int.TryParse(S(prop), out var n) ? n : null;
            bool? B(string prop) => b.TryGetProperty(prop, out var v)
                ? v.ValueKind switch
                  {
                      JsonValueKind.True => true,
                      JsonValueKind.False => false,
                      _ => (bool?)null
                  }
                : null;

            var preex = b.TryGetProperty("Preexistencias", out var px)
                        && px.ValueKind == JsonValueKind.Array ? px.GetArrayLength() : 0;

            lista.Add(new BeneficiarioAfiliado
            {
                NumeroPersona     = I("NumeroPersona"),
                Nombres           = S("Nombres"),
                Apellidos         = S("Apellidos"),
                Documento         = S("NumeroDocumento"),
                Genero            = S("Genero"),
                Edad              = I("Edad"),
                Relacion          = S("RelacionDependiente"),
                DeducibleCubierto = decimal.TryParse(S("DeducibleCubierto"), out var dc) ? dc : null,
                EnCarencia        = B("EnCarencia"),
                DiasFinCarencia   = I("DiasFinCarencia"),
                EnCarenciaHosp    = B("EnCarenciaHospitalaria"),
                Preexistencias    = preex,
                Maternidad        = B("Maternidad")
            });
        }

        // Misma garantía que el servicio, en un solo sitio: la API no siempre
        // devuelve al titular dentro de Beneficiarios.
        return PortalClienteService.AsegurarTitular(lista, contrato);
    }

    private async Task<ClienteSolicitudVm?> ArmarSolicitudAsync(Guid caseCode)
    {
        var sol = await _db.SolicitudCliente.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CaseCode == caseCode);
        if (sol == null) return null;

        var docs = await _db.DataFile.AsNoTracking()
            .Where(d => d.CaseCode == caseCode)
            .OrderBy(d => d.Id)
            .Select(d => new
            {
                d.Id, d.OriginalName, d.CreatedDate,
                // Sin texto no hay nada que juzgar: es una foto ilegible, y eso
                // hay que decirlo en vez de dejar la tarjeta en blanco.
                TieneTexto = d.Text != null && d.Text.Length > 30,
                Paginas = _db.DataFilePage.Count(pg => pg.DataFileId == d.Id)
            })
            .ToListAsync();

        // Lo que la tipificación ya dedujo de esos documentos. Puede no existir
        // todavía: el afiliado acaba de subirlos.
        var clas = await _db.DocumentoClasificacion.AsNoTracking()
            .Where(c => c.DataFileNavigation.CaseCode == caseCode && c.IsCurrent)
            .Select(c => new
            {
                c.DataFileId, c.CreatedDate, c.TipoArchivo, c.TipoSoporte, c.EsFacturaValida,
                c.ValorTotal, c.EmisorNombre, c.NumeroFactura, c.FechaEmision,
                c.JustificacionTipo,
                // El tipo de prestador es lo que permite decirle al afiliado
                // "es una factura de farmacia" en vez de "es una factura".
                c.EmisorTipo, c.EmisorRuc,
                // La frase del clasificador: es lo único que le dice al afiliado
                // qué subió cuando el tipo no se reconoció.
                c.ResumenSoporte, c.MedicoNombre,
                // De quién es el documento (REQ-020j)
                c.PacienteNumeroPersona, c.PacienteCoincide, c.PacienteJustificacion,
                // Los datos fiscales que hacen verificable la factura
                c.ClaveAcceso, c.NumeroAutorizacion
            })
            .ToListAsync();

        var vm = new ClienteSolicitudVm
        {
            CaseCode         = caseCode,
            Cedula           = sol.Cedula,
            NumeroContrato   = sol.NumeroContrato,
            NombrePlan       = sol.NombrePlan,
            NombreTitular    = sol.NombreTitular,
            Estado           = sol.Estado,
            ValorPresentado  = sol.ValorPresentado,
            DatosConfirmados = sol.DatosConfirmados,
            Contrato         = LeerContrato(sol.ContratoJson),

            // A quién va dirigido: gobierna el deducible y la carencia que se
            // aplican, así que se enseña siempre y se puede cambiar.
            NombreBeneficiario   = sol.NombreBeneficiario,
            RelacionBeneficiario = sol.RelacionBeneficiario,
            CedulaBeneficiario   = sol.CedulaBeneficiario,
            EdadBeneficiario     = sol.EdadBeneficiario,
            DeducibleCubierto    = sol.DeducibleCubierto,
            EnCarencia           = sol.EnCarencia,
            DiasFinCarencia      = sol.DiasFinCarencia,
            TienePreexistencias  = sol.TienePreexistencias,
            TotalBeneficiarios   = (LeerBeneficiariosGuardados(sol.BeneficiariosJson)
                                    ?? new List<BeneficiarioAfiliado>()).Count,

            ExplicacionJson  = sol.ExplicacionJson,
            Explicacion      = LeerExplicacion(sol.ExplicacionJson)
        };

        // ── El proceso completo, no solo la subida ────────────────────────
        // El afiliado corre los mismos cuatro pasos que el auditor. Se leen
        // las notas que deja cada uno para saber cuáles están hechos: es el
        // mismo criterio que usa la bandeja, así que las dos pantallas no
        // pueden discrepar sobre en qué punto va el caso.
        var notas = await _db.Note.AsNoTracking()
            .Where(n => n.CaseCode == caseCode)
            .Select(n => new { n.Title, n.Detail, n.CreatedAt })
            .ToListAsync();
        // Un paso está hecho cuando PRODUJO algo, no cuando dejó una nota.
        // El defecto medido: la resolución guardaba {"_raw":true} —13 caracteres,
        // el marcador de «no salió nada, va a control humano»— y como la nota
        // existía, el paso contaba como hecho. Resultado: nada pendiente, el
        // botón recorría una lista vacía y recargaba.
        // Y además de haber producido algo, tiene que haberlo producido DESPUÉS
        // del último documento. Si el afiliado adjunta la factura que le
        // faltaba, el expediente, la revisión médica y la resolución anteriores
        // hablan de un conjunto de papeles que ya no es el suyo: darlos por
        // buenos le devolvería el mismo resultado de antes, con la factura
        // nueva ignorada y ninguna señal de que no se tuvo en cuenta.
        //
        // Ambas fechas son UTC (comprobado: el servidor SQL corre en UTC y las
        // dos columnas se escriben con DateTime.UtcNow), así que se comparan
        // directamente.
        // La frontera de frescura no son sólo los documentos: son también las
        // clasificaciones. La entrada del expediente no es el PDF, es lo que se
        // dedujo de él. Medido en 7b99e00e: el expediente se construyó con dos
        // documentos aún sin tipificar y su nota era posterior al último
        // documento, así que por fecha pasaba por buena — y al terminar la
        // tipificación seguía en verde, describiendo un caso que ya no era.
        DateTime? ultimoDocumento = docs.Count > 0 ? docs.Max(d => d.CreatedDate) : null;
        DateTime? ultimaLectura   = clas.Count > 0 ? clas.Max(c => c.CreatedDate) : null;

        DateTime? ultimaEntrada =
            ultimoDocumento is null ? ultimaLectura
            : ultimaLectura is null ? ultimoDocumento
            : (ultimoDocumento > ultimaLectura ? ultimoDocumento : ultimaLectura);

        bool Produjo(string titulo) => notas.Any(n =>
            string.Equals(n.Title, titulo, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(n.Detail)
            && n.Detail!.Length > 40
            && EstadoDelProceso.NotaVigente(n.CreatedAt, ultimaEntrada));

        vm.Pasos = new List<PasoClienteVm>
        {
            new() { Numero = 1, Titulo = "Sus documentos", Icono = "fa-file-text-o",
                    Descripcion = "Leemos cada documento que adjuntó",
                    Hecho = docs.Count > 0 },
            // Hecho cuando lo está para TODOS los documentos, no para alguno.
            //
            // Éste es EL defecto por el que la pantalla se quedaba «Identificando
            // el documento…» para siempre, y sale sólo si se sube en dos tandas
            // —que es justo lo que invita a hacer el enlace «Adjuntar otro
            // documento»—. Medido en el caso 7b99e00e: se sube un PDF, se
            // clasifica; se suben dos más, y como ya había UNA clasificación el
            // paso contaba como hecho, así que el arranque automático se saltaba
            // la tipificación y corría el paso siguiente. Los dos documentos
            // nuevos se quedaban SIN CLASIFICAR, sin error y sin reintento: sus
            // tarjetas decían «Identificando el documento…» eternamente.
            //
            // Es el mismo error que la resolución que contaba como hecha porque
            // la nota existía: presencia no es completitud. Con un conjunto que
            // crece, la pregunta nunca es «¿hay alguno?» sino «¿faltan?».
            new() { Numero = 2, Titulo = "Qué nos trajo", Icono = "fa-tags",
                    Descripcion = "Identificamos si es factura, receta, informe…",
                    Hecho = EstadoDelProceso.TodosTipificados(
                                docs.Select(d => d.Id),
                                clas.Select(c => c.DataFileId)),
                    Url = Url.Action("Generar", "Clasificacion", new { area = "Studio" }) },
            new() { Numero = 3, Titulo = "Su expediente", Icono = "fa-sitemap",
                    Descripcion = "Ordenamos qué documento respalda cada gasto",
                    Hecho = Produjo("ExpedienteDocumental"),
                    Url = Url.Action("Generar", "Expediente", new { area = "Studio" }) },
            new() { Numero = 4, Titulo = "Revisión médica", Icono = "fa-user-md",
                    Descripcion = "Comprobamos que lo facturado corresponde a su atención",
                    Hecho = Produjo("AuditoriaMedicina"),
                    Url = Url.Action("Generar", "Auditoria", new { area = "Studio" }) },
            new() { Numero = 5, Titulo = "Qué le cubrimos", Icono = "fa-gavel",
                    Descripcion = "Aplicamos su plan regla por regla y calculamos el reembolso",
                    Hecho = Produjo("ResolucionReembolso"),
                    Url = Url.Action("Generar", "Resolucion", new { area = "Studio" }) },
        };

        // Un paso no puede estar hecho si el anterior no lo está.
        //
        // Es el invariante de una cadena, y sin él se cuela el caso peor: el
        // expediente de 7b99e00e se construyó cuando dos de los tres documentos
        // estaban SIN CLASIFICAR, y su nota es posterior al último documento, de
        // modo que por fecha pasaba por buena. Un resultado calculado sobre
        // papeles que aún no se habían leído no es un resultado: es una foto de
        // medio expediente, y encima con aspecto de estar completa.
        //
        // Se propaga hacia adelante: en cuanto un paso queda pendiente, todo lo
        // que viene detrás vuelve a pendiente y se recalcula en orden.
        EstadoDelProceso.PropagarPendientes(vm.Pasos);

        // Qué respalda a qué. El expediente ya lo empareja y lo guarda en
        // `vinculos`; hasta ahora se quedaba en la base sin que el afiliado lo
        // llegara a ver, y su pantalla era una pila de tarjetas sueltas.
        var expediente = notas
            .Where(n => string.Equals(n.Title, "ExpedienteDocumental", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => n.Detail)
            .FirstOrDefault();

        var vinculos = LeerVinculos(expediente);

        // La resolución: la MISMA nota que lee el auditor, traducida.
        var reso = notas.Where(n => string.Equals(n.Title, "ResolucionReembolso", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(n => n.CreatedAt)
                        .Select(n => n.Detail)
                        .FirstOrDefault();
        var audi = notas.Where(n => string.Equals(n.Title, "AuditoriaMedicina", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(n => n.CreatedAt)
                        .Select(n => n.Detail)
                        .FirstOrDefault();
        vm.Resolucion = LeerResolucion(reso, audi);

        // ── El porcentaje sale del PLAN, no del JSON del modelo ──────────────
        //
        // Hasta hoy se derivaba de valorCubierto/valorPresentado, cifras que
        // escribia el modelo y que nadie contrastaba. Desde REQ-027b al modelo se
        // le pide que las deje en null cuando no le constan, asi que sin esto la
        // pantalla enseñaria «0%» y el afiliado leeria «no me cubren nada».
        //
        // Los hechos ya estan estructurados de la clasificacion: procedimiento
        // homologado, beneficio, y si la homologacion quedo ambigua. Con eso y el
        // plan del contrato, la cobertura se CONSULTA en Pr05Beneficios.
        if (_cobertura != null && vm.Resolucion is { Items.Count: > 0 } rvm)
        {
            var procs = await _db.DocumentoProcedimiento.AsNoTracking()
                .Where(x => _db.DataFile.Any(f => f.Id == x.DataFileId && f.CaseCode == caseCode))
                .Select(x => new { x.Descripcion, x.CodigoBeneficio, x.NumeroProcedimiento,
                                   x.NombreLr05, x.HomologacionAmbigua })
                .ToListAsync();

            if (procs.Count > 0)
            {
                var evaluados = await _cobertura.EvaluarAsync(
                    procs.Select(x => (x.Descripcion ?? string.Empty, 0m, x.CodigoBeneficio,
                                       (int?)x.NumeroProcedimiento, x.NombreLr05,
                                       x.HomologacionAmbigua == true)),
                    sol.CodigoPlan, VersionDelPlan(sol.ContratoJson), sol.CodigoProducto,
                    HttpContext.RequestAborted);

                // Se emparejan por la descripcion normalizada: las dos vienen del
                // mismo OCR, asi que coinciden salvo mayusculas, tildes y espacios.
                // Lo que no empareja se queda sin porcentaje, que es mejor que
                // ponerle el de otra linea.
                var porTexto = evaluados
                    .GroupBy(e => Normaliza(e.Descripcion))
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var it in rvm.Items)
                {
                    if (!porTexto.TryGetValue(Normaliza(it.Descripcion), out var ev)) continue;
                    it.PorcentajeDelPlan  = ev.PorcentajeDelPlan;
                    it.ExplicacionDelPlan = ev.ParaElCliente;
                }
            }
        }

        // ── Una factura no se paga dos veces, y eso no lo decide el modelo ────
        //
        // El hallazgo del duplicado llegaba al agente como un dato más y era él
        // quien decidía si cambiaba el resultado. Un dato así no se opina.
        //
        // Se lee la respuesta REAL de factura_ya_pagada_bd que quedó guardada en
        // el caso y se juzga en código. Si esa factura ya está en un reclamo sin
        // anular —en este contrato o en cualquier otro, de esta persona o de
        // otra— no vuelve a entrar. Medido sobre 001-100-000000916: 8 líneas en
        // el contrato que se presentaba y 1 en otro.
        //
        // Los anulados NO cuentan: si se anuló, esa presentación dejó de existir.
        // Se pregunta DIRECTAMENTE, no se espera a que el agente haya llamado a
        // su herramienta: en cuanto el documento se identifica ya hay clave de
        // acceso, y el agente todavia no ha corrido. Hacerle recorrer el camino
        // entero para darle un no que se sabia desde el primer papel es hacerle
        // perder el tiempo.
        var facturas = clas
            .Where(c => !string.IsNullOrWhiteSpace(c.NumeroFactura)
                     && !string.IsNullOrWhiteSpace(c.ClaveAcceso))
            .Select(c => ((string?)c.NumeroFactura, (string?)c.ClaveAcceso))
            .Distinct()
            .ToList();

        var repetida = facturas.Count > 0
            ? await _repetidas.BuscarAsync(facturas)
            : null;

        vm.FacturaRepetida = FacturaRepetida.Juzgar(repetida, vm.NumeroContrato);

        if (vm.FacturaRepetida.Bloquea)
        {
            // El log importa: si esto se dispara de más, alguien deja de cobrar
            // lo que le toca, y hay que poder medirlo.
            _log.LogWarning(
                "[Portal] Caso {Caso}: factura ya registrada en {N} reclamo(s) sin anular "
                + "(mismo contrato: {Mismo}). No se permite presentarla de nuevo.",
                caseCode, vm.FacturaRepetida.Hallazgos.Count, vm.FacturaRepetida.HayEnElMismoContrato);
        }

        // Un descuadre entre lo que el modelo dice que va a pagar y sus propias
        // partes no puede pasar en silencio: es dinero, y la pantalla enseña la
        // cifra derivada. Queda en el log para poder medir cada cuánto ocurre.
        if (vm.Resolucion is { CuadraElPagoDelModelo: false } rr)
        {
            _log.LogWarning(
                "[Portal] Caso {Caso}: la resolución no cuadra. estimadoPagar {Modelo} frente a "
                + "cubierto {Cubierto} - deducible {Deducible} - copago {Copago} = {Derivado} "
                + "(desvío {Desvio})",
                sol.CaseCode, rr.TotalEstimadoPagar, rr.TotalCubierto, rr.TotalDeducible,
                rr.TotalCopago, rr.Devolvemos, rr.DesviacionDelModelo);
        }

        // Las hojas: el clasificador ya las tipificó una por una y lo dejó en
        // DocumentoTag con su PageNumber. Se recuperan igual que en la pantalla
        // del auditor, para no tener dos criterios distintos sobre lo mismo.
        // Las personas del contrato: hacen falta para poner NOMBRE al número que
        // devolvió el clasificador. Él nunca transcribe nombres — se limita a
        // decir a qué persona de la lista corresponde.
        var personas = LeerBeneficiariosGuardados(sol.BeneficiariosJson)
                       ?? LeerContrato(sol.ContratoJson)?.Beneficiarios
                       ?? new List<BeneficiarioAfiliado>();

        var idsDocs = docs.Select(d => d.Id).ToList();
        var tags = await _db.DocumentoTag.AsNoTracking()
            .Where(t => idsDocs.Contains(t.DataFileId) && t.PageNumber != null)
            .Select(t => new { t.DataFileId, t.PageNumber, t.Tag, t.Categoria, t.Valor })
            .ToListAsync();

        // Las marcas de todo el documento (con y sin página): son la prueba
        // visible de que se leyó. Se excluyen las de control —el tipo, el
        // soporte y el total ya se muestran arriba con su propio formato.
        // Los rubros de la factura: es lo que el afiliado quiere ver desglosado,
        // y estaban guardados sin que nadie los enseñara.
        var items = await _db.DocumentoItem.AsNoTracking()
            .Where(i => idsDocs.Contains(i.DataFileId))
            .OrderBy(i => i.DataFileId).ThenBy(i => i.Orden)
            .Select(i => new { i.DataFileId, i.Descripcion, i.TipoRubro,
                               i.Cantidad, i.ValorTotal, i.PageNumber })
            .ToListAsync();

        var dxDoc = await _db.DocumentoDiagnostico.AsNoTracking()
            .Where(d => idsDocs.Contains(d.DataFileId))
            .Select(d => new { d.DataFileId, d.Codigo, d.Descripcion })
            .ToListAsync();

        var tagsDoc = await _db.DocumentoTag.AsNoTracking()
            // Se traen TODAS: las marcas técnicas alimentan las verificaciones
            // legibles (VerificacionVm), no se pintan en crudo.
            .Where(t => idsDocs.Contains(t.DataFileId))
            .Select(t => new { t.DataFileId, t.Tag })
            .ToListAsync();

        // Un respaldo no se juzga solo: si la factura ya está en el expediente,
        // pedírsela otra vez manda al afiliado a buscar un papel que ya subió.
        var hayFactura = clas.Any(x => x.EsFacturaValida == true);

        foreach (var d in docs)
        {
            var c = clas.FirstOrDefault(x => x.DataFileId == d.Id);
            vm.Documentos.Add(new ClienteDocumentoVm
            {
                HayFacturaEnElCaso = hayFactura,
                DocId       = d.Id,
                Nombre      = d.OriginalName,
                Subido      = d.CreatedDate,
                Leido       = c != null,
                Tipo        = c?.TipoArchivo,
                TipoSoporte = c?.TipoSoporte,
                EsFactura   = c?.EsFacturaValida ?? false,
                Valor       = c?.ValorTotal,
                Emisor      = c?.EmisorNombre,
                NumeroDoc   = c?.NumeroFactura,
                Fecha       = c?.FechaEmision,
                PorQue      = c?.JustificacionTipo,
                EmisorTipo  = c?.EmisorTipo,
                EmisorRuc   = c?.EmisorRuc,
                ClaveAcceso = c?.ClaveAcceso,
                NumeroAutorizacion = c?.NumeroAutorizacion,
                Resumen     = c?.ResumenSoporte,
                Medico      = c?.MedicoNombre,
                Items       = items.Where(i => i.DataFileId == d.Id)
                                   .Select(i => new ItemClienteVm
                                   {
                                       Descripcion = i.Descripcion,
                                       TipoRubro   = i.TipoRubro,
                                       Cantidad    = i.Cantidad,
                                       Valor       = i.ValorTotal,
                                       Pagina      = i.PageNumber
                                   }).ToList(),
                Diagnosticos = dxDoc.Where(x => x.DataFileId == d.Id)
                                    .Select(x => new DxClienteVm
                                    {
                                        Codigo = x.Codigo, Descripcion = x.Descripcion
                                    }).ToList(),
                Tags        = tagsDoc.Where(t => t.DataFileId == d.Id)
                                     .Select(t => t.Tag)
                                     .Distinct(StringComparer.OrdinalIgnoreCase)
                                     .ToList(),
                PacienteCoincide      = c?.PacienteCoincide,
                PacienteNumeroPersona = c?.PacienteNumeroPersona,
                PacienteJustificacion = c?.PacienteJustificacion,
                // Contra QUIÉN se compara: el beneficiario elegido ahora, no el
                // que estaba elegido cuando se clasificó. Sin esto el aviso
                // sobrevive al cambio y ofrece cambiar a la persona ya elegida.
                BeneficiarioElegido   = sol.NumeroPersona,
                // A qué factura respalda, según el expediente.
                RespaldaADocId = vinculos.TryGetValue(d.Id, out var vnc) ? vnc.Factura : null,
                PorQueRespalda = vinculos.TryGetValue(d.Id, out var vnc2) ? vnc2.PorQue : null,
                // El nombre sale de la lista de personas del contrato, no del
                // documento: el clasificador nunca lo transcribe.
                PacienteNombre = c?.PacienteNumeroPersona is int np
                    ? personas.FirstOrDefault(x => x.NumeroPersona == np)?.NombreCompleto
                    : null,
                TieneTexto  = d.TieneTexto,
                Paginas     = d.Paginas,
                Hojas       = tags
                    .Where(t => t.DataFileId == d.Id)
                    .GroupBy(t => t.PageNumber!.Value)
                    .OrderBy(g => g.Key)
                    .Select(g => new HojaClienteVm
                    {
                        Pagina      = g.Key,
                        Tipo        = g.FirstOrDefault(t => t.Categoria == "TIPO")?.Tag,
                        TipoSoporte = g.FirstOrDefault(t => t.Categoria == "SOPORTE")?.Tag,
                        EsFactura   = g.Any(t => t.Tag == "FACTURA_VALIDA"),
                        Valor       = decimal.TryParse(
                            g.FirstOrDefault(t => t.Tag == "TOTAL_DETECTADO")?.Valor, out var vh) ? vh : null
                    })
                    .ToList()
            });
        }

        return vm;
    }

    /// <summary>
    /// La version del plan, que vive dentro del contrato guardado. Sin ella no se
    /// puede consultar la cobertura: el mismo plan cambia de porcentajes entre
    /// versiones.
    /// </summary>
    private static int? VersionDelPlan(string? contratoJson)
    {
        if (string.IsNullOrWhiteSpace(contratoJson)) return null;
        try
        {
            var raiz = JsonDocument.Parse(contratoJson).RootElement;
            if (raiz.TryGetProperty("Version", out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetInt32();
        }
        catch { /* contrato ilegible: sin version, y la cobertura lo dira */ }
        return null;
    }

    /// <summary>
    /// Para emparejar descripciones que salieron del mismo OCR por caminos
    /// distintos: fuera mayusculas, tildes, signos y espacios de mas.
    /// </summary>
    private static string Normaliza(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        var sb = new StringBuilder();
        foreach (var c in texto.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
            else if (char.IsWhiteSpace(c) && sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
        }
        return sb.ToString().Trim();
    }
}
