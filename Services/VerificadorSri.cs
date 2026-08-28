using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using app_tramites.Services.Ai.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace app_ocr_ai_models.Services;

// =============================================================================
// REQ-024c — Una factura que el SRI no reconoce no es una factura
//
// La regla es la misma que la del duplicado: si no existe en el SRI, no entra.
// Un comprobante que la autoridad no reconoce no autoriza a pagar nada.
//
// -- POR QUÉ ESTO NO BLOQUEA TODAVÍA -----------------------------------------
//
// Porque la señal, HOY, no distingue "el SRI dice que no existe" de "desde aquí
// no se puede preguntar al SRI". Medido el 2026-08-28 contra pruebas, con cinco
// facturas REALES sacadas de reclamos pagados de produccion:
//
//     ...930000000113   no esta en el repositorio   SRI: no devolvio documento
//     ...670990967911   no esta en el repositorio   SRI: no devolvio documento
//     ...910502191018   no esta en el repositorio   SRI: no devolvio documento
//     ...785658032319   no esta en el repositorio   SRI: no devolvio documento
//     ...620003526217   no esta en el repositorio   SRI: no devolvio documento
//
// Cinco de cinco. Facturas que existen de verdad y que se pagaron. Bloquear con
// esa respuesta rechazaria TODAS las solicitudes del portal.
//
// Asi que el mecanismo queda montado y comprobado, y el bloqueo detras de un
// interruptor que por defecto esta APAGADO:
//
//     "Saludsa": { "BloquearSiNoEstaEnSri": false }
//
// El dia que la consulta al SRI funcione en el ambiente donde corra esto, se
// pone en true y la regla entra en vigor sin tocar codigo. Antes NO: una regla
// que dice que no a todo el mundo no es un control, es una averia.
//
// -- EL SRI TIENE QUE SER EL DE PRODUCCION -----------------------------------
//
// Esa es la razon de fondo del resultado de arriba: las facturas que sube el
// afiliado son REALES, y el unico SRI que las conoce es el de produccion. Contra
// pruebas la respuesta no significa nada.
//
// Por eso este verificador tiene su propia configuracion y NO arrastra la del
// resto de la aplicacion, que puede seguir apuntando a pruebas:
//
//     "Saludsa": {
//       "Sri": {
//         "BaseUrl": "https://servicios.saludsa.com.ec/ServicioRepositorio",
//         "Auth": { "TokenUrl": "...", "ClientId": "...", "Username": "...", "Password": "..." }
//       }
//     }
//
// Si no se configura, cae en la del resto y el resultado NO sirve para bloquear.
// Mientras tanto se informa igual —queda visible y en el log— para poder medir
// cada cuanto pasa de verdad.
// =============================================================================

/// <summary>Qué se sabe de una factura frente al SRI.</summary>
public enum EstadoSri
{
    /// <summary>Está en el repositorio de Saludsa o el SRI la devolvió.</summary>
    Encontrada,

    /// <summary>Se preguntó y NO existe. Es el único caso que justificaría bloquear.</summary>
    NoEncontrada,

    /// <summary>No se pudo preguntar: sin conexión, sin token, error del servicio.</summary>
    NoSePudoComprobar
}

public sealed class ResultadoSri
{
    public string ClaveAcceso { get; init; } = string.Empty;
    public EstadoSri Estado { get; init; }

    /// <summary>Lo que contestó el servicio, para el log. Nunca se le enseña al afiliado.</summary>
    public string? Detalle { get; init; }
}

public interface IVerificadorSri
{
    /// <summary>
    /// Busca cada factura en el repositorio y, si no está, pide al servicio que
    /// la traiga del SRI. No escribe nada nuestro: la carga la hace el API.
    /// </summary>
    Task<IReadOnlyList<ResultadoSri>> ComprobarAsync(IEnumerable<string> clavesAcceso,
                                                     CancellationToken ct = default);
}

public sealed class VerificadorSri : IVerificadorSri
{
    private readonly IHttpClientFactory _http;
    private readonly ISaludsaTokenProvider _token;
    private readonly IConfiguration _config;
    private readonly ILogger<VerificadorSri> _log;

    public VerificadorSri(IHttpClientFactory http, ISaludsaTokenProvider token,
                          IConfiguration config, ILogger<VerificadorSri> log)
    {
        _http = http; _token = token; _config = config; _log = log;
    }

    /// <summary>
    /// ¿Se bloquea cuando el SRI no la reconoce? Apagado por defecto: ver la
    /// cabecera de este fichero y la medición que hay detrás.
    /// </summary>
    public bool BloqueoActivo =>
        _config.GetValue("Saludsa:BloquearSiNoEstaEnSri", false) && ApuntaAProduccion;

    public async Task<IReadOnlyList<ResultadoSri>> ComprobarAsync(
        IEnumerable<string> clavesAcceso, CancellationToken ct = default)
    {
        var claves = clavesAcceso
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!.Trim())
            .Where(c => c.Length == 49)          // una clave de acceso son 49 dígitos
            .Distinct()
            .ToList();

        if (claves.Count == 0) return Array.Empty<ResultadoSri>();

        // El SRI propio manda sobre el general: el resto de la aplicacion puede
        // seguir en pruebas mientras esta comprobacion va a produccion, que es el
        // unico sitio donde las facturas del afiliado existen de verdad.
        var baseUrl = (_config["Saludsa:Sri:BaseUrl"]
                       ?? _config["Saludsa:BaseUrls:ApiRepositorio"]
                       ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _log.LogWarning("[SRI] Falta Saludsa:BaseUrls:ApiRepositorio. No se comprueba.");
            return claves.Select(c => new ResultadoSri
            {
                ClaveAcceso = c, Estado = EstadoSri.NoSePudoComprobar,
                Detalle = "sin BaseUrl configurada"
            }).ToList();
        }

        var salida = new List<ResultadoSri>(claves.Count);

        foreach (var clave in claves)
        {
            salida.Add(await UnaAsync(baseUrl, clave, ct));
        }

        return salida;
    }

    private async Task<ResultadoSri> UnaAsync(string baseUrl, string clave, CancellationToken ct)
    {
        try
        {
            var cli = _http.CreateClient();
            cli.Timeout = TimeSpan.FromSeconds(25);

            foreach (var (k, v) in await CabecerasAsync(ct))
                cli.DefaultRequestHeaders.TryAddWithoutValidation(k, v);

            // 1) ¿Ya está en el repositorio de Saludsa?
            var enRepo = await LlamarAsync(cli, HttpMethod.Get,
                $"{baseUrl}/api/Documento/ObtenerFactura?claveAcceso={Uri.EscapeDataString(clave)}", ct);
            if (enRepo.Ok)
                return new ResultadoSri { ClaveAcceso = clave, Estado = EstadoSri.Encontrada };

            // 2) No está: que el servicio la traiga del SRI y la guarde.
            //
            //    OJO: este endpoint contesta HTTP 200 con Estado "Error" cuando el
            //    SRI no devuelve el documento. Mirar solo el codigo HTTP diria que
            //    fue bien. Por eso se mira el Estado del cuerpo, no el 200.
            var delSri = await LlamarAsync(cli, HttpMethod.Post,
                $"{baseUrl}/api/Sri?claveAcceso={Uri.EscapeDataString(clave)}", ct);

            if (delSri.Ok)
                return new ResultadoSri { ClaveAcceso = clave, Estado = EstadoSri.Encontrada };

            // Distinguir "el SRI dice que no" de "no se pudo preguntar". Si no
            // hubo respuesta legible del servicio, es lo segundo.
            return new ResultadoSri
            {
                ClaveAcceso = clave,
                Estado = delSri.HuboRespuesta ? EstadoSri.NoEncontrada : EstadoSri.NoSePudoComprobar,
                Detalle = delSri.Mensaje
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[SRI] No se pudo comprobar la clave {Clave}.", clave);
            return new ResultadoSri
            {
                ClaveAcceso = clave, Estado = EstadoSri.NoSePudoComprobar, Detalle = ex.Message
            };
        }
    }

    /// <summary>
    /// Cabeceras para llamar al repositorio. Si hay credenciales propias del SRI
    /// se usan esas —porque van a otro ambiente— y si no, las de siempre.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> CabecerasAsync(CancellationToken ct)
    {
        var tokenUrl = _config["Saludsa:Sri:Auth:TokenUrl"];
        var usuario  = _config["Saludsa:Sri:Auth:Username"];
        var clave    = _config["Saludsa:Sri:Auth:Password"];
        var cliente  = _config["Saludsa:Sri:Auth:ClientId"];

        if (string.IsNullOrWhiteSpace(tokenUrl) || string.IsNullOrWhiteSpace(usuario)
            || string.IsNullOrWhiteSpace(clave))
            return await _token.GetAuthHeadersAsync(ct);

        var cli = _http.CreateClient();
        cli.Timeout = TimeSpan.FromSeconds(20);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"]  = cliente ?? string.Empty,
            ["username"]   = usuario,
            ["password"]   = clave
        });

        using var resp = await cli.PostAsync(tokenUrl, form, ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var acceso = doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;

        if (string.IsNullOrWhiteSpace(acceso))
            return await _token.GetAuthHeadersAsync(ct);

        return new Dictionary<string, string>
        {
            ["Authorization"]        = "Bearer " + acceso,
            ["CodigoAplicacion"]     = _config["Saludsa:Auth:CodigoAplicacion"]     ?? "3",
            ["CodigoPlataforma"]     = _config["Saludsa:Auth:CodigoPlataforma"]     ?? "7",
            ["SistemaOperativo"]     = _config["Saludsa:Auth:SistemaOperativo"]     ?? "Windows",
            ["DispositivoNavegador"] = _config["Saludsa:Auth:DispositivoNavegador"] ?? "DeveloperAI-Nexus",
            ["DireccionIP"]          = _config["Saludsa:Auth:DireccionIP"]          ?? "10.12.10.142"
        };
    }

    /// <summary>
    /// ¿Apunta de verdad a producción? Sin esto, un "no existe en el SRI" no
    /// significa nada y no puede bloquear a nadie.
    /// </summary>
    public bool ApuntaAProduccion =>
        !string.IsNullOrWhiteSpace(_config["Saludsa:Sri:BaseUrl"])
        && !(_config["Saludsa:Sri:BaseUrl"] ?? string.Empty)
             .Contains("pruebas", StringComparison.OrdinalIgnoreCase);

    private sealed record Respuesta(bool Ok, bool HuboRespuesta, string? Mensaje);

    private async Task<Respuesta> LlamarAsync(HttpClient cli, HttpMethod metodo, string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(metodo, url);
            using var resp = await cli.SendAsync(req, ct);
            var cuerpo = await resp.Content.ReadAsStringAsync(ct);

            if (string.IsNullOrWhiteSpace(cuerpo)) return new Respuesta(false, false, null);

            using var doc = JsonDocument.Parse(cuerpo);
            var estado = doc.RootElement.TryGetProperty("Estado", out var e) ? e.GetString() : null;
            var mensaje = doc.RootElement.TryGetProperty("Mensajes", out var m)
                          && m.ValueKind == JsonValueKind.Array && m.GetArrayLength() > 0
                          ? m[0].GetString() : null;

            var ok = string.Equals(estado, "OK", StringComparison.OrdinalIgnoreCase);
            return new Respuesta(ok, estado != null, mensaje);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new Respuesta(false, false, ex.Message);
        }
    }
}
