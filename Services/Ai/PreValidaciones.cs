using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using app_tramites.Services.Ai.Tools;
using Microsoft.Extensions.Logging;

namespace app_ocr_ai_models.Services.Ai;

// =============================================================================
// REQ-026 — Lo que se va a consultar SÍ o SÍ, se consulta antes y a la vez
//
// En un bucle de tool-use, cada herramienta es una IDA Y VUELTA COMPLETA al
// modelo: se le manda la conversación entera, contesta "llama a esta", se
// ejecuta, se le devuelve el resultado y vuelve a pensar. Siete herramientas son
// ocho viajes, y el afiliado los paga todos en fila.
//
// Medido sobre los últimos diez casos del portal:
//
//     AGENTE_CLAUDE .............. 7,3 herramientas por ejecución → 115 s
//     AGENTE_AUDITOR_MEDICINA .... 6,6 herramientas por ejecución →  87 s
//     AGENTE_PORTAL_CLIENTE ...... 1,0 herramienta  por ejecución →   4 s
//
// La diferencia entre 4 y 115 segundos no es lo que tardan las herramientas
// —eso son segundos sueltos— sino el número de viajes.
//
// Y hay una parte que NO hace falta descubrir: el contrato del afiliado, sus
// preexistencias y el convenio del prestador se van a consultar siempre, en
// todos los casos. Preguntárselas al modelo una por una es hacerle descubrir lo
// que ya sabemos.
//
// Aquí se lanzan TODAS A LA VEZ, antes de la primera llamada, y sus respuestas
// entran ya en el mensaje. El modelo empieza con los datos delante.
//
// -- Lo que esto NO hace -----------------------------------------------------
// No le quita las herramientas al agente. Si necesita algo más —o quiere
// confirmar algo— las sigue teniendo. Y como el ejecutor memoriza por caso, si
// vuelve a pedir una de estas la respuesta sale del memo sin ir a la VPN.
//
// -- Si una falla, no pasa nada ----------------------------------------------
// Se omite del bloque y ya está: el agente puede pedirla por su cuenta. Un
// prelanzamiento que rompe el análisis cuando falla sería peor que no tenerlo.
// =============================================================================

public sealed record DatoPrevio(string Tool, string Titulo, string Json);

public interface IPreValidaciones
{
    /// <summary>
    /// Lanza en paralelo lo que siempre se consulta y devuelve el bloque listo
    /// para meter en el mensaje. Vacío si no hay nada que traer.
    /// </summary>
    Task<string> BloqueAsync(string agenteCode, long executionId, string? cedula,
                             IEnumerable<string?> rucsEmisores, string? caseIdentity,
                             CancellationToken ct = default);
}

public sealed class PreValidaciones : IPreValidaciones
{
    // Mismo tope que el OCR y que el clasificador, por el mismo motivo: el cuello
    // es la espera del servicio, no la CPU.
    private const int ALaVez = 4;

    private readonly IToolExecutor _tools;
    private readonly ILogger<PreValidaciones> _log;

    public PreValidaciones(IToolExecutor tools, ILogger<PreValidaciones> log)
    {
        _tools = tools; _log = log;
    }

    public async Task<string> BloqueAsync(string agenteCode, long executionId, string? cedula,
                                          IEnumerable<string?> rucsEmisores, string? caseIdentity,
                                          CancellationToken ct = default)
    {
        var pedidos = new List<(string Tool, string Titulo, Dictionary<string, object?> Args)>();

        // La cédula va a 10 dígitos CON el cero inicial: la API rechaza la de 9.
        var doc = (cedula ?? string.Empty).Trim();
        if (doc.Length is 9) doc = "0" + doc;

        if (doc.Length >= 9)
        {
            pedidos.Add(("resolver_contrato_por_cedula", "Contrato del afiliado",
                new Dictionary<string, object?> { ["numeroDocumento"] = doc, ["tipoDocumento"] = "C" }));

            pedidos.Add(("consultar_preexistencias_por_cedula", "Preexistencias registradas",
                new Dictionary<string, object?> { ["identificacion"] = doc }));
        }

        // Un sobre puede traer facturas de varios prestadores: se resuelven todos.
        foreach (var ruc in rucsEmisores.Where(r => !string.IsNullOrWhiteSpace(r))
                                        .Select(r => r!.Trim()).Distinct().Take(4))
        {
            pedidos.Add(("resolver_convenio_por_ruc", $"Convenio del prestador {ruc}",
                new Dictionary<string, object?> { ["ruc"] = ruc }));
        }

        if (pedidos.Count == 0) return string.Empty;

        using var turno = new SemaphoreSlim(ALaVez, ALaVez);

        var tareas = pedidos.Select(async p =>
        {
            await turno.WaitAsync(ct);
            try
            {
                var json = await _tools.ExecuteAsync(p.Tool, agenteCode, p.Args, executionId, caseIdentity, ct);
                return new DatoPrevio(p.Tool, p.Titulo, json);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Que falle una NO puede tumbar el análisis: se omite y el agente
                // la pedirá por su cuenta si la necesita.
                _log.LogWarning(ex, "[PreValidaciones] '{Tool}' no se pudo adelantar.", p.Tool);
                return null;
            }
            finally { turno.Release(); }
        }).ToList();

        var datos = (await Task.WhenAll(tareas)).Where(d => d != null).Cast<DatoPrevio>().ToList();
        if (datos.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## Consultas ya hechas por ti (no hace falta volver a pedirlas)");
        sb.AppendLine("Esto se consultó ANTES de escribirte, en paralelo. Son las respuestas");
        sb.AppendLine("reales de las mismas herramientas que tienes. Úsalas directamente.");
        sb.AppendLine();

        foreach (var d in datos)
        {
            sb.AppendLine($"### {d.Titulo}  ·  herramienta `{d.Tool}`");
            sb.AppendLine(Recortar(d.Json, 6000));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Un tope por respuesta: el historial de un afiliado con veinte sobres puede
    /// ocupar más que todo el resto del mensaje, y entonces el prelanzamiento
    /// dejaría de ahorrar tiempo para empezar a costarlo.
    /// </summary>
    private static string Recortar(string texto, int max) =>
        string.IsNullOrEmpty(texto) || texto.Length <= max
            ? texto
            : texto[..max] + $"\n… (recortado, {texto.Length - max} caracteres más; "
                           + "vuelve a llamar a la herramienta si necesitas el resto)";
}
