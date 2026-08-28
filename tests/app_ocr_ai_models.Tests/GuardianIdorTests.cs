using System.Collections.Generic;
using app_tramites.Services.Ai.Tools;
using Xunit;

namespace app_ocr_ai_models.Tests;

// =============================================================================
// REQ-020d — El guardián anti-IDOR, comparando cada clase con la suya.
//
// Estas pruebas existen porque el cambio toca seguridad: hay que demostrar dos
// cosas a la vez, y una sin la otra no vale.
//
//   · que SIGUE denegando lo que debe (pedir datos de otra persona)
//   · que YA NO deniega lo legítimo (un contrato comparado contra una cédula
//     nunca coincidía, y ese falso rechazo es lo que este cambio corrige)
// =============================================================================

public class GuardianIdorTests
{
    private static readonly ToolAuthorizationGuard Guardian = new();

    private static Dictionary<string, object?> Input(params (string Campo, object? Valor)[] campos)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (campo, valor) in campos) d[campo] = valor;
        return d;
    }

    // ── Lo que el guardián tiene que seguir impidiendo ───────────────────

    [Fact]
    public void Deniega_la_cedula_de_un_tercero()
    {
        var identidad = new IdentidadCaso().ConCedula("0912514197");

        var permitido = Guardian.IsAuthorized(
            "consultar_preexistencias_por_cedula",
            Input(("identificacion", "1757100373")),   // otra persona
            identidad);

        Assert.False(permitido);
    }

    [Fact]
    public void Deniega_un_contrato_ajeno()
    {
        var identidad = new IdentidadCaso()
            .ConCedula("0912514197")
            .ConContrato("Costa", "IND", "4160731");

        var permitido = Guardian.IsAuthorized(
            "consultar_deducible_contrato",
            Input(("region", "Costa"), ("codigoProducto", "IND"),
                  ("numeroContrato", "9999999")),      // otro contrato
            identidad);

        Assert.False(permitido);
    }

    [Fact]
    public void Deniega_un_numero_de_persona_ajeno()
    {
        var identidad = new IdentidadCaso()
            .ConContrato("Costa", "IND", "4160731")
            .ConPersona("842221");

        var permitido = Guardian.IsAuthorized(
            "consultar_deducible_contrato",
            Input(("region", "Costa"), ("codigoProducto", "IND"),
                  ("numeroContrato", "4160731"), ("numeroPersonaBeneficiario", "111111")),
            identidad);

        Assert.False(permitido);
    }

    // ── El falso rechazo que este cambio corrige ─────────────────────────

    [Fact]
    public void Ya_no_deniega_un_contrato_propio_por_no_ser_una_cedula()
    {
        // Este es EL caso medido en producción: 3 llamadas de
        // consultar_deducible_contrato denegadas porque el guardián comparaba
        // numeroContrato (4160731) contra la cédula del caso (0912514197).
        var identidad = new IdentidadCaso()
            .ConCedula("0912514197")
            .ConContrato("Costa", "IND", "4160731")
            .ConPersona("842221");

        var permitido = Guardian.IsAuthorized(
            "consultar_deducible_contrato",
            Input(("region", "Costa"),
                  ("codigoProducto", "IND"),
                  ("numeroContrato", "4160731"),
                  ("numeroPersonaBeneficiario", "842221")),
            identidad);

        Assert.True(permitido);
    }

    [Fact]
    public void Admite_la_cedula_de_un_dependiente_del_mismo_contrato()
    {
        // Un contrato cubre al titular y a sus hijos. Consultar las
        // preexistencias del hijo por SU cédula es legítimo: antes se denegaba
        // por no ser la del titular.
        var identidad = new IdentidadCaso()
            .ConCedula("0912514197")     // titular
            .ConCedula("1757100373");    // hijo

        var permitido = Guardian.IsAuthorized(
            "consultar_preexistencias_por_cedula",
            Input(("identificacion", "1757100373")),
            identidad);

        Assert.True(permitido);
    }

    [Fact]
    public void La_cedula_coincide_con_y_sin_cero_inicial()
    {
        // En las APIs la cédula viaja con cero inicial y en las bases de
        // Saludsa sin él. Si la normalización fallara, el guardián denegaría a
        // la persona correcta.
        var identidad = new IdentidadCaso().ConCedula("0912514197");

        Assert.True(Guardian.IsAuthorized(
            "consultar_preexistencias_por_cedula",
            Input(("identificacion", "912514197")),
            identidad));
    }

    // ── Los estados de desconocimiento ───────────────────────────────────

    [Fact]
    public void Sin_identidad_resuelta_deja_pasar()
    {
        // Es la primera llamada del caso, la que justamente averigua quién es.
        Assert.True(Guardian.IsAuthorized(
            "resolver_contrato_por_cedula",
            Input(("numeroDocumento", "0912514197")),
            new IdentidadCaso()));
    }

    [Fact]
    public void Sin_conocer_esa_clase_no_juzga_esa_clase()
    {
        // Se conoce la cédula pero no el contrato: no se puede juzgar el
        // contrato. Denegar aquí sería inventarse una certeza que no se tiene
        // — y es exactamente lo que rompía las consultas de deducible.
        var identidad = new IdentidadCaso().ConCedula("0912514197");

        Assert.True(Guardian.IsAuthorized(
            "consultar_deducible_contrato",
            Input(("region", "Costa"), ("codigoProducto", "IND"),
                  ("numeroContrato", "4160731")),
            identidad));
    }

    [Fact]
    public void Un_campo_que_no_identifica_a_nadie_no_se_revisa()
    {
        var identidad = new IdentidadCaso().ConCedula("0912514197");

        Assert.True(Guardian.IsAuthorized(
            "consultar_coberturas_plan",
            Input(("region", "Costa"), ("codigoPlan", "N3-D-C")),
            identidad));
    }

    [Fact]
    public void La_firma_antigua_sigue_comportandose_igual()
    {
        // Los llamadores que aún pasan una sola cédula no deben cambiar de
        // comportamiento para la clase cédula.
        Assert.False(Guardian.IsAuthorized(
            "consultar_preexistencias_por_cedula",
            Input(("identificacion", "1757100373")),
            "0912514197"));

        Assert.True(Guardian.IsAuthorized(
            "consultar_preexistencias_por_cedula",
            Input(("identificacion", "0912514197")),
            "0912514197"));
    }
}
