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
using app_tramites.Services.Ai.Tools;

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
    private readonly ISaludsaTokenProvider? _token;
    private readonly IHttpClientFactory? _http;

    public DiagnosticoController(IConfiguration config, OCRDbContext db,
                                 ISaludsaTokenProvider? token = null,
                                 IHttpClientFactory? http = null)
    {
        _config = config;
        _db = db;
        _token = token;
        _http = http;
    }

    /// <summary>
    /// La portada. Antes devolvia el JSON crudo y Nestor tenia razon: un renglon
    /// de llaves y comillas escapadas no se entiende. El dato era el mismo; lo
    /// que faltaba era decirlo en castellano y con los colores de la marca.
    ///
    /// La pagina no calcula nada por si sola: los botones llaman a Conexiones,
    /// Tool y Api, que son las mismas rutas de antes. Se sigue pudiendo pedir el
    /// JSON a mano si alguien lo prefiere.
    /// </summary>
    [AllowAnonymous]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Las tools del chat, con la conexion por la que van y unos parametros
        // de ejemplo para que el boton "probar" haga algo util en vez de pedir
        // una consulta sin filtros que devolveria 0 filas siempre.
        var tools = await _db.OPAIModelTool.AsNoTracking()
            .Where(mt => mt.ModelCode == AgenteDelChat && mt.IsEnabled)
            .Join(_db.OPAITool.AsNoTracking().Where(t => t.IsActive),
                  mt => mt.ToolCode, t => t.Code, (mt, t) => t)
            .OrderBy(t => t.BindingType).ThenBy(t => t.Code)
            .Select(t => new { t.Code, t.BindingType, t.BindingConfig })
            .ToListAsync(ct);

        ViewData["Tools"] = tools.Select(t =>
        {
            string? conexion = null, baseUrl = null;
            try
            {
                using var doc = JsonDocument.Parse(t.BindingConfig ?? "{}");
                if (doc.RootElement.TryGetProperty("connection", out var cn))
                    conexion = cn.GetString();
                if (doc.RootElement.TryGetProperty("baseUrl", out var bu))
                    baseUrl = bu.GetString();
            }
            catch { /* una config rota no debe tumbar la pagina de diagnostico */ }

            return new
            {
                code = t.Code,
                tipo = t.BindingType,
                conexion,
                baseUrl,
                prueba = PruebaDeEjemplo(t.Code)
            };
        }).ToList();

        return View();
    }

    /// <summary>Las cadenas que usan las tools, en el orden en que se prueban.</summary>
    private static readonly string[] Cadenas =
    {
        "SaludConsultas", "SaludReclamos", "SaludPrestadores",
        "SaludsaCreditoFarmacia", "DefaultConnection"
    };

    private const string AgenteDelChat = "AGENTE_CHAT_CLIENTE";

    /// <summary>
    /// Parametros de ejemplo por tool, con identificadores REALES de pruebas.
    /// Sin ellos el boton probaria consultas sin filtro: todas dirian 0 filas y
    /// eso no distingue una tool rota de una consulta sin resultados.
    /// </summary>
    private static string PruebaDeEjemplo(string code) => code switch
    {
        "consultar_autorizaciones"        => "p_contrato=4102902",
        "consultar_sobre_bd"              => "p_numeroSobre=NA-2612807",
        "consultar_detalle_sobre_bd"      => "p_numeroSobre=NA-2612807",
        "historial_reembolsos_cliente_bd" => "p_contrato=4102902",
        "buscar_medicina"                 => "p_nombre=losartan",
        "tarifario_prestador"             => "p_numeroConvenio=51493",
        "buscar_prestador_convenio"       => "p_numeroConvenio=51493",
        "buscar_sucursales_cerca"         => "p_ciudad=Quito&p_cerca=la carolina&p_tipo=farmacia",
        "copago_del_prestador"            => "p_numeroConvenio=11715",
        "factura_ya_pagada_bd"            => "p_numeroFactura=001-002-000002600",
        "condiciones_del_plan"            => "p_codigoProducto=IND&p_codigoPlan=N5-C&p_version=32",
        "coberturas_y_topes_del_plan"     => "p_codigoProducto=IND&p_codigoPlan=N5-C&p_version=32&p_region=Costa",
        "codigo_liquidacion_y_cobertura"  => "p_numeroConvenio=51493",
        "consultar_deducible_contrato"    => "p_region=Costa&p_codigoProducto=IND&p_numeroContrato=4102902&p_numeroPersonaBeneficiario=5446025",
        "consultar_coberturas_plan"       => "p_region=Costa&p_codigoProducto=IND&p_codigoPlan=N5-C&p_versionPlan=32&p_contratoNumero=4102902&p_personaNumero=5446025",
        _ => ""
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

        // El GATEWAY tambien: las dos tools que van por HTTP mueren a los 15 s
        // sin llegar a pedir el token, y sin probar el puerto no se distingue
        // "no llego al host" de "el servicio tarda".
        foreach (var clave3 in new[] { "Saludsa:BaseUrls:ApiContrato", "Saludsa:Auth:TokenUrl" })
        {
            var u = _config[clave3];
            if (string.IsNullOrWhiteSpace(u) || !Uri.TryCreate(u, UriKind.Absolute, out var uri))
                continue;
            red.Add(await TcpAsync(uri.Host, uri.Port, ct));
        }

        // El gateway por su IP PRIVADA y por la PUBLICA. Nestor lo vio: ese
        // servicio esta publicado en internet -el DNS publico devuelve
        // 107.154.79.171, un Imperva- y solo el DNS corporativo lo resuelve a
        // 10.66.66.117. Si la publica abre y la privada no, el problema no es
        // un cortafuegos ajeno: es que la app esta preguntandole al DNS
        // equivocado y saliendo por el tunel en vez de por internet.
        red.Add(await TcpAsync("10.66.66.117", 443, ct));
        red.Add(await TcpAsync("107.154.79.171", 443, ct));

        // Y cualquier otro que se quiera probar sin volver a desplegar.
        var extra = Request.Query["host"].ToString();
        if (!string.IsNullOrWhiteSpace(extra))
        {
            var pt = int.TryParse(Request.Query["puerto"], out var pp) ? pp : 443;
            red.Add(await TcpAsync(extra, pt, ct));
        }

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
                                          string? clave = null)
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

        // Los parametros de la tool llegan como p_<nombre> en la query. Con
        // nombres fijos solo se podia probar consultar_autorizaciones; el resto
        // de las tools piden convenio, ciudad, plan o medicina, y sin poder
        // pasarselos la prueba habria dicho "0 filas" de todas y eso no
        // distingue una tool rota de una consulta sin resultados.
        var valores = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k2, v2) in Request.Query)
            if (k2.StartsWith("p_", StringComparison.OrdinalIgnoreCase))
                valores[k2[2..]] = v2.ToString();

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

    // GET /Studio/Diagnostico/Api?code=<tool>&clave=...&p_<param>=<valor>
    //
    // Lo mismo que Tool pero para las tools que van por el GATEWAY. Cronometra
    // el TOKEN aparte de la llamada: son dos fallos distintos -credencial
    // contra alcance- y juntos no se distinguen. Devuelve el codigo HTTP y el
    // tama~o de la respuesta, NUNCA su contenido: son datos de un afiliado.
    [AllowAnonymous]
    public async Task<IActionResult> Api(string code, CancellationToken ct, string? clave = null)
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            var esperada = _config["Diagnostico:Clave"];
            if (string.IsNullOrWhiteSpace(esperada) ||
                !string.Equals(clave, esperada, StringComparison.Ordinal))
                return NotFound();
        }
        if (_token == null || _http == null)
            return Json(new { error = "sin proveedor de token o de http" });

        var tool = await _db.OPAITool.AsNoTracking().FirstOrDefaultAsync(t => t.Code == code, ct);
        if (tool == null || tool.BindingType == "Sql")
            return Json(new { error = "no existe o no es una tool de gateway", code });

        using var doc = JsonDocument.Parse(tool.BindingConfig ?? "{}");
        var raiz = doc.RootElement;
        var plantilla = raiz.TryGetProperty("baseUrl", out var b0) ? b0.GetString() ?? "" : "";
        var ruta = raiz.TryGetProperty("path", out var p0) ? p0.GetString() ?? "" : "";
        var metodo = raiz.TryGetProperty("method", out var m0) ? m0.GetString() ?? "GET" : "GET";

        // La MISMA lista que InternalApiToolExecutor. Cuando faltaban tres,
        // consultar_mis_reembolsos aparecia rota -"invalid request URI"- y la
        // tool estaba bien: el que no sabia resolver era el diagnostico. Un
        // diagnostico que no imita al ejecutor no diagnostica: confunde.
        var clave2 = plantilla switch
        {
            "{api-contrato}"     => "Saludsa:BaseUrls:ApiContrato",
            "{api-armonix}"      => "Saludsa:BaseUrls:ApiArmonix",
            "{api-prestador}"    => "Saludsa:BaseUrls:ApiPrestador",
            "{api-repositorio}"  => "Saludsa:BaseUrls:ApiRepositorio",
            "{api-liquidacion}"  => "Saludsa:BaseUrls:ApiLiquidacion",
            "{api-reembolso-automatico}" => "Saludsa:BaseUrls:ApiReembolsoAutomatico",
            _ => null
        };
        var baseUrl = clave2 == null ? plantilla : _config[clave2];
        if (string.IsNullOrWhiteSpace(baseUrl))
            return Json(new { code, error = $"sin URL para {plantilla}", clave2 });

        var reloj = Stopwatch.StartNew();
        long msToken = -1;
        try
        {
            var cab = await _token.GetAuthHeadersAsync(ct);
            msToken = reloj.ElapsedMilliseconds;

            // El ejecutor manda paramMap por QUERY -tambien en POST- y bodyMap
            // en el cuerpo. Meterlo todo en el cuerpo daba 404 en
            // consultar_coberturas_plan_prestador, que exige sus tres
            // obligatorios en la query: otra tool sana marcada como rota.
            var enCuerpo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (raiz.TryGetProperty("bodyMap", out var bm) && bm.ValueKind == JsonValueKind.Array)
                foreach (var x in bm.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.String) enCuerpo.Add(x.GetString()!);

            var qs = string.Join("&", Request.Query
                .Where(x => x.Key.StartsWith("p_", StringComparison.OrdinalIgnoreCase))
                .Where(x => !enCuerpo.Contains(x.Key[2..]))
                .Select(x => Uri.EscapeDataString(x.Key[2..]) + "=" +
                             Uri.EscapeDataString(x.Value.ToString())));
            var url = baseUrl.TrimEnd('/') + ruta + (qs.Length > 0 ? "?" + qs : "");

            using var req = new HttpRequestMessage(new HttpMethod(metodo), url);
            foreach (var (n, v) in cab) req.Headers.TryAddWithoutValidation(n, v);
            if (metodo != "GET")
            {
                var cuerpo = Request.Query
                    .Where(x => x.Key.StartsWith("p_", StringComparison.OrdinalIgnoreCase))
                    .Where(x => enCuerpo.Count == 0 || enCuerpo.Contains(x.Key[2..]))
                    .ToDictionary(x => x.Key[2..], x => (object?)x.Value.ToString());
                req.Content = new StringContent(JsonSerializer.Serialize(cuerpo),
                                                System.Text.Encoding.UTF8, "application/json");
            }

            using var cli = _http.CreateClient();
            cli.Timeout = TimeSpan.FromSeconds(60);
            using var resp = await cli.SendAsync(req, ct);
            var texto = await resp.Content.ReadAsStringAsync(ct);

            return Json(new
            {
                code, url = url.Length > 160 ? url[..160] : url, metodo,
                msToken, msTotal = reloj.ElapsedMilliseconds,
                http = (int)resp.StatusCode,
                bytes = texto.Length,
                // Solo el sobre, no los datos: dice si el servicio contesto bien
                // sin sacar del sistema la informacion de nadie.
                estadoEnCuerpo = texto.Contains("\"Estado\":\"OK\"") ? "OK"
                               : texto.Contains("\"Estado\":\"Error\"") ? "Error en el cuerpo"
                               : "(sin campo Estado)"
            });
        }
        catch (Exception ex)
        {
            return Json(new { code, msToken, msTotal = reloj.ElapsedMilliseconds,
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

        // El NOMBRE y la CONEXION se miden por separado. Sin esto no se
        // distingue "no resuelvo el nombre" de "no llego a la maquina", y son
        // dos averias distintas con dos duenos distintos.
        //
        // Y no es teorico: las cadenas SQL de esta app van por IP -porque desde
        // Azure el DNS corporativo no resolvia- mientras el gateway va por
        // nombre. Si el nombre es el que falla, la culpa no es del cortafuegos.
        string? ip = null;
        long msDns = -1;
        if (!System.Net.IPAddress.TryParse(host, out _))
        {
            try
            {
                var dns = System.Net.Dns.GetHostAddressesAsync(host, ct);
                if (await Task.WhenAny(dns, Task.Delay(6000, ct)) == dns)
                    ip = (await dns).FirstOrDefault()?.ToString();
                msDns = reloj.ElapsedMilliseconds;
                if (ip == null)
                    return new { host, puerto, abre = false, ms = reloj.ElapsedMilliseconds,
                                 dns = "NO RESUELVE", msDns,
                                 detalle = "el nombre no se resuelve desde aqui: es DNS, no cortafuegos" };
            }
            catch (Exception dnsEx)
            {
                return new { host, puerto, abre = false, ms = reloj.ElapsedMilliseconds,
                             dns = "NO RESUELVE", msDns = reloj.ElapsedMilliseconds,
                             detalle = "DNS: " + (dnsEx.Message.Length > 90 ? dnsEx.Message[..90] : dnsEx.Message) };
            }
        }

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
                         dns = ip ?? "(es una IP)", msDns, detalle = "puerto abierto" };
        }
        catch (Exception ex)
        {
            return new { host, puerto, abre = false, ms = reloj.ElapsedMilliseconds,
                         dns = ip ?? "(es una IP)", msDns,
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
