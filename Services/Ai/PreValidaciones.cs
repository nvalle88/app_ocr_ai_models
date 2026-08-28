using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using app_ocr_ai_models.Data;
using app_tramites.Services.Ai.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace app_ocr_ai_models.Services.Ai;

// =============================================================================
// REQ-026 — Al agente sólo lo que hace falta pensar
//
// La primera versión de esto lanzaba las herramientas en paralelo para no pagar
// una ida y vuelta por cada una. Estaba atacando el síntoma: el contrato del
// afiliado NO hay que ir a buscarlo, porque **ya lo tenemos**.
//
// Cuando el afiliado se identifica en el portal, se resuelve su contrato una vez
// y se guarda entero en SolicitudCliente:
//
//     NumeroContrato · CodigoProducto · CodigoRegion · CodigoPlan · NombrePlan
//     NumeroPersona  · ContratoJson   · DeducibleCubierto
//     EnCarencia     · DiasFinCarencia · TienePreexistencias · BeneficiariosJson
//
// Pedirlo otra vez por una herramienta —y encima haciendo que el modelo lo
// descubra en una vuelta entera de conversación— es trabajo regalado dos veces.
// En la traza salía como «Localizando su contrato · 0,6 s», y ni esos 0,6 s ni
// los ~10 s de la vuelta al modelo hacían falta.
//
// -- La regla que se sigue aquí ----------------------------------------------
// El agente es para lo que hay que INTERPRETAR: qué dice un documento, qué
// procedimiento es ese texto, si el soporte corresponde a la factura. Todo lo
// que es leer un dato que ya está, o una cuenta, va en código normal.
//
// Lo único que queda como llamada es el convenio del prestador, porque su RUC
// sale de la factura y no se conoce hasta que el documento está leído. Y aun
// ése se resuelve aquí, en paralelo, no en el bucle del modelo.
// =============================================================================

public interface IPreValidaciones
{
    /// <summary>
    /// El bloque de datos que el agente NO tiene que ir a buscar. Vacío si no
    /// hay nada que darle.
    /// </summary>
    Task<string> BloqueAsync(Guid caseCode, string agenteCode, long executionId,
                             IEnumerable<string?> rucsEmisores, string? caseIdentity,
                             CancellationToken ct = default);
}

public sealed class PreValidaciones : IPreValidaciones
{
    // Mismo tope que el OCR y el clasificador, por el mismo motivo: el cuello es
    // la espera del servicio, no la CPU.
    private const int ALaVez = 4;

    private readonly OCRDbContext _db;
    private readonly IToolExecutor _tools;
    private readonly ILogger<PreValidaciones> _log;

    public PreValidaciones(OCRDbContext db, IToolExecutor tools, ILogger<PreValidaciones> log)
    {
        _db = db; _tools = tools; _log = log;
    }

    public async Task<string> BloqueAsync(Guid caseCode, string agenteCode, long executionId,
                                          IEnumerable<string?> rucsEmisores, string? caseIdentity,
                                          CancellationToken ct = default)
    {
        var sb = new StringBuilder();

        // ── 1. Lo que YA está en la base: cero llamadas, cero espera ─────────
        var sol = await _db.SolicitudCliente.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CaseCode == caseCode, ct);

        if (sol != null)
        {
            sb.AppendLine("## Contrato del afiliado (ya resuelto, NO lo vuelvas a consultar)");
            sb.AppendLine($"- Contrato: {sol.NumeroContrato}");
            sb.AppendLine($"- Producto: {sol.CodigoProducto}   Región: {sol.CodigoRegion}");
            sb.AppendLine($"- Plan: {sol.CodigoPlan} ({sol.NombrePlan})");
            sb.AppendLine($"- Titular: {sol.NombreTitular}   Persona: {sol.NumeroPersona}");

            if (!string.IsNullOrWhiteSpace(sol.NombreBeneficiario))
                sb.AppendLine($"- El reembolso es para: {sol.NombreBeneficiario} "
                            + $"({sol.RelacionBeneficiario}, {sol.EdadBeneficiario} años, "
                            + $"cédula {sol.CedulaBeneficiario})");

            if (sol.DeducibleCubierto.HasValue)
                sb.AppendLine("- Deducible ya cubierto: "
                            + sol.DeducibleCubierto.Value.ToString("N2", CultureInfo.GetCultureInfo("es-EC")));

            // Las dos carencias, separadas. Cruzarlas niega consultas cubiertas o
            // paga hospitalizaciones que no lo están.
            sb.AppendLine($"- Carencia ambulatoria: {Si(sol.EnCarencia)}"
                        + (sol.DiasFinCarencia is > 0 ? $" (faltan {sol.DiasFinCarencia} días)" : ""));
            sb.AppendLine($"- Tiene preexistencias registradas: {Si(sol.TienePreexistencias)}");
            sb.AppendLine();

            // El contrato completo, por si necesita un detalle que no está arriba.
            if (!string.IsNullOrWhiteSpace(sol.ContratoJson))
            {
                sb.AppendLine("### Contrato completo (JSON)");
                sb.AppendLine(Recortar(sol.ContratoJson!, 5000));
                sb.AppendLine();
            }
        }

        // ── 2. Lo único que sí hay que ir a buscar ───────────────────────────
        //
        // El RUC del prestador sale de la factura, así que no se conoce hasta que
        // el documento está leído. Se resuelve aquí y en paralelo, no dentro del
        // bucle del modelo.
        var rucs = rucsEmisores.Where(r => !string.IsNullOrWhiteSpace(r))
                               .Select(r => r!.Trim()).Distinct().Take(4).ToList();

        if (rucs.Count > 0)
        {
            using var turno = new SemaphoreSlim(ALaVez, ALaVez);

            var tareas = rucs.Select(async ruc =>
            {
                await turno.WaitAsync(ct);
                try
                {
                    var json = await _tools.ExecuteAsync("resolver_convenio_por_ruc", agenteCode,
                        new Dictionary<string, object?> { ["ruc"] = ruc },
                        executionId, caseIdentity, ct);
                    return (Ruc: ruc, Json: json);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // Que falle una no puede tumbar el análisis: se omite y el
                    // agente la pedirá por su cuenta si la necesita.
                    _log.LogWarning(ex, "[PreValidaciones] convenio del RUC {Ruc} no se pudo adelantar.", ruc);
                    return (Ruc: ruc, Json: (string?)null);
                }
                finally { turno.Release(); }
            }).ToList();

            var convenios = (await Task.WhenAll(tareas)).Where(x => x.Json != null).ToList();

            if (convenios.Count > 0)
            {
                sb.AppendLine("## Convenio de los prestadores que facturaron (ya consultado)");
                foreach (var c in convenios)
                {
                    sb.AppendLine($"### RUC {c.Ruc}");
                    sb.AppendLine(Recortar(c.Json!, 3000));
                    sb.AppendLine();
                }
            }
        }

        return sb.Length == 0 ? string.Empty : sb.ToString();
    }

    private static string Si(bool? v) => v == true ? "SÍ" : v == false ? "no" : "no informado";

    /// <summary>
    /// Un tope por bloque: si el contrato de un afiliado con veinte dependientes
    /// ocupa más que los documentos, esto deja de ahorrar tiempo y empieza a
    /// costarlo.
    /// </summary>
    private static string Recortar(string texto, int max) =>
        string.IsNullOrEmpty(texto) || texto.Length <= max
            ? texto
            : texto[..max] + $"… (recortado, {texto.Length - max} caracteres más)";
}
