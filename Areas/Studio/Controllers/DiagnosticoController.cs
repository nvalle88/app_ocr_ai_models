using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace app_ocr_ai_models.Areas.Studio.Controllers;

/// <summary>
/// Diagnóstico de las conexiones SQL que usan las tools, ejecutado DESDE DONDE
/// CORRE LA APP.
///
/// Nació de un fallo que no se podía reproducir: <c>consultar_autorizaciones</c>
/// moría con «Execution Timeout» a los 30 segundos en web-nexus-test, mientras
/// la misma consulta, con los mismos parámetros y contra el mismo servidor,
/// tardaba 1 segundo desde un portátil en la VPN. Se descartaron midiendo el
/// coste de la consulta, los índices, el tipo de los parámetros, el plan
/// cacheado, los reintentos y los bloqueos: todos iban rápido.
///
/// Lo que quedaba era el ALCANCE desde el App Service, y eso no se puede medir
/// desde fuera. Kudu está cerrado (401: credenciales básicas deshabilitadas) y
/// abrirlo es tocar la seguridad de la app. Así que se replica aquí lo que hace
/// el ejecutor de tools —abrir la conexión y lanzar un SELECT— y se cronometra
/// cada paso por separado, que es lo que distingue «no llego a la máquina» de
/// «la consulta es lenta».
///
/// Es SOLO LECTURA y no enseña contraseñas: de cada cadena salen el host y la
/// base, nunca la credencial.
/// </summary>
[Area("Studio")]
[Authorize]
public class DiagnosticoController : Controller
{
    private readonly IConfiguration _config;

    public DiagnosticoController(IConfiguration config) => _config = config;

    /// <summary>Las cadenas que usan las tools, en el orden en que se prueban.</summary>
    private static readonly string[] Cadenas =
    {
        "SaludConsultas", "SaludReclamos", "SaludPrestadores",
        "SaludsaCreditoFarmacia", "DefaultConnection"
    };

    // GET /Studio/Diagnostico/Conexiones
    public async Task<IActionResult> Conexiones(CancellationToken ct)
    {
        var resultado = new List<object>();

        foreach (var nombre in Cadenas)
        {
            var cs = _config.GetConnectionString(nombre);
            if (string.IsNullOrWhiteSpace(cs))
            {
                resultado.Add(new { conexion = nombre, estado = "NO CONFIGURADA" });
                continue;
            }

            resultado.Add(await ProbarAsync(nombre, cs, ct));
        }

        return Json(new
        {
            ambiente = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "(sin fijar)",
            maquina  = Environment.MachineName,
            momento  = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC",
            conexiones = resultado
        });
    }

    /// <summary>
    /// Abrir y consultar se cronometran POR SEPARADO a propósito: si lo que
    /// tarda es abrir, el problema es de red o de firewall; si lo que tarda es
    /// el SELECT, es de la base. Juntos no distinguen una cosa de la otra, que
    /// es justo lo que hacía imposible el diagnóstico desde fuera.
    /// </summary>
    private static async Task<object> ProbarAsync(string nombre, string cs, CancellationToken ct)
    {
        var reloj = Stopwatch.StartNew();
        long msAbrir = -1;

        try
        {
            // Timeout corto a propósito: aquí interesa saber SI llega, no
            // esperar treinta segundos por cada una de las cinco.
            var b = new SqlConnectionStringBuilder(cs) { ConnectTimeout = 10 };

            await using var cn = new SqlConnection(b.ConnectionString);
            await cn.OpenAsync(ct);
            msAbrir = reloj.ElapsedMilliseconds;

            await using var cmd = new SqlCommand(
                "SELECT @@SERVERNAME, DB_NAME(), @@VERSION", cn) { CommandTimeout = 10 };
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);

            var servidor = r.GetValue(0)?.ToString();
            var baseDatos = r.GetValue(1)?.ToString();

            return new
            {
                conexion = nombre,
                destino  = Destino(cs),
                estado   = "OK",
                msAbrir,
                msTotal  = reloj.ElapsedMilliseconds,
                servidor,
                baseDatos
            };
        }
        catch (Exception ex)
        {
            return new
            {
                conexion = nombre,
                destino  = Destino(cs),
                estado   = msAbrir < 0 ? "NO ABRE" : "ABRE PERO FALLA LA CONSULTA",
                msAbrir,
                msTotal  = reloj.ElapsedMilliseconds,
                error    = ex.GetType().Name,
                mensaje  = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message
            };
        }
    }

    /// <summary>Host y base de una cadena, SIN la credencial.</summary>
    private static string Destino(string cs)
    {
        try
        {
            var b = new SqlConnectionStringBuilder(cs);
            return $"{b.DataSource} / {b.InitialCatalog}";
        }
        catch
        {
            // Una cadena mal formada no debe tumbar el diagnóstico: se saca el
            // host a mano y se sigue con las demás.
            var m = Regex.Match(cs, @"(?i)(?:data source|server)\s*=\s*([^;]+)");
            return m.Success ? m.Groups[1].Value.Trim() : "(no se pudo leer)";
        }
    }
}
