using System.Globalization;
using app_ocr_ai_models.Services;
using Xunit;

namespace app_ocr_ai_models.Tests;

// =============================================================================
// Lo que se le dice al afiliado de cada gasto.
//
// Estas frases sustituyen a las que escribia el modelo. La diferencia es que
// estas NO pueden contradecir a los datos, porque salen de ellos. Los tests
// sujetan justo eso: que cuando no se sabe, no se inventa, y que un dato que
// falta nunca se convierte en una negativa.
// =============================================================================
public class EvaluadorDeCoberturaTests
{
    private static GastoEvaluado Con(decimal? pct, string? nombreBen = "LABORATORIO CLINICO",
                                     decimal? tope = null, bool ded = false,
                                     bool ambigua = false, string? porque = null) =>
        new()
        {
            Descripcion = "COLONOSCOPIA (VCC)", ValorPresentado = 358.08m,
            CodigoBeneficio = "A003", NombreBeneficio = nombreBen,
            PorcentajeDelPlan = pct, TopePorPrestacion = tope,
            AplicaDeducible = ded, Ambigua = ambigua, PorQueNoSeSabe = porque
        };

    [Fact]
    public void Con_porcentaje_lo_dice_con_el_nombre_del_beneficio_no_el_codigo()
    {
        var t = Con(80m).ParaElCliente;

        Assert.Contains("80%", t);
        Assert.DoesNotContain("A003", t);          // al afiliado el codigo no le dice nada
    }

    [Fact]
    public void El_nombre_del_beneficio_sale_ACENTUADO_aunque_el_catalogo_no_lo_este()
    {
        // Pr07CatalogoBeneficios guarda "LABORATORIO CLINICO", en mayusculas y
        // sin tildes. Eso esta bien en un maestro y mal en la pantalla de una
        // persona.
        Assert.Contains("laboratorio clínico", Con(80m, "LABORATORIO CLINICO").ParaElCliente);
        Assert.Contains("consulta médica",     Con(80m, "CONSULTA MEDICA").ParaElCliente);
    }

    [Fact]
    public void Un_beneficio_que_no_esta_en_el_mapa_sale_igual_en_minuscula()
    {
        // Sin tilde, pero legible y mejor que un codigo: no se bloquea la frase
        // por no tener el nombre en el mapa.
        Assert.Contains("beneficio raro", Con(80m, "BENEFICIO RARO").ParaElCliente);
    }

    [Fact]
    public void Un_cero_por_ciento_se_dice_como_lo_que_es()
    {
        var t = Con(0m).ParaElCliente;

        Assert.Contains("no cubre", t);
        Assert.DoesNotContain("0%", t);            // "cubre el 0%" suena a error, no a respuesta
    }

    [Fact]
    public void El_tope_se_menciona_solo_cuando_es_un_tope_de_verdad()
    {
        // 999999 es el "sin tope" del maestro: decirlo seria ruido.
        Assert.DoesNotContain("tope", Con(80m, tope: 999999m).ParaElCliente);
        Assert.Contains("tope", Con(80m, tope: 500m).ParaElCliente);
    }

    [Fact]
    public void Si_va_contra_el_deducible_se_avisa()
    {
        Assert.Contains("deducible", Con(80m, ded: true).ParaElCliente);
        Assert.DoesNotContain("deducible", Con(80m, ded: false).ParaElCliente);
    }

    // ── Cuando no se sabe, NO se inventa y NO se niega ───────────────────────

    [Fact]
    public void Una_homologacion_ambigua_no_canta_porcentaje()
    {
        // Medido: "colonoscopia" son TRES beneficios distintos. Cuando empata,
        // elegir uno es elegir un porcentaje al azar.
        var t = Con(80m, ambigua: true).ParaElCliente;

        Assert.Contains("confirmando", t);
        Assert.DoesNotContain("%", t);
    }

    [Fact]
    public void Sin_porcentaje_NO_se_dice_que_no_cubre()
    {
        // El peor error posible: convertir "no lo se" en "no tiene cobertura".
        var t = Con(null, porque: "Estamos confirmando los datos de su plan.").ParaElCliente;

        Assert.DoesNotContain("no cubre", t);
        Assert.DoesNotContain("%", t);
        Assert.Contains("confirmando", t);
    }

    [Fact]
    public void Sin_porcentaje_y_sin_motivo_se_dice_que_lo_mira_una_persona()
    {
        var t = Con(null, porque: null).ParaElCliente;

        Assert.Contains("especialista", t);
        Assert.DoesNotContain("no cubre", t);
    }

    [Fact]
    public void Sin_nombre_de_beneficio_la_frase_sigue_teniendo_sentido()
    {
        var t = Con(70m, nombreBen: null).ParaElCliente;

        Assert.Contains("70%", t);
        Assert.Contains("esta prestación", t);
    }

    [Fact]
    public void El_porcentaje_se_escribe_sin_decimales_inutiles()
    {
        Assert.Contains("80%",   Con(80.00m).ParaElCliente);
        Assert.Contains("77,5%", Con(77.5m).ParaElCliente);   // es-EC usa coma
    }
}
