using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace app_ocr_ai_models.Services;

// =============================================================================
// REQ-027 — El modelo extrae hechos. Las cifras se calculan.
//
// El reparto que faltaba, y que es el que ordena todo lo demás:
//
//     el agente ......... saca los códigos, busca los diagnósticos y estructura
//                         los documentos. Eso hay que INTERPRETARLO.
//     el código ......... con esos hechos, mira qué dice el plan y arma la
//                         explicación. Eso NO se interpreta: se consulta y se
//                         calcula.
//
// Hasta hoy el porcentaje salía del JSON del modelo (ClienteController:906) y
// nadie lo contrastaba. Por eso un caso pudo decir que de $478,08 se cubrían
// $478,08 —el 100%— sin que chirriara nada: era prosa con formato de número.
//
// -- Lo que esto SÍ calcula --------------------------------------------------
// Lo que el plan dice, por ítem: qué beneficio le corresponde, qué porcentaje
// tiene ese beneficio en ESE plan y esa versión, y qué topes lleva. Todo sale de
// Pr05Beneficios, no de una frase.
//
// -- Lo que esto NO calcula, y no va a calcular ------------------------------
// El importe final. El arancel, el deducible acumulado y el copago los resuelve
// api-liquidaciones con diez ramas que no se replican aquí: sería un segundo
// motor de dinero y el día que discrepen nadie sabrá cuál miente.
//
// Por eso al afiliado se le habla de «lo que su plan cubre», que es verdad y es
// verificable, y no de «lo que le vamos a pagar», que todavía no se sabe.
// =============================================================================

/// <summary>Lo que el plan dice de un gasto concreto.</summary>
public sealed class GastoEvaluado
{
    public string Descripcion { get; init; } = string.Empty;
    public decimal ValorPresentado { get; init; }

    public string? CodigoBeneficio { get; init; }
    public string? NombreBeneficio { get; init; }
    public int? NumeroProcedimiento { get; init; }
    public string? NombreProcedimiento { get; init; }

    /// <summary>Lo que dice el plan. null cuando no se pudo determinar.</summary>
    public decimal? PorcentajeDelPlan { get; init; }

    public decimal? TopePorPrestacion { get; init; }
    public bool AplicaDeducible { get; init; }

    /// <summary>La homologación empató: el beneficio no es seguro.</summary>
    public bool Ambigua { get; init; }

    /// <summary>Por qué no hay porcentaje, cuando no lo hay.</summary>
    public string? PorQueNoSeSabe { get; init; }

    /// <summary>
    /// Lo que se le dice al afiliado de este gasto. Sale de los datos, no de una
    /// frase del modelo, y por eso no puede contradecirlos.
    /// </summary>
    public string ParaElCliente
    {
        get
        {
            if (Ambigua)
                return "Estamos confirmando exactamente qué prestación es, para aplicarle "
                     + "la cobertura que le corresponde.";

            if (PorcentajeDelPlan is null)
                return string.IsNullOrWhiteSpace(PorQueNoSeSabe)
                    ? "Lo está revisando un especialista."
                    : PorQueNoSeSabe!;

            if (PorcentajeDelPlan == 0)
                return $"Su plan no cubre {Familia()} por esta vía.";

            var pct = PorcentajeDelPlan.Value.ToString("0.##", CultureInfo.GetCultureInfo("es-EC"));
            var frase = $"Su plan cubre el {pct}% de {Familia()}.";

            if (TopePorPrestacion is > 0 and < 999999)
                frase += " Con un tope de "
                       + TopePorPrestacion.Value.ToString("N2", CultureInfo.GetCultureInfo("es-EC"))
                       + " por prestación.";

            if (AplicaDeducible)
                frase += " Este gasto va contra su deducible.";

            return frase;
        }
    }

    /// <summary>
    /// «laboratorio clínico», no «A003» y tampoco «laboratorio clinico».
    ///
    /// El catálogo Pr07CatalogoBeneficios guarda los nombres en mayúsculas y SIN
    /// TILDES —«LABORATORIO CLINICO», «CONSULTA MEDICA»—, que es normal en un
    /// maestro de sistemas pero se ve descuidado en la pantalla de una persona.
    /// Aquí se acentúan los que de verdad aparecen; el resto sale del catálogo
    /// tal cual, en minúscula, que sigue siendo mejor que un código.
    /// </summary>
    private static readonly Dictionary<string, string> ConTilde = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LABORATORIO CLINICO"]        = "laboratorio clínico",
        ["LABORATORIO IMAGEN"]         = "estudios de imagen",
        ["CONSULTA MEDICA"]            = "consulta médica",
        ["PROCEDIMIENTOS DIAGNOSTICO"] = "procedimientos diagnósticos",
        ["HONORARIOS MEDICOS"]         = "honorarios médicos",
        ["SERVICIOS HOSPITALARIOS"]    = "servicios hospitalarios",
        ["MEDICINAS"]                  = "medicinas",
        ["PROTESIS NO DENTALES"]       = "prótesis",
        ["TERAPIA DE REHABILITACION"]  = "terapia de rehabilitación",
    };

    private string Familia()
    {
        var n = (NombreBeneficio ?? string.Empty).Trim();
        if (n.Length == 0) return "esta prestación";
        return ConTilde.TryGetValue(n, out var bonito) ? bonito : n.ToLowerInvariant();
    }
}

public interface IEvaluadorDeCobertura
{
    /// <summary>
    /// Qué dice el plan de cada gasto. Una sola ida a la base para todos los
    /// beneficios del sobre.
    /// </summary>
    Task<IReadOnlyList<GastoEvaluado>> EvaluarAsync(
        IEnumerable<(string Descripcion, decimal Valor, string? CodigoBeneficio,
                     int? NumeroProcedimiento, string? NombreLr05, bool Ambigua)> gastos,
        string? codigoPlan, int? versionPlan, string? codigoProducto,
        CancellationToken ct = default);
}

public sealed class EvaluadorDeCobertura : IEvaluadorDeCobertura
{
    private const string ConnName = "SaludReclamos";

    // Un solo viaje para todos los beneficios del sobre. Los parametros van como
    // VARCHAR a proposito: contra columnas varchar, un nvarchar obliga a SQL
    // Server a convertir LA COLUMNA y el indice deja de servir.
    private const string Consulta = @"
SELECT b.CodigoBeneficio,
       MAX(cb.NombreBeneficio)                        AS NombreBeneficio,
       MIN(b.PorcentajeSinConvenio)                   AS PctMin,
       MAX(b.PorcentajeSinConvenio)                   AS PctMax,
       MAX(b.MontoPorPrestacion)                      AS Tope,
       MAX(CASE WHEN b.AplicaDeducible = 1 THEN 1 ELSE 0 END) AS AplicaDeducible
  FROM Salud.dbo.Pr05Beneficios b WITH (NOLOCK)
  LEFT JOIN Salud.dbo.Pr07CatalogoBeneficios cb WITH (NOLOCK)
         ON cb.CodigoBeneficio = b.CodigoBeneficio
 WHERE b.CodigoPlan     = @codigoPlan
   AND b.VersionPlan    = @versionPlan
   AND b.CodigoProducto = @codigoProducto
 GROUP BY b.CodigoBeneficio";

    private readonly IConfiguration _config;
    private readonly ILogger<EvaluadorDeCobertura> _log;

    public EvaluadorDeCobertura(IConfiguration config, ILogger<EvaluadorDeCobertura> log)
    {
        _config = config; _log = log;
    }

    public async Task<IReadOnlyList<GastoEvaluado>> EvaluarAsync(
        IEnumerable<(string Descripcion, decimal Valor, string? CodigoBeneficio,
                     int? NumeroProcedimiento, string? NombreLr05, bool Ambigua)> gastos,
        string? codigoPlan, int? versionPlan, string? codigoProducto,
        CancellationToken ct = default)
    {
        var lista = gastos.ToList();
        if (lista.Count == 0) return Array.Empty<GastoEvaluado>();

        // Sin la llave del plan no se puede decir nada: NO se inventa un
        // porcentaje ni se calla, se dice por qué.
        if (string.IsNullOrWhiteSpace(codigoPlan) || versionPlan is null
            || string.IsNullOrWhiteSpace(codigoProducto))
        {
            return lista.Select(g => Sin(g, "Estamos confirmando los datos de su plan.")).ToList();
        }

        Dictionary<string, (string? Nombre, decimal? PctMin, decimal? PctMax, decimal? Tope, bool Ded)> plan;

        try
        {
            plan = await LeerPlanAsync(codigoPlan!, versionPlan.Value, codigoProducto!, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Que no se pueda consultar no puede convertirse en un 0%: eso le
            // diria al afiliado que no tiene cobertura cuando si la tiene.
            _log.LogWarning(ex, "[Cobertura] No se pudo leer el plan {Plan} v{Ver}.", codigoPlan, versionPlan);
            return lista.Select(g => Sin(g, "Lo está revisando un especialista.")).ToList();
        }

        return lista.Select(g =>
        {
            var ben = (g.CodigoBeneficio ?? string.Empty).Trim().ToUpperInvariant();

            if (g.Ambigua)
                return new GastoEvaluado
                {
                    Descripcion = g.Descripcion, ValorPresentado = g.Valor,
                    CodigoBeneficio = g.CodigoBeneficio, NumeroProcedimiento = g.NumeroProcedimiento,
                    NombreProcedimiento = g.NombreLr05, Ambigua = true
                };

            if (ben.Length == 0)
                return Sin(g, "Estamos identificando qué prestación es.");

            if (!plan.TryGetValue(ben, out var fila))
                return Sin(g, "Lo está revisando un especialista.");

            // Si el mismo beneficio tiene porcentajes distintos segun la
            // cobertura -medido: pasa en 1 de cada 3 llaves-, aqui NO se elige
            // uno. Decirle al afiliado uno de varios posibles, al azar, es peor
            // que decirle que lo estamos mirando.
            var seguro = fila.PctMin.HasValue && fila.PctMax.HasValue
                         && fila.PctMin == fila.PctMax;

            if (!seguro)
                return Sin(g, "Estamos confirmando el porcentaje exacto que le corresponde.",
                           fila.Nombre);

            var pct = fila.PctMin;

            // Un valor mayor que 100 no es un porcentaje. Hay 1.867 filas asi en
            // 680 planes; aplicarlo pagaria mas que la factura.
            if (pct is > 100)
                return Sin(g, "Lo está revisando un especialista.", fila.Nombre);

            return new GastoEvaluado
            {
                Descripcion         = g.Descripcion,
                ValorPresentado     = g.Valor,
                CodigoBeneficio     = ben,
                NombreBeneficio     = fila.Nombre,
                NumeroProcedimiento = g.NumeroProcedimiento,
                NombreProcedimiento = g.NombreLr05,
                PorcentajeDelPlan   = pct,
                TopePorPrestacion   = fila.Tope,
                AplicaDeducible     = fila.Ded
            };
        }).ToList();
    }

    private static GastoEvaluado Sin(
        (string Descripcion, decimal Valor, string? CodigoBeneficio, int? NumeroProcedimiento,
         string? NombreLr05, bool Ambigua) g, string porque, string? nombreBen = null) =>
        new()
        {
            Descripcion = g.Descripcion, ValorPresentado = g.Valor,
            CodigoBeneficio = g.CodigoBeneficio, NombreBeneficio = nombreBen,
            NumeroProcedimiento = g.NumeroProcedimiento, NombreProcedimiento = g.NombreLr05,
            PorQueNoSeSabe = porque
        };

    private async Task<Dictionary<string, (string?, decimal?, decimal?, decimal?, bool)>>
        LeerPlanAsync(string plan, int version, string producto, CancellationToken ct)
    {
        var salida = new Dictionary<string, (string?, decimal?, decimal?, decimal?, bool)>(
            StringComparer.OrdinalIgnoreCase);

        var cs = _config.GetConnectionString(ConnName);
        if (string.IsNullOrWhiteSpace(cs)) return salida;

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);

        await using var cmd = new SqlCommand(Consulta, cn) { CommandTimeout = 20 };
        cmd.Parameters.Add(new SqlParameter("@codigoPlan",     SqlDbType.VarChar, 40) { Value = plan });
        cmd.Parameters.Add(new SqlParameter("@versionPlan",    SqlDbType.Int)         { Value = version });
        cmd.Parameters.Add(new SqlParameter("@codigoProducto", SqlDbType.VarChar, 10) { Value = producto });

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            salida[rd.GetString(0).Trim()] = (
                rd.IsDBNull(1) ? null : rd.GetString(1).Trim(),
                rd.IsDBNull(2) ? null : rd.GetDecimal(2),
                rd.IsDBNull(3) ? null : rd.GetDecimal(3),
                rd.IsDBNull(4) ? null : rd.GetDecimal(4),
                !rd.IsDBNull(5) && rd.GetInt32(5) == 1);
        }

        return salida;
    }
}
