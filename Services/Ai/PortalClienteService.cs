using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using app_ocr_ai_models.Data;
using app_tramites.Models.ModelAi;
using app_tramites.Services.Ai.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace app_ocr_ai_models.Services.Ai;

// =============================================================================
// REQ-020 — Los datos del afiliado, resueltos por código y no por el modelo.
//
// El portal del cliente empieza por una cédula y necesita saber, con certeza,
// qué contratos tiene esa persona y en qué condiciones está cada uno. Eso no se
// le pregunta a un modelo: se consulta. El modelo entra después, sólo para
// explicar en cristiano lo que estas consultas ya dejaron resuelto.
//
// Las llamadas van por el mismo IToolExecutor que usan los agentes, y no por un
// HttpClient propio, por tres razones concretas:
//   · el token de Saludsa y la resolución de {api-contrato} ya están ahí;
//   · el guardián D2 sigue vigente (el portal sólo alcanza sus 9 herramientas);
//   · cada consulta queda en ToolInvocation, así que la pantalla del cliente
//     puede rendir cuentas igual que la del auditor.
// =============================================================================

public sealed class PortalClienteService
{
    private const string AgentePortal = "AGENTE_PORTAL_CLIENTE";

    private readonly OCRDbContext _db;
    private readonly IToolExecutor _tools;
    private readonly ILogger<PortalClienteService> _log;

    public PortalClienteService(OCRDbContext db, IToolExecutor tools, ILogger<PortalClienteService> log)
    {
        _db    = db    ?? throw new ArgumentNullException(nameof(db));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _log   = log   ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Normaliza la cédula al formato que exige la API: diez dígitos CON el cero
    /// inicial. En las bases de Saludsa se guarda sin él, y en las APIs es al
    /// revés; confundirlos es lo que hacía que un afiliado con contrato vigente
    /// apareciera como inexistente.
    /// </summary>
    public static string NormalizarCedula(string? cedula)
    {
        var d = new string((cedula ?? string.Empty).Where(char.IsDigit).ToArray());
        return d.Length == 9 ? "0" + d : d;
    }

    /// <summary>
    /// Los contratos de una persona. Devuelve lista vacía cuando la API
    /// responde que no hay datos, que no es lo mismo que un fallo: al afiliado
    /// hay que decirle "no encontramos contratos con esa cédula", no "error".
    /// </summary>
    public async Task<ResultadoContratos> BuscarContratosAsync(
        string cedula, Guid caseCode, CancellationToken ct = default)
    {
        var ced = NormalizarCedula(cedula);
        if (ced.Length < 9)
            return ResultadoContratos.Invalida("La cédula debe tener 10 dígitos.");

        var execId = await AbrirEjecucionAsync(caseCode, "resolver_contrato_por_cedula", ct);

        string json;
        try
        {
            // Los CUATRO parametros con los que el servicio devuelve TODOS los
            // contratos, tal como los manda el front del portal (lo verifico
            // Nestor a mano): sin canalAcceso=APP-WEB algunos afiliados vuelven
            // vacios, y codigoProducto=True es un flag del endpoint, no un
            // producto. Aqui se pasan explicitos y no por el default del schema,
            // porque esta ruta no la conduce el modelo.
            json = await _tools.ExecuteAsync(
                "resolver_contrato_por_cedula", AgentePortal,
                new Dictionary<string, object?>
                {
                    ["numeroDocumento"] = ced,
                    ["tipoDocumento"]   = "C",
                    ["codigoProducto"]  = "True",
                    ["canalAcceso"]     = "APP-WEB"
                },
                execId, ced, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[Portal] No se pudo consultar el contrato de la cédula {Cedula}", ced);
            await CerrarEjecucionAsync(execId, "Failed", ex.Message, ct);
            return ResultadoContratos.Fallo(
                "No pudimos consultar sus contratos en este momento. Vuelva a intentarlo en unos minutos.");
        }

        await CerrarEjecucionAsync(execId, "Completed", Recorta(json, 4000), ct);

        var contratos = ParsearContratos(json);
        if (contratos.Count == 0)
        {
            // "Estado":"Error" con "No existen datos" es la respuesta normal de
            // la API cuando la persona no tiene contrato: no es una avería.
            return ResultadoContratos.SinDatos(
                $"No encontramos contratos activos con la cédula {ced}. " +
                "Verifique el número o comuníquese con Salud S.A.");
        }

        return ResultadoContratos.Ok(ced, contratos);
    }

    /// <summary>
    /// Lee el sobre de respuesta de ObtenerContratoPorDocumento. El JSON viene
    /// con la forma { Estado, Datos: { Entidades: [...] } } y cada entidad es un
    /// contrato con su plan, su deducible y el titular.
    /// </summary>
    private static List<ContratoAfiliado> ParsearContratos(string json)
    {
        var lista = new List<ContratoAfiliado>();
        if (string.IsNullOrWhiteSpace(json)) return lista;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;

            if (raiz.TryGetProperty("Estado", out var est)
                && string.Equals(est.GetString(), "Error", StringComparison.OrdinalIgnoreCase))
                return lista;

            if (!raiz.TryGetProperty("Datos", out var datos)) return lista;
            if (!datos.TryGetProperty("Entidades", out var ents) || ents.ValueKind != JsonValueKind.Array)
                return lista;

            foreach (var e in ents.EnumerateArray())
            {
                var tit = e.TryGetProperty("Titular", out var t) && t.ValueKind == JsonValueKind.Object ? t : default;

                lista.Add(new ContratoAfiliado
                {
                    Numero           = Txt(e, "Numero"),
                    Region           = Txt(e, "Region"),
                    Producto         = Txt(e, "Producto"),
                    CodigoPlan       = Txt(e, "CodigoPlan"),
                    NombrePlan       = Txt(e, "NombrePlan"),
                    NombreComercial  = Txt(e, "NombreComercialPlanApp") ?? Txt(e, "NombreComercialPlan"),
                    Estado           = Txt(e, "Estado"),
                    Nivel            = Num(e, "Nivel"),
                    Version          = Num(e, "Version"),
                    CoberturaMaxima  = Dec(e, "CoberturaMaxima"),
                    DeducibleTotal   = Dec(e, "DeducibleTotal"),
                    CuotaMensual     = Dec(e, "CuotaMensual"),
                    EsMoroso         = Bool(e, "EsMoroso"),
                    TieneImpedimento = Bool(e, "TieneImpedimento"),
                    MotivoImpedimento= Txt(e, "MotivoImpedimento"),
                    FechaInicio      = Txt(e, "FechaInicio"),
                    TitularNombre    = tit.ValueKind == JsonValueKind.Object
                                        ? $"{Txt(tit, "Nombres")} {Txt(tit, "Apellidos")}".Trim()
                                        : null,
                    TitularNumero    = tit.ValueKind == JsonValueKind.Object ? Num(tit, "Numero") : null,
                    TitularDocumento = tit.ValueKind == JsonValueKind.Object ? Txt(tit, "NumeroDocumento") : null,
                    // Los dependientes venian en la MISMA respuesta y se estaban
                    // tirando. Importan: el deducible consumido, la carencia y
                    // las preexistencias son de cada persona, no del contrato.
                    Beneficiarios    = LeerBeneficiarios(e),
                    Crudo            = e.GetRawText()
                });
            }
        }
        catch (JsonException ex)
        {
            // Un JSON que no encaja no debe tumbar la pantalla del afiliado.
            System.Diagnostics.Debug.WriteLine("[Portal] respuesta de contratos ilegible: " + ex.Message);
        }

        return lista;
    }


    /// <summary>
    /// Las personas cubiertas por el contrato: el titular y sus dependientes.
    ///
    /// Cada una trae lo que de verdad decide la liquidacion —cuanto deducible
    /// lleva consumido, si sigue en carencia y que preexistencias tiene—, asi
    /// que elegir mal al beneficiario no es un detalle de formulario: aplica el
    /// deducible de otra persona e ignora la carencia de la que se atendio.
    /// </summary>
    private static List<BeneficiarioAfiliado> LeerBeneficiarios(JsonElement contrato)
    {
        var lista = new List<BeneficiarioAfiliado>();
        if (contrato.ValueKind != JsonValueKind.Object) return lista;
        if (!contrato.TryGetProperty("Beneficiarios", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return lista;

        foreach (var b in arr.EnumerateArray())
        {
            if (b.ValueKind != JsonValueKind.Object) continue;

            var preex = 0;
            if (b.TryGetProperty("Preexistencias", out var px) && px.ValueKind == JsonValueKind.Array)
                preex = px.GetArrayLength();

            lista.Add(new BeneficiarioAfiliado
            {
                NumeroPersona     = Num(b, "NumeroPersona"),
                Nombres           = Txt(b, "Nombres"),
                Apellidos         = Txt(b, "Apellidos"),
                Documento         = Txt(b, "NumeroDocumento"),
                Genero            = Txt(b, "Genero"),
                Edad              = Num(b, "Edad"),
                FechaNacimiento   = Txt(b, "FechaNacimiento"),
                Relacion          = Txt(b, "RelacionDependiente"),
                DeducibleCubierto = Dec(b, "DeducibleCubierto"),
                EnCarencia        = Bool(b, "EnCarencia"),
                DiasFinCarencia   = Num(b, "DiasFinCarencia"),
                EnCarenciaHosp    = Bool(b, "EnCarenciaHospitalaria"),
                Preexistencias    = preex,
                Maternidad        = Bool(b, "Maternidad")
            });
        }

        return AsegurarTitular(lista, contrato);
    }

    /// <summary>
    /// Garantiza que el titular esté en la lista de personas elegibles.
    ///
    /// La API no es consistente: en unos contratos devuelve al titular dentro
    /// de Beneficiarios (con RelacionDependiente = "Titular") y en otros no.
    /// Medido en el contrato 4160731: sólo devolvió a las dos hijas, así que el
    /// titular no podía presentar un reembolso suyo — que es justamente el caso
    /// más frecuente, porque es quien está usando el portal.
    ///
    /// Se añade sólo si falta, comparando por documento para no duplicarlo
    /// cuando la API sí lo trae.
    /// </summary>
    public static List<BeneficiarioAfiliado> AsegurarTitular(
        List<BeneficiarioAfiliado> lista, JsonElement contrato)
    {
        if (contrato.ValueKind == JsonValueKind.Object
            && contrato.TryGetProperty("Titular", out var t)
            && t.ValueKind == JsonValueKind.Object)
        {
            var doc = Txt(t, "NumeroDocumento");
            var num = Num(t, "Numero");

            var yaEsta = lista.Any(x =>
                (!string.IsNullOrWhiteSpace(doc)
                 && string.Equals(x.Documento, doc, StringComparison.OrdinalIgnoreCase))
                || (num.HasValue && x.NumeroPersona == num));

            if (!yaEsta && (num.HasValue || !string.IsNullOrWhiteSpace(doc)))
            {
                lista.Add(new BeneficiarioAfiliado
                {
                    NumeroPersona   = num,
                    Nombres         = Txt(t, "Nombres"),
                    Apellidos       = Txt(t, "Apellidos"),
                    Documento       = doc,
                    Genero          = Txt(t, "Genero"),
                    Edad            = Num(t, "Edad"),
                    FechaNacimiento = Txt(t, "FechaNacimiento"),
                    Relacion        = "Titular"
                    // Las condiciones (deducible, carencia, preexistencias) no
                    // vienen en el nodo Titular. Se dejan en null: es más honesto
                    // que enseñar un cero que no se ha comprobado.
                });
            }
        }

        // El titular primero: es quien está usando el portal y lo normal es que
        // el reembolso sea suyo, así que ahorra un clic en el caso frecuente.
        return lista
            .OrderByDescending(x => x.EsTitular)
            .ThenBy(x => x.Nombres)
            .ToList();
    }

    // ── Lectores tolerantes ───────────────────────────────────────────────
    // La API mezcla tipos entre entornos (Numero llega como número o como
    // cadena), así que se lee por valor y no por tipo esperado.

    private static string? Txt(JsonElement e, string prop)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.ToString(),
            JsonValueKind.True   => "true",
            JsonValueKind.False  => "false",
            _ => null
        };
    }

    private static int? Num(JsonElement e, string prop)
    {
        var s = Txt(e, prop);
        return int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static decimal? Dec(JsonElement e, string prop)
    {
        var s = Txt(e, prop);
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static bool? Bool(JsonElement e, string prop)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.True  => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) ? b : null,
            _ => null
        };
    }

    // ── Trazabilidad ──────────────────────────────────────────────────────

    /// <summary>
    /// Abre el StepExecution al que se colgará la invocación. Sin él la llamada
    /// no quedaría auditada y la pantalla del cliente no podría enseñar qué se
    /// consultó de verdad.
    /// </summary>
    private async Task<long> AbrirEjecucionAsync(Guid caseCode, string motivo, CancellationToken ct)
    {
        var exec = new StepExecution
        {
            CaseCode       = caseCode,
            StepOrder      = 0,
            // El afiliado todavía no ha subido nada: esta ejecución no opera
            // sobre ningún documento y la columna lo admite (REQ-020b).
            DataFileId     = null,
            ModelCode      = AgentePortal,
            RequestContent = motivo,
            Status         = "Running",
            StartDate      = DateTime.UtcNow
        };
        _db.StepExecution.Add(exec);
        await _db.SaveChangesAsync(ct);
        return exec.ExecutionId;
    }

    private async Task CerrarEjecucionAsync(long execId, string estado, string? respuesta, CancellationToken ct)
    {
        var exec = await _db.StepExecution.FirstOrDefaultAsync(x => x.ExecutionId == execId, ct);
        if (exec == null) return;
        exec.Status          = estado;
        exec.EndDate         = DateTime.UtcNow;
        exec.ResponseContent = respuesta;
        await _db.SaveChangesAsync(ct);
    }

    private static string? Recorta(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);
}

/// <summary>Un contrato del afiliado, tal como lo presenta el portal.</summary>
public sealed class ContratoAfiliado
{
    public string? Numero { get; set; }
    public string? Region { get; set; }
    public string? Producto { get; set; }
    public string? CodigoPlan { get; set; }
    public string? NombrePlan { get; set; }
    public string? NombreComercial { get; set; }
    public string? Estado { get; set; }
    public int? Nivel { get; set; }
    public int? Version { get; set; }
    public decimal? CoberturaMaxima { get; set; }
    public decimal? DeducibleTotal { get; set; }
    public decimal? CuotaMensual { get; set; }
    public bool? EsMoroso { get; set; }
    public bool? TieneImpedimento { get; set; }
    public string? MotivoImpedimento { get; set; }
    public string? FechaInicio { get; set; }
    public string? TitularNombre { get; set; }
    public int? TitularNumero { get; set; }
    public string? TitularDocumento { get; set; }

    /// <summary>Las personas cubiertas por este contrato (titular y dependientes).</summary>
    public List<BeneficiarioAfiliado> Beneficiarios { get; set; } = new();

    /// <summary>La entidad completa, para guardarla tal cual se recibió.</summary>
    public string? Crudo { get; set; }

    /// <summary>Nombre corto para el selector: lo que el afiliado reconoce.</summary>
    public string Titulo =>
        !string.IsNullOrWhiteSpace(NombreComercial) ? NombreComercial!
        : !string.IsNullOrWhiteSpace(NombrePlan) ? NombrePlan!
        : $"Contrato {Numero}";

    /// <summary>
    /// Si algo del propio contrato ya bloquea el reembolso, conviene decirlo
    /// antes de que el afiliado suba diez documentos para nada.
    /// </summary>
    public string? Advertencia =>
        TieneImpedimento == true
            ? (string.IsNullOrWhiteSpace(MotivoImpedimento)
                ? "Este contrato tiene un impedimento registrado."
                : $"Este contrato tiene un impedimento: {MotivoImpedimento}")
        : EsMoroso == true
            ? "Este contrato figura con cuotas pendientes de pago."
        : !string.IsNullOrWhiteSpace(Estado) && !Estado!.Equals("Activo", StringComparison.OrdinalIgnoreCase)
            ? $"Este contrato figura como {Estado}."
        : null;
}

/// <summary>
/// Resultado de buscar contratos. Distingue tres cosas que no son lo mismo:
/// no encontrar contratos, escribir mal la cédula, y que la consulta falle.
/// Tratarlas igual es lo que produce mensajes de error inútiles.
/// </summary>
public sealed class ResultadoContratos
{
    public bool EsOk { get; private init; }
    public string? Cedula { get; private init; }
    public string? Mensaje { get; private init; }
    public List<ContratoAfiliado> Contratos { get; private init; } = new();

    public static ResultadoContratos Ok(string cedula, List<ContratoAfiliado> c) =>
        new() { EsOk = true, Cedula = cedula, Contratos = c };

    public static ResultadoContratos SinDatos(string msg) => new() { EsOk = false, Mensaje = msg };
    public static ResultadoContratos Invalida(string msg) => new() { EsOk = false, Mensaje = msg };
    public static ResultadoContratos Fallo(string msg)    => new() { EsOk = false, Mensaje = msg };
}

/// <summary>
/// Una persona cubierta por el contrato. Lleva sus propias condiciones porque
/// son suyas y no del contrato: el deducible se consume por persona, la
/// carencia corre desde que ESA persona entro al plan, y las preexistencias
/// son las que declaro ella.
/// </summary>
public sealed class BeneficiarioAfiliado
{
    public int? NumeroPersona { get; set; }
    public string? Nombres { get; set; }
    public string? Apellidos { get; set; }
    public string? Documento { get; set; }
    public string? Genero { get; set; }
    public int? Edad { get; set; }
    public string? FechaNacimiento { get; set; }

    /// <summary>Titular | Conyuge | Hijo | …</summary>
    public string? Relacion { get; set; }

    public decimal? DeducibleCubierto { get; set; }
    public bool? EnCarencia { get; set; }
    public int? DiasFinCarencia { get; set; }
    public bool? EnCarenciaHosp { get; set; }
    public int Preexistencias { get; set; }
    public bool? Maternidad { get; set; }

    public string NombreCompleto => $"{Nombres} {Apellidos}".Trim();

    public bool EsTitular =>
        string.Equals(Relacion, "Titular", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lo que hay que advertirle ANTES de que presente, no despues. Una
    /// carencia sin cumplir deja el gasto fuera aunque el plan lo contemple, y
    /// enterarse al final es la peor manera de saberlo.
    /// </summary>
    public string? Advertencia
    {
        get
        {
            if (EnCarencia == true)
                return DiasFinCarencia is > 0
                    ? $"Todavia esta en periodo de espera: le faltan {DiasFinCarencia} dias para poder usar algunos beneficios."
                    : "Todavia esta en periodo de espera para algunos beneficios.";
            if (EnCarenciaHosp == true)
                return "Todavia esta en periodo de espera para la cobertura hospitalaria.";
            return null;
        }
    }
}
