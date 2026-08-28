using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace app_ocr_ai_models.Services.Ai;

// =============================================================================
// REQ-019r — Homologación del texto de la factura contra el catálogo REAL de
//            procedimientos, y validación de la correlación diagnóstico ↔ proc.
//
//   Salud.dbo.Lr05Procedimientos              37.448  catálogo maestro
//   Salud.dbo.Lr46CorrelacionDXProcedimiento 583.590  correlación dx ↔ proc.
//
// Esto NO se le pregunta al modelo: es un lookup y una regla. El modelo extrae
// el texto del ítem; el código lo homologa y valida contra la base.
//
// El algoritmo de emparejamiento está replicado y probado en
// tools/nexus_probar_homologacion_lr05.py, contra la base real y con casos de
// respuesta esperada. Si se toca aquí, se toca allá y se vuelve a correr.
// =============================================================================

/// <summary>Un procedimiento del catálogo Lr05 con sus raíces ya calculadas.</summary>
public sealed record ProcedimientoLr05(
    int NumeroProcedimiento,
    int? CodigoHarvard,
    string? CodigoBeneficio,
    string NombreEspanol,
    List<string> Raices);

/// <summary>Resultado de homologar el texto de un ítem.</summary>
public sealed class HomologacionResultado
{
    public int NumeroProcedimiento { get; init; }

    /// <summary>
    /// El codigo que la liquidacion escribe de verdad en Lr04DetalleReclamo.
    /// OJO: <c>CodigoProcedimiento</c> del detalle del reclamo NO es
    /// <see cref="NumeroProcedimiento"/> -que es la fila de Lr05- sino este
    /// CodigoHarvard. Verificado: la fila 7044 tiene Harvard 504001, y es el
    /// 504001 el que aparece en el reclamo.
    /// </summary>
    public int? CodigoHarvard { get; init; }
    public string? NombreLr05 { get; init; }
    public string? CodigoBeneficio { get; init; }
    public bool EsMedicina { get; init; }

    /// <summary>
    /// MARCA | GENERICA | null. Lo dice el propio catálogo por beneficio:
    /// A010 = medicina de marca, A011 = medicina genérica. No hay que
    /// deducirlo del nombre del producto.
    /// </summary>
    public string? TipoMedicina { get; init; }

    public decimal Score { get; init; }
    /// <summary>Hubo empate con otro procedimiento distinto: requiere ojo humano.</summary>
    public bool Ambigua { get; init; }
}

/// <summary>Resultado de validar la correlación contra Lr46.</summary>
public sealed class CorrelacionResultado
{
    /// <summary>CORRELACIONA | NO_CORRELACIONA | SIN_VALIDAR.</summary>
    public string Estado { get; init; } = "SIN_VALIDAR";
    public int? Probabilidad { get; init; }
    public string? DiagnosticoUsado { get; init; }
    public bool? Confirmada { get; init; }
    public int UmbralAplicado { get; init; }
    /// <summary>Frase lista para el informe, con el matiz correcto de cada estado.</summary>
    public string Explicacion { get; init; } = string.Empty;
}

public interface IHomologadorProcedimientos
{
    Task<HomologacionResultado?> HomologarAsync(string? textoItem, CancellationToken ct = default);

    Task<CorrelacionResultado> ValidarCorrelacionAsync(
        int numeroProcedimiento, string? codigoBeneficio, IEnumerable<string?> diagnosticos,
        CancellationToken ct = default);
}

public sealed class HomologadorProcedimientos : IHomologadorProcedimientos
{
    private const string ConnName = "SaludProcedimientos";
    private const string CacheKeyCatalogo = "lr05:catalogo";

    /// <summary>
    /// Cuánto se recuerda que el catálogo no se pudo cargar. Corto: sólo tiene
    /// que cubrir la ráfaga de una misma petición, no dejar la homologación
    /// apagada si el enlace vuelve.
    /// </summary>
    private const int SegundosRecordandoElFallo = 45;
    private const string CacheKeyUmbrales = "lr46:umbrales";

    // Mismos parámetros que el banco de pruebas en Python
    private const int LargoRaiz = 8;      // COLONOSCOPICO / COLONOSCOPIA -> COLONOSC
    private const int MinToken = 5;
    private const int MinRaizUnica = 7;

    private static readonly HashSet<string> PalabrasVacias = new(StringComparer.Ordinal)
    {
        "DE","DEL","LA","EL","LOS","LAS","CON","SIN","POR","PARA","EN","Y","O","A",
        "UN","UNA","SU","AL","MAS","QUE","SE","ES","OTRAS","OTROS","TOTAL",
        "INCLUYE","USO","EQUIPO","DERECHO","CODIGO","NIVEL","TIPO","ESTUDIO",
        "PROCEDIMIENTO","INFORME","PACIENTE","SEGUN","CADA"
    };

    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HomologadorProcedimientos> _logger;

    public HomologadorProcedimientos(IConfiguration config, IMemoryCache cache,
                                     ILogger<HomologadorProcedimientos> logger)
    {
        _config = config;
        _cache = cache;
        _logger = logger;
    }

    // ── Homologación ────────────────────────────────────────────────────────

    public async Task<HomologacionResultado?> HomologarAsync(string? textoItem, CancellationToken ct = default)
    {
        var rd = Raices(textoItem);
        if (rd.Count == 0) return null;

        var catalogo = await CatalogoAsync(ct);
        if (catalogo.Count == 0) return null;

        var umbrales = await UmbralesAsync(ct);
        var primera = rd[0];

        (double F1, int Comunes, int Tamano, ProcedimientoLr05 P)? mejor = null, segundo = null;

        foreach (var p in catalogo)
        {
            var comunes = 0;
            var tieneprimera = false;
            foreach (var r in rd)
            {
                if (!p.Raices.Contains(r, StringComparer.Ordinal)) continue;
                comunes++;
                if (r == primera) tieneprimera = true;
            }
            if (comunes == 0) continue;

            // Con una sola raíz en común tiene que ser la PRIMERA de la descripción
            // (el núcleo). Sin esto, "…CON INTUBACION" ganaba por "INTUBACION" y
            // "ESTUDIO COLONOSCOPICO COMPLETO" caía en el procedimiento "Completo.".
            if (comunes == 1 && (!tieneprimera || primera.Length < MinRaizUnica)) continue;

            // F1 entre lo que se explicó de la descripción y lo que se usó del
            // nombre del catálogo. Con una sola cobertura, el algoritmo se iba a
            // los nombres de una palabra.
            var recall = (double)comunes / rd.Count;
            var precision = (double)comunes / Math.Max(1, p.Raices.Count);
            var f1 = (recall + precision) == 0 ? 0 : 2 * recall * precision / (recall + precision);

            var cand = (F1: Math.Round(f1, 4), Comunes: comunes, Tamano: p.Raices.Count, P: p);

            if (mejor == null || EsMejor(cand, mejor.Value)) { segundo = mejor; mejor = cand; }
            else if (segundo == null || EsMejor(cand, segundo.Value)) { segundo = cand; }
        }

        if (mejor == null) return null;

        var m = mejor.Value;
        var ambigua = segundo != null
                      && Math.Abs(segundo.Value.F1 - m.F1) < 0.0001
                      && segundo.Value.Comunes == m.Comunes
                      && !string.Equals(segundo.Value.P.NombreEspanol, m.P.NombreEspanol,
                                        StringComparison.OrdinalIgnoreCase);

        var ben = (m.P.CodigoBeneficio ?? string.Empty).Trim().ToUpperInvariant();
        var hayCfg = umbrales.TryGetValue(ben, out var cfg);
        var esMedicina = hayCfg && cfg.EsMedicina;
        var tipoMedicina = hayCfg ? cfg.Tipo : null;

        return new HomologacionResultado
        {
            NumeroProcedimiento = m.P.NumeroProcedimiento,
            CodigoHarvard = m.P.CodigoHarvard,
            NombreLr05 = m.P.NombreEspanol,
            CodigoBeneficio = m.P.CodigoBeneficio,
            EsMedicina = esMedicina,
            TipoMedicina = tipoMedicina,
            Score = (decimal)m.F1,
            Ambigua = ambigua
        };
    }

    private static bool EsMejor((double F1, int Comunes, int Tamano, ProcedimientoLr05 P) a,
                               (double F1, int Comunes, int Tamano, ProcedimientoLr05 P) b)
    {
        if (Math.Abs(a.F1 - b.F1) > 0.0001) return a.F1 > b.F1;
        if (a.Comunes != b.Comunes) return a.Comunes > b.Comunes;
        if (a.Tamano != b.Tamano) return a.Tamano < b.Tamano;                 // más genérico
        return a.P.NumeroProcedimiento < b.P.NumeroProcedimiento;             // determinismo
    }

    // ── Correlación ─────────────────────────────────────────────────────────

    public async Task<CorrelacionResultado> ValidarCorrelacionAsync(
        int numeroProcedimiento, string? codigoBeneficio, IEnumerable<string?> diagnosticos,
        CancellationToken ct = default)
    {
        var umbrales = await UmbralesAsync(ct);
        var ben = (codigoBeneficio ?? string.Empty).Trim().ToUpperInvariant();
        var cfg = umbrales.TryGetValue(ben, out var c) ? c
                : umbrales.TryGetValue("DEFAULT", out var d) ? d
                : (EsMedicina: false, Umbral: 1, Tipo: (string?)null);
        var familia = cfg.EsMedicina
            ? "medicina" + (string.IsNullOrWhiteSpace(cfg.Tipo) ? "" : " " + cfg.Tipo!.ToLowerInvariant())
            : "procedimiento";

        await using var cn = Abrir();
        if (cn == null)
        {
            return new CorrelacionResultado
            {
                Estado = "SIN_VALIDAR", UmbralAplicado = cfg.Umbral,
                Explicacion = "No se pudo consultar Lr46 (conexión SaludProcedimientos no disponible)."
            };
        }
        await cn.OpenAsync(ct);

        // ¿El procedimiento existe en Lr46 bajo ALGÚN diagnóstico? Es lo que separa
        // "no correlaciona" de "no hay con qué validar" — y al 88% de los
        // procedimientos del catálogo simplemente no se les puede validar.
        await using (var cmdExiste = cn.CreateCommand())
        {
            cmdExiste.CommandText =
                "SELECT COUNT(*) FROM Salud.dbo.Lr46CorrelacionDXProcedimiento WITH (NOLOCK) " +
                "WHERE NumeroProcedimiento = @num";
            cmdExiste.Parameters.Add("@num", SqlDbType.Int).Value = numeroProcedimiento;
            var total = Convert.ToInt32(await cmdExiste.ExecuteScalarAsync(ct) ?? 0);
            if (total == 0)
            {
                return new CorrelacionResultado
                {
                    Estado = "SIN_VALIDAR", UmbralAplicado = cfg.Umbral,
                    Explicacion = $"El procedimiento {numeroProcedimiento} no está registrado en la " +
                                  "correlación Lr46 bajo ningún diagnóstico: no hay con qué validar. " +
                                  "No es una negativa."
                };
            }
        }

        var claves = VariantesDx(diagnosticos).ToList();
        if (claves.Count == 0)
        {
            return new CorrelacionResultado
            {
                Estado = "SIN_VALIDAR", UmbralAplicado = cfg.Umbral,
                Explicacion = "El sobre no trae diagnóstico CIE-10 legible: no se puede validar la correlación."
            };
        }

        var nombres = claves.Select((_, i) => "@d" + i).ToList();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            "SELECT TOP 1 CodigoDiagnostico, ISNULL(Probabilidad, 0), ISNULL(CONVERT(int, EsConfirmado), 0) " +
            "FROM Salud.dbo.Lr46CorrelacionDXProcedimiento WITH (NOLOCK) " +
            "WHERE NumeroProcedimiento = @num AND ISNULL(Activo, 1) = 1 " +
            $"AND CodigoDiagnostico IN ({string.Join(",", nombres)}) " +
            "ORDER BY Probabilidad DESC";
        cmd.Parameters.Add("@num", SqlDbType.Int).Value = numeroProcedimiento;
        for (var i = 0; i < claves.Count; i++)
            cmd.Parameters.Add(nombres[i], SqlDbType.VarChar, 10).Value = claves[i];

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
        {
            return new CorrelacionResultado
            {
                Estado = "NO_CORRELACIONA", UmbralAplicado = cfg.Umbral,
                Explicacion = $"El procedimiento {numeroProcedimiento} está en Lr46 con otros " +
                              $"diagnósticos, pero no con {string.Join("/", claves)}."
            };
        }

        var dxUsado = rd.GetString(0);
        var prob = rd.GetInt32(1);
        var confirmada = rd.GetInt32(2) == 1;
        var correlaciona = prob >= cfg.Umbral;

        return new CorrelacionResultado
        {
            Estado = correlaciona ? "CORRELACIONA" : "NO_CORRELACIONA",
            Probabilidad = prob,
            DiagnosticoUsado = dxUsado,
            Confirmada = confirmada,
            UmbralAplicado = cfg.Umbral,
            Explicacion = correlaciona
                ? $"Correlaciona con el diagnóstico {dxUsado} al {prob}% " +
                  $"(umbral {familia}: {cfg.Umbral}%){(confirmada ? ", correlación confirmada" : "")}."
                : $"Registrado para {dxUsado} pero con {prob}%, bajo el umbral de {familia} " +
                  $"({cfg.Umbral}%)."
        };
    }

    // ── Datos ───────────────────────────────────────────────────────────────

    private SqlConnection? Abrir()
    {
        var cs = _config.GetConnectionString(ConnName);
        if (string.IsNullOrWhiteSpace(cs))
        {
            _logger.LogWarning("No hay cadena de conexión '{Conn}': no se puede homologar contra Lr05.", ConnName);
            return null;
        }
        return new SqlConnection(cs);
    }

    /// <summary>
    /// El catálogo completo en memoria. Son 37k filas de texto corto: cabe de
    /// sobra y filtrar en memoria es mucho mejor que 37k LIKE por ítem. Se
    /// refresca cada 6 horas (Lr05 se edita, pero no cada minuto).
    /// </summary>
    private async Task<List<ProcedimientoLr05>> CatalogoAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKeyCatalogo, out List<ProcedimientoLr05>? cacheado) && cacheado != null)
            return cacheado;

        var lista = new List<ProcedimientoLr05>(40000);
        await using var cn = Abrir();
        if (cn == null) return lista;

        try
        {
            await cn.OpenAsync(ct);
            await using var cmd = cn.CreateCommand();
            cmd.CommandText =
                "SELECT NumeroProcedimiento, CodigoBeneficio, NombreEspanol, CodigoHarvard " +
                "FROM Salud.dbo.Lr05Procedimientos WITH (NOLOCK) " +
                "WHERE NombreEspanol IS NOT NULL AND LEN(LTRIM(RTRIM(NombreEspanol))) > 2";
            cmd.CommandTimeout = 60;
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var nombre = rd.GetString(2);
                lista.Add(new ProcedimientoLr05(
                    rd.GetInt32(0),
                    rd.IsDBNull(3) ? null : rd.GetInt32(3),
                    rd.IsDBNull(1) ? null : rd.GetString(1),
                    nombre,
                    Raices(nombre)));
            }
            _logger.LogInformation("Catálogo Lr05 cargado: {N} procedimientos.", lista.Count);
        }
        catch (Exception ex)
        {
            // El fallo TAMBIÉN se cachea, aunque sea poco rato.
            //
            // Antes este `return` salía sin pasar por el _cache.Set de abajo, así
            // que un catálogo inalcanzable no se recordaba y se reintentaba la
            // conexión ENTERA en cada llamada. Y HomologarAsync se llama una vez
            // POR PROCEDIMIENTO dentro de un bucle
            // (ClasificacionController.Generar), con Connect Timeout=30 en la
            // cadena de SQLMIGRACION: con la VPN caída, una factura de seis
            // líneas se comía TRES MINUTOS esperando seis veces a la misma
            // máquina muerta, en silencio y sin un solo error. Eso es lo que el
            // afiliado ve como "se quedó congelado".
            //
            // La ventana es corta a propósito: cubre la ráfaga de una petición
            // sin dejar la homologación apagada si el enlace vuelve enseguida.
            // Durante esos segundos se devuelve lo mismo que se devolvería de
            // todas formas —nada—, pero al instante en vez de en medio minuto.
            _logger.LogWarning(ex, "No se pudo cargar el catálogo Lr05. "
                                 + "Se recuerda el fallo {Seg}s para no reintentar por cada ítem.",
                                 SegundosRecordandoElFallo);
            _cache.Set(CacheKeyCatalogo, lista,
                       TimeSpan.FromSeconds(SegundosRecordandoElFallo));
            return lista;
        }

        _cache.Set(CacheKeyCatalogo, lista, TimeSpan.FromHours(6));
        return lista;
    }

    private async Task<Dictionary<string, (bool EsMedicina, int Umbral, string? Tipo)>> UmbralesAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKeyUmbrales, out Dictionary<string, (bool, int, string?)>? c) && c != null)
            return c!;

        var mapa = new Dictionary<string, (bool EsMedicina, int Umbral, string? Tipo)>(StringComparer.OrdinalIgnoreCase)
        {
            // Respaldo si la tabla no está: A010 marca y A011 genérica son las dos
            // medicina y llevan la regla de farmacia (70); el resto, presencia.
            ["A010"] = (true, 70, "MARCA"),
            ["A011"] = (true, 70, "GENERICA"),
            ["DEFAULT"] = (false, 1, null)
        };

        var cs = _config.GetConnectionString("OcrAiConnection") ?? _config.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(cs))
        {
            try
            {
                await using var cn = new SqlConnection(cs);
                await cn.OpenAsync(ct);
                await using var cmd = cn.CreateCommand();
                cmd.CommandText = "SELECT CodigoBeneficio, EsMedicina, UmbralProbabilidad, TipoMedicina " +
                                  "FROM dbo.CatalogoBeneficioCorrelacion WHERE IsActive = 1";
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                var leidos = new Dictionary<string, (bool, int, string?)>(StringComparer.OrdinalIgnoreCase);
                while (await rd.ReadAsync(ct))
                    leidos[rd.GetString(0)] = (rd.GetBoolean(1), rd.GetInt32(2),
                                               rd.IsDBNull(3) ? null : rd.GetString(3));
                if (leidos.Count > 0)
                    mapa = leidos.ToDictionary(k => k.Key,
                                               v => (v.Value.Item1, v.Value.Item2, v.Value.Item3),
                                               StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudieron leer los umbrales de correlación; se usan los de respaldo.");
            }
        }

        _cache.Set(CacheKeyUmbrales, mapa, TimeSpan.FromMinutes(30));
        return mapa;
    }

    // ── Utilidades de texto ─────────────────────────────────────────────────

    /// <summary>
    /// Raíces significativas EN ORDEN: sin tildes, mayúsculas, sin puntuación,
    /// sin muletillas y truncadas a 8 caracteres. La primera es el núcleo.
    /// </summary>
    private static List<string> Raices(string? texto)
    {
        var sinTildes = new string((texto ?? string.Empty)
            .Normalize(NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .ToArray()).ToUpperInvariant();

        var lista = new List<string>();
        foreach (var w in Regex.Split(sinTildes, @"[^A-Z0-9]+"))
        {
            if (w.Length < MinToken || PalabrasVacias.Contains(w)) continue;
            var r = w.Length > LargoRaiz ? w[..LargoRaiz] : w;
            if (!lista.Contains(r, StringComparer.Ordinal)) lista.Add(r);
        }
        return lista;
    }

    /// <summary>
    /// K63.5 → K635 y K63. En Lr46 el CIE-10 va sin punto y a menudo truncado a
    /// tres caracteres (K58.0 está como 'K58'): hay que buscar las dos formas.
    /// </summary>
    private static IEnumerable<string> VariantesDx(IEnumerable<string?> diagnosticos)
    {
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dx in diagnosticos ?? Enumerable.Empty<string?>())
        {
            var d = Regex.Replace((dx ?? string.Empty).ToUpperInvariant(), "[^A-Z0-9]", "");
            if (d.Length == 0) continue;
            if (vistos.Add(d)) yield return d;
            if (d.Length > 3 && vistos.Add(d[..3])) yield return d[..3];
        }
    }
}
