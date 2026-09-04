using System.Data;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using app_ocr_ai_models.Data;

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
    private readonly OCRDbContext _db;

    public DiagnosticoController(IConfiguration config, OCRDbContext db)
    {
        _config = config;
        _db = db;
    }

    /// <summary>Las cadenas que usan las tools, en el orden en que se prueban.</summary>
    private static readonly string[] Cadenas =
    {
        "SaludConsultas", "SaludReclamos", "SaludPrestadores",
        "SaludsaCreditoFarmacia", "DefaultConnection"
    };

    // GET /Studio/Diagnostico/Conexiones[?clave=...]
    //
    // Se puede llamar SIN sesion, pero solo con la clave de 'Diagnostico:Clave'
    // de App Settings. Hizo falta porque el fallo solo se ve DESDE Azure y la
    // sesion del navegador vive en la maquina de quien lo reporta. Sin clave
    // configurada, o con una que no coincide, responde 404: un 403 confirmaria
    // que la ruta existe.
    [AllowAnonymous]
    public async Task<IActionResult> Conexiones(CancellationToken ct, string? clave = null)
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            var esperada = _config["Diagnostico:Clave"];
            if (string.IsNullOrWhiteSpace(esperada) ||
                !string.Equals(clave, esperada, StringComparison.Ordinal))
                return NotFound();
        }

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

        // Un TCP por HOST distinto, no por cadena: tres cadenas al mismo
        // servidor comparten la suerte de la red y probarlo tres veces solo
        // alarga la espera.
        var hosts = Cadenas
            .Select(n => _config.GetConnectionString(n))
            .Where(cs => !string.IsNullOrWhiteSpace(cs))
            .Select(cs => SoloHost(cs!))
            .Where(h => h.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var red = new List<object>();
        foreach (var h in hosts) red.Add(await TcpAsync(h, 1433, ct));

        return Json(new
        {
            ambiente = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "(sin fijar)",
            red,
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

    // GET /Studio/Diagnostico/Tool?code=consultar_autorizaciones&contrato=...&clave=...
    //
    // Corre la consulta REAL de una tool desde donde corre la app, igual que el
    // ejecutor -mismos parametros varchar, mismo CommandTimeout- y devuelve
    // SOLO el tiempo y el numero de filas. Nunca las filas: son datos de salud
    // de una persona y aqui lo que se diagnostica es el reloj, no el contenido.
    //
    // Hizo falta porque la consulta tarda 1 s desde la VPN y muere a los 30
    // desde Azure. Medir en la maquina equivocada ya me llevo a una conclusion
    // falsa una vez -di por hecho que la red no llegaba, y llega-.
    [AllowAnonymous]
    public async Task<IActionResult> Tool(string code, CancellationToken ct,
                                          string? clave = null, string? contrato = null,
                                          string? cedula = null, string? numeroAutorizacion = null)
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            var esperada = _config["Diagnostico:Clave"];
            if (string.IsNullOrWhiteSpace(esperada) ||
                !string.Equals(clave, esperada, StringComparison.Ordinal))
                return NotFound();
        }

        var tool = await _db.OPAITool.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Code == code, ct);
        if (tool == null || tool.BindingType != "Sql")
            return Json(new { error = "no existe o no es una tool de SQL", code });

        using var doc = JsonDocument.Parse(tool.BindingConfig ?? "{}");
        var raiz = doc.RootElement;
        var conn = raiz.TryGetProperty("connection", out var cn0) ? cn0.GetString() : "SaludConsultas";
        var query = raiz.TryGetProperty("query", out var q0) ? q0.GetString() ?? "" : "";
        var cs = _config.GetConnectionString(conn!);
        if (string.IsNullOrWhiteSpace(cs) || query.Length == 0)
            return Json(new { error = "sin cadena o sin consulta", conexion = conn });

        var valores = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["contrato"] = contrato, ["cedula"] = cedula,
            ["numeroAutorizacion"] = numeroAutorizacion
        };

        var relojTotal = Stopwatch.StartNew();
        long msAbrir = -1, msPrimeraFila = -1;
        try
        {
            await using var c = new SqlConnection(cs);
            await c.OpenAsync(ct);
            msAbrir = relojTotal.ElapsedMilliseconds;

            await using var cmd = new SqlCommand(query, c) { CommandTimeout = 30 };
            foreach (Match m in Regex.Matches(query, @"@([A-Za-z_][A-Za-z0-9_]*)"))
            {
                var n = m.Groups[1].Value;
                if (cmd.Parameters.Contains("@" + n)) continue;
                valores.TryGetValue(n, out var v);
                cmd.Parameters.Add(new SqlParameter("@" + n, SqlDbType.VarChar, 400)
                { Value = (object?)v ?? DBNull.Value });
            }

            var filas = 0;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                if (filas == 0) msPrimeraFila = relojTotal.ElapsedMilliseconds;
                filas++;
            }
            return Json(new { code, conexion = conn, estado = "OK", msAbrir, msPrimeraFila,
                              msTotal = relojTotal.ElapsedMilliseconds, filas });
        }
        catch (Exception ex)
        {
            return Json(new { code, conexion = conn, estado = "FALLA", msAbrir, msPrimeraFila,
                              msTotal = relojTotal.ElapsedMilliseconds,
                              error = ex.GetType().Name,
                              mensaje = ex.Message.Length > 250 ? ex.Message[..250] : ex.Message });
        }
    }

    /// <summary>
    /// El equivalente al ping que SI se puede hacer desde un App Service.
    ///
    /// Azure bloquea ICMP en el sandbox: un ping de toda la vida no sale de
    /// aqui, y su ausencia no probaria nada. Lo que si se puede -y ademas es lo
    /// que de verdad importa- es abrir el puerto 1433 por TCP, que es lo que
    /// necesita SQL Server. Separa las dos cosas que se confundian:
    ///
    ///   no abre el puerto  -> es la RED (ruta, firewall del servidor)
    ///   abre pero SQL falla -> llega bien, el problema es credencial o base
    /// </summary>
    private static async Task<object> TcpAsync(string host, int puerto, CancellationToken ct)
    {
        var reloj = Stopwatch.StartNew();
        try
        {
            using var cliente = new TcpClient();
            var tarea = cliente.ConnectAsync(host, puerto, ct).AsTask();
            var cortado = await Task.WhenAny(tarea, Task.Delay(8000, ct));
            if (cortado != tarea)
                return new { host, puerto, abre = false, ms = reloj.ElapsedMilliseconds,
                             detalle = "sin respuesta en 8 s (la red no llega o el firewall lo corta)" };
            await tarea;
            return new { host, puerto, abre = true, ms = reloj.ElapsedMilliseconds,
                         detalle = "puerto abierto" };
        }
        catch (Exception ex)
        {
            return new { host, puerto, abre = false, ms = reloj.ElapsedMilliseconds,
                         detalle = ex.Message.Length > 160 ? ex.Message[..160] : ex.Message };
        }
    }

    /// <summary>Host de una cadena, sin el prefijo tcp: ni el puerto.</summary>
    private static string SoloHost(string cs)
    {
        var m = Regex.Match(cs, @"(?i)(?:data source|server)\s*=\s*([^;]+)");
        if (!m.Success) return string.Empty;
        var h = m.Groups[1].Value.Trim();
        if (h.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) h = h[4..];
        var coma = h.IndexOf(',');
        return coma > 0 ? h[..coma] : h;
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
