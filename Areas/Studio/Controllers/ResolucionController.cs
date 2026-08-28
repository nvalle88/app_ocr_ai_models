using System.Text;
using System.Text.Json;
using app_ocr_ai_models.Areas.Studio.Models;
using app_ocr_ai_models.Data;
using app_tramites.Models.ModelAi;
using app_tramites.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace app_ocr_ai_models.Areas.Studio.Controllers;

// ============================================================
// REQ-019 — Resolución de reembolso (pre-liquidación PROPUESTA por IA).
//   El agente (con la skill SKILL_RESOLUCION_REEMBOLSO) evalúa el sobre
//   contra las reglas y devuelve un JSON PropuestaPreLiquidacionReembolso.
//   Aquí se parsea, se mapea a la vista y se arma la carta (borrador).
//
//   SALVAGUARDAS (ver docs): la IA solo PROPONE.
//     - No escribe estados de liquidación (33/11) ni ejecuta liquidación.
//     - "Enviar carta" no envía en el click: registra la carta tras
//       confirmación; el envío real a un canal es un paso aparte.
// ============================================================

/// <summary>Controller del Área Studio para la resolución de reembolso asistida por IA.</summary>
[Area("Studio")]
[Authorize]
public sealed class ResolucionController : Controller
{
    private const string NoteResolucion = "ResolucionReembolso";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IProcessOrchestrator _orchestrator;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly OCRDbContext _db;
    private readonly ILogger<ResolucionController> _logger;

    public ResolucionController(
        IProcessOrchestrator orchestrator,
        UserManager<IdentityUser> userManager,
        OCRDbContext db,
        ILogger<ResolucionController> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _userManager  = userManager  ?? throw new ArgumentNullException(nameof(userManager));
        _db           = db           ?? throw new ArgumentNullException(nameof(db));
        _logger       = logger       ?? throw new ArgumentNullException(nameof(logger));
    }

    // GET /Studio/Resolucion?caseCode=<guid>
    // Si ya hay una resolución guardada, la muestra; si no, ofrece generarla.
    [HttpGet]
    public async Task<IActionResult> Index(Guid caseCode, bool embed = false)
    {
        ViewData["Embed"] = embed;
        if (caseCode == Guid.Empty)
            return View(new ResolucionViewModel { Error = "Falta el CaseCode." });

        var notaJson = await _db.Note
            .Where(n => n.CaseCode == caseCode && n.Title == NoteResolucion)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => n.Detail)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(notaJson))
            return View(new ResolucionViewModel { CaseCode = caseCode, Generada = false });

        return View(BuildViewModel(caseCode, notaJson, tools: await CargarToolInvocacionesAsync(caseCode)));
    }

    // POST /Studio/Resolucion/Generar
    // Corre el motor IA (con la skill de reglas) y persiste el JSON de la propuesta.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generar(Guid caseCode, string? processCodeOverride)
    {
        if (caseCode == Guid.Empty)
            return View("Index", new ResolucionViewModel { Error = "Falta el CaseCode." });

        var user = await _userManager.GetUserAsync(User);

        OrchestrationResult resultado;
        try
        {
            resultado = await _orchestrator.RunAsync(
                caseCode, user,
                string.IsNullOrWhiteSpace(processCodeOverride) ? null : processCodeOverride.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error generando la resolución del caso {CaseCode}.", caseCode);
            return View("Index", new ResolucionViewModel
            {
                CaseCode = caseCode, Generada = false,
                Error = $"Error al generar la resolución: {ex.Message}"
            });
        }

        // Texto del agente = resultado final (o unión de pasos como respaldo)
        var texto = !string.IsNullOrWhiteSpace(resultado.FinalText)
            ? resultado.FinalText
            : string.Join("\n\n", resultado.Steps.Select(s => s.ResponseText));

        var json = ExtractJson(texto);

        // Persistir: si hubo JSON, se guarda tal cual; si no, el texto crudo (fallback CH).
        await GuardarResolucionAsync(caseCode,
            string.IsNullOrWhiteSpace(json) ? "{\"_raw\":true}" : json,
            string.IsNullOrWhiteSpace(json) ? texto : null,
            user?.UserName);

        var vm = string.IsNullOrWhiteSpace(json)
            ? new ResolucionViewModel
              {
                  CaseCode = caseCode, Generada = true, Estado = ResolucionEstado.Ch,
                  RawText = texto, GeneradoPor = User?.Identity?.Name ?? "—",
                  Error = "El motor no devolvió una resolución estructurada; se deriva a control humano."
              }
            : BuildViewModel(caseCode, json, texto);

        return View("Index", vm);
    }

    // GET /Studio/Resolucion/Informe?caseCode=<guid>  → PDF imprimible
    [HttpGet]
    public async Task<IActionResult> Informe(Guid caseCode)
    {
        var notaJson = await _db.Note
            .Where(n => n.CaseCode == caseCode && n.Title == NoteResolucion)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => n.Detail)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(notaJson))
            return NotFound("Genera primero la resolución del caso.");

        return View(BuildViewModel(caseCode, notaJson, tools: await CargarToolInvocacionesAsync(caseCode)));
    }

    /// <summary>
    /// Audit trail REAL: herramientas ejecutadas para el caso, leídas de la tabla
    /// ToolInvocation (persistida por el executor en cada llamada) vía StepExecution.
    /// </summary>
    private async Task<IReadOnlyList<ToolInvocacionVM>> CargarToolInvocacionesAsync(Guid caseCode)
    {
        try
        {
            return await _db.ToolInvocation.AsNoTracking()
                .Where(ti => ti.Execution.CaseCode == caseCode)
                .OrderByDescending(ti => ti.StartDate)
                .Take(40)
                .Select(ti => new ToolInvocacionVM
                {
                    ToolCode    = ti.ToolCode,
                    StartDate   = ti.StartDate,
                    DuracionSeg = ti.EndDate != null
                        ? EF.Functions.DateDiffMillisecond(ti.StartDate, ti.EndDate.Value) / 1000.0
                        : null,
                    IsError      = ti.IsError,
                    InputResumen = ti.RequestJson
                })
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudieron cargar las ToolInvocation del caso {CaseCode}.", caseCode);
            return Array.Empty<ToolInvocacionVM>();
        }
    }

    // POST /Studio/Resolucion/EnviarCarta
    // NO envía en el click: solo se alcanza tras confirmación del usuario.
    // Registra la carta como Nota (borrador para el canal); no liquida ni escribe estados.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EnviarCarta(Guid caseCode, string carta, string? correo)
    {
        if (caseCode == Guid.Empty || string.IsNullOrWhiteSpace(carta))
            return Ok(new { ok = false, error = "Datos incompletos." });

        _db.Note.Add(new Note
        {
            CaseCode  = caseCode,
            Title     = "CartaClienteRegistrada",
            Detail    = (string.IsNullOrWhiteSpace(correo) ? "" : $"Para: {correo}\n\n") + carta,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = $"resolucion|{User?.Identity?.Name}"
        });
        await _db.SaveChangesAsync();

        // No se despacha correo aquí (integración de canal = paso aparte, con su propia autorización).
        return Ok(new { ok = true, message = "Carta registrada. El envío al canal es un paso posterior con su propia autorización." });
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task GuardarResolucionAsync(Guid caseCode, string json, string? raw, string? user)
    {
        // Reemplaza la resolución previa del caso (una vigente por caso).
        var previas = await _db.Note
            .Where(n => n.CaseCode == caseCode && n.Title == NoteResolucion)
            .ToListAsync();
        if (previas.Count > 0) _db.Note.RemoveRange(previas);

        _db.Note.Add(new Note
        {
            CaseCode  = caseCode,
            Title     = NoteResolucion,
            Detail    = raw is null ? json : json,   // el JSON manda; raw solo si no hubo JSON
            CreatedAt = DateTime.UtcNow,
            CreatedBy = $"resolucion-ia|{user}"
        });
        await _db.SaveChangesAsync();
    }

    private ResolucionViewModel BuildViewModel(Guid caseCode, string json, string? rawFallback = null,
        IReadOnlyList<ToolInvocacionVM>? tools = null)
    {
        PropuestaReembolsoDto? dto = null;
        try { dto = JsonSerializer.Deserialize<PropuestaReembolsoDto>(json, JsonOpts); }
        catch (JsonException ex) { _logger.LogWarning(ex, "JSON de resolución inválido para {CaseCode}.", caseCode); }

        if (dto is null || dto.Items.Count == 0)
        {
            return new ResolucionViewModel
            {
                CaseCode = caseCode, Generada = true, Estado = ResolucionEstado.Ch,
                RawText = rawFallback, GeneradoPor = User?.Identity?.Name ?? "—",
                Error = "No se pudo interpretar la resolución; requiere control humano."
            };
        }

        var estado = MapEstado(dto.EstadoPropuesto, dto.Confianza);

        return new ResolucionViewModel
        {
            CaseCode         = caseCode,
            Generada         = true,
            Estado           = estado,
            Propuesta        = dto,
            CartaTexto       = BuildCarta(estado, dto),
            GeneradoPor      = User?.Identity?.Name ?? "—",
            ToolInvocaciones = tools ?? Array.Empty<ToolInvocacionVM>()
        };
    }

    /// <summary>
    /// Arma la carta (borrador) al cliente según el estado, con los datos del DTO.
    /// Plantillas total / parcial / negativa. Siempre con el rótulo NO OFICIAL.
    /// </summary>
    private static string BuildCarta(ResolucionEstado estado, PropuestaReembolsoDto dto)
    {
        var c = dto.Cabecera ?? new PropuestaReembolsoDto.CabeceraDto();
        var t = dto.Totales ?? new PropuestaReembolsoDto.TotalesDto();
        string M(decimal v) => v.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("es-EC"));
        var titular = string.IsNullOrWhiteSpace(c.Titular) ? "Afiliado(a)" : c.Titular;
        var sobre = c.NumeroSobre ?? "—";
        var prestador = string.IsNullOrWhiteSpace(c.Prestador) ? "el prestador" : c.Prestador;
        var producto = c.Producto ?? "su plan";
        var sb = new StringBuilder();

        sb.AppendLine($"Estimado(a) {titular}:").AppendLine()
          .AppendLine("Reciba un cordial saludo de Saludsa.").AppendLine();

        if (estado == ResolucionEstado.Liquida)
        {
            sb.AppendLine($"Hemos revisado su solicitud de reembolso del sobre N.° {sobre}, correspondiente a la atención recibida en {prestador}.")
              .AppendLine()
              .AppendLine($"Nos complace informarle que su solicitud fue CUBIERTA conforme a las condiciones de {producto}:")
              .AppendLine()
              .AppendLine($"  • Valor presentado:    {M(t.Presentado)}")
              .AppendLine($"  • Copago:              {M(t.Copago)}")
              .AppendLine($"  • Deducible aplicado:  {M(t.Deducible)}")
              .AppendLine($"  • Valor a reembolsar:  {M(t.EstimadoPagar == 0 ? t.Cubierto : t.EstimadoPagar)}")
              .AppendLine()
              .AppendLine("El valor reconocido será acreditado según el medio de pago registrado en su contrato.");
        }
        else if (estado == ResolucionEstado.Negativa)
        {
            var motivo = dto.Items.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Motivo))?.Motivo
                         ?? dto.Observaciones.FirstOrDefault() ?? "No cumple las condiciones de cobertura del plan.";
            sb.AppendLine($"Hemos revisado con detenimiento su solicitud de reembolso del sobre N.° {sobre} (atención en {prestador}), bajo {producto}.")
              .AppendLine()
              .AppendLine("Lamentamos informarle que, en esta ocasión, su solicitud NO PROCEDE para reembolso por el siguiente motivo:")
              .AppendLine()
              .AppendLine($"  → {motivo}")
              .AppendLine()
              .AppendLine("Entendemos que esta respuesta no es la esperada. Si considera que existe información adicional que respalde su caso, quedamos a su disposición en nuestros canales de atención.");
        }
        else // Semi / Ch → cobertura parcial / en revisión
        {
            sb.AppendLine($"Hemos concluido una primera revisión de su solicitud de reembolso del sobre N.° {sobre} (atención en {prestador}), bajo {producto}.")
              .AppendLine()
              .AppendLine("A continuación el detalle de lo reconocido y de los rubros en revisión:")
              .AppendLine();
            foreach (var it in dto.Items)
            {
                var mot = string.IsNullOrWhiteSpace(it.Motivo) ? "" : $" — {it.Motivo}";
                sb.AppendLine($"  • {it.Descripcion}: presentado {M(it.ValorPresentado)}, cubierto {M(it.ValorCubierto)}{mot}");
            }
            sb.AppendLine()
              .AppendLine($"Resumen: presentado {M(t.Presentado)} · copago {M(t.Copago)} · deducible {M(t.Deducible)} · a reembolsar {M(t.EstimadoPagar == 0 ? t.Cubierto : t.EstimadoPagar)}.")
              .AppendLine()
              .AppendLine("Algunos rubros requieren una revisión adicional de nuestro equipo; le contactaremos con la resolución definitiva.");
        }

        sb.AppendLine().AppendLine("Atentamente,").AppendLine("Reembolsos Saludsa")
          .AppendLine().AppendLine("——")
          .AppendLine("Este documento es una estimación de pre-liquidación de carácter informativo, NO OFICIAL.")
          .AppendLine("Los valores definitivos se confirman al liquidarse formalmente su reembolso en Saludsa.");

        return sb.ToString();
    }

    private static ResolucionEstado MapEstado(string? estadoPropuesto, double? confianza)
    {
        if (confianza is < 0.5) return ResolucionEstado.Ch;   // baja confianza → control humano
        return (estadoPropuesto ?? "").ToUpperInvariant() switch
        {
            "LIQUIDA_AUTO" => ResolucionEstado.Liquida,
            "SEMI"         => ResolucionEstado.Semi,
            "NEGATIVA"     => ResolucionEstado.Negativa,
            "CONTROL_HUMANO" => ResolucionEstado.Ch,
            _              => ResolucionEstado.Ch
        };
    }

    /// <summary>
    /// Extrae el primer objeto JSON balanceado del texto del agente
    /// (tolera fences ```json y prosa alrededor).
    /// </summary>
    private static string? ExtractJson(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var start = texto.IndexOf('{');
        if (start < 0) return null;

        int depth = 0; bool inStr = false; char prev = '\0';
        for (var i = start; i < texto.Length; i++)
        {
            var c = texto[i];
            if (inStr)
            {
                if (c == '"' && prev != '\\') inStr = false;
            }
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
