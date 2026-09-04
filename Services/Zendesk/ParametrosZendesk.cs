using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;

namespace app_tramites.Services.Zendesk;

/// <summary>
/// Las credenciales de Zendesk, leídas de donde de verdad viven: la tabla de
/// parámetros <c>Saludsa.Administracion.ParametroServicioWeb</c>, servicio
/// <c>WebApiComunicaciones</c>. Es lo que hace api-comunicacion
/// (<c>ProxyZendesk.cs</c>: <c>DbConfig.ObtenerValorParametro(...)</c>).
///
/// <para><b>Por qué no se copian a appsettings.</b> Ahí se pueden cambiar sin
/// desplegar nada, y ya hay un sitio donde son verdad. Copiarlas crearía una
/// segunda verdad que se desincroniza en silencio el día que alguien rote el
/// token.</para>
///
/// <para><b>Dos trampas comprobadas</b>, y las dos hacen perder tiempo porque
/// mienten sobre la causa:</para>
/// <list type="number">
///   <item><b>Hay DOS instancias de Zendesk.</b> Los tickets de reembolso viven
///   en <c>servicioexperience1562940791</c>; la otra
///   (<c>…1692030147</c>) es la de los demás servicios. Preguntar por un ticket
///   de reembolso en la instancia equivocada devuelve <b>404</b>, que se lee
///   como «ese ticket no existe» cuando existe perfectamente.</item>
///   <item><b>Los nombres vienen emparejados</b>: <c>TokenZenDesk&lt;X&gt;</c>
///   con <c>UriZenDesk&lt;X&gt;</c>. Mezclarlos da 404 o 401.</item>
/// </list>
///
/// <para>Y el par <c>ZendeskUserName</c>/<c>ZendeskAPIKey</c> NO sirve para
/// leer: autentica pero devuelve <b>403 Forbidden</b>. En Zendesk el permiso lo
/// da el correo del usuario, no la clave.</para>
/// </summary>
public sealed class ParametrosZendesk
{
    private readonly IConfiguration _config;
    private readonly ILogger<ParametrosZendesk> _logger;

    /// <summary>
    /// Se cachean en memoria: son dos filas que no cambian entre peticiones, y
    /// releerlas en cada llamada añadiría un viaje a la base por cada consulta
    /// de ticket. Se cachea solo lo que salió bien.
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> _cache = new();

    public ParametrosZendesk(IConfiguration config, ILogger<ParametrosZendesk> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// El sufijo del par de parámetros a usar. El de los sobres de reembolso es
    /// éste; se deja como constante con nombre para que no aparezca escrito a
    /// mano en la configuración de cada tool.
    /// </summary>
    public const string Reembolso = "ServicioExperienceReembolsoElectronico";

    /// <summary>Token y raíz del servicio, para el par pedido.</summary>
    public async Task<(string Token, string Raiz)> ObtenerAsync(
        string sufijo, CancellationToken ct = default)
    {
        var token = await ValorAsync("TokenZenDesk" + sufijo, ct);
        var uri   = await ValorAsync("UriZenDesk" + sufijo, ct);

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(uri))
            throw new InvalidOperationException(
                $"[Zendesk] Faltan los parámetros 'TokenZenDesk{sufijo}' o 'UriZenDesk{sufijo}' "
                + "en Saludsa.Administracion.ParametroServicioWeb.");

        // La URI del parámetro trae el path completo (…/api/v2/tickets). Aquí se
        // quiere solo la raíz: cada tool añade el suyo. Sin recortar salía la
        // ruta duplicada y el servicio devolvía 404 "InvalidEndpoint" — que
        // parece un ticket inexistente y es una URL mal armada.
        var raiz = new Uri(uri!).GetLeftPart(UriPartial.Authority);
        return (token!, raiz);
    }

    private async Task<string?> ValorAsync(string codigo, CancellationToken ct)
    {
        if (_cache.TryGetValue(codigo, out var guardado)) return guardado;

        var cs = _config.GetConnectionString("SaludConsultas");
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                "[Zendesk] No hay cadena 'SaludConsultas' para leer los parámetros.");

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);

        // La credencial entra por bdd_Salud_Consultas pero la tabla vive en la
        // base Saludsa: hay que nombrarla con las tres partes.
        await using var cmd = new SqlCommand(
            "SELECT TOP 1 CONVERT(nvarchar(max), Valor) "
            + "FROM Saludsa.Administracion.ParametroServicioWeb WITH (NOLOCK) "
            + "WHERE Codigo = @codigo", cn) { CommandTimeout = 30 };
        cmd.Parameters.Add(new SqlParameter("@codigo", System.Data.SqlDbType.VarChar, 200)
        { Value = codigo });

        var valor = (await cmd.ExecuteScalarAsync(ct)) as string;
        if (!string.IsNullOrWhiteSpace(valor))
        {
            _cache[codigo] = valor!;
            _logger.LogInformation("[Zendesk] Parámetro '{Codigo}' leído de la base.", codigo);
        }
        return valor;
    }
}
