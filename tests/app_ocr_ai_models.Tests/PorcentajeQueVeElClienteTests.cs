using app_ocr_ai_models.Areas.Studio.Models;
using Xunit;

namespace app_ocr_ai_models.Tests;

// =============================================================================
// El porcentaje que aparece en la pantalla del afiliado.
//
// Venia de dividir valorCubierto entre valorPresentado, dos cifras que escribia
// el modelo. Desde REQ-027b al modelo se le pide que las deje en null cuando no
// le constan, asi que esa division pasaba a dar CERO — y «0%» en la pantalla de
// alguien que espera un reembolso se lee como «no me cubren nada».
//
// Ahora manda el porcentaje del PLAN, leido de Pr05Beneficios. Y cuando no hay
// ninguno de los dos, no se enseña porcentaje: es mejor no decir nada que decir
// que no le cubren.
// =============================================================================
public class PorcentajeQueVeElClienteTests
{
    private static ItemResolucionVm Item(decimal presentado, decimal cubierto, decimal? delPlan = null) =>
        new()
        {
            Descripcion = "COLONOSCOPIA (VCC)",
            ValorPresentado = presentado,
            ValorCubierto = cubierto,
            PorcentajeDelPlan = delPlan
        };

    [Fact]
    public void El_del_PLAN_manda_sobre_el_derivado()
    {
        // El plan dice 80. Las cifras del modelo darian 50. Gana el plan, porque
        // es el unico verificable contra la tabla.
        var it = Item(presentado: 100m, cubierto: 50m, delPlan: 80m);

        Assert.Equal(80m, it.Porcentaje);
    }

    [Fact]
    public void Sin_dato_del_plan_se_deriva_de_las_cifras_reales()
    {
        var it = Item(presentado: 400m, cubierto: 300m);

        Assert.Equal(75m, it.Porcentaje);
    }

    [Fact]
    public void Un_cubierto_en_CERO_ya_NO_se_enseña_como_0_por_ciento()
    {
        // Este es el fallo que se cierra: el modelo deja de poner valorCubierto y
        // la division daba 0. «0%» dice «no le cubrimos nada», que es una
        // afirmacion, cuando lo cierto es que aun no se sabe.
        var it = Item(presentado: 478.08m, cubierto: 0m);

        Assert.Null(it.Porcentaje);
    }

    [Fact]
    public void Sin_valor_presentado_tampoco_se_inventa()
    {
        Assert.Null(Item(presentado: 0m, cubierto: 0m).Porcentaje);
    }

    [Fact]
    public void Un_cero_del_PLAN_si_es_una_respuesta_y_se_respeta()
    {
        // Distinto del caso anterior: aqui el plan DICE que cubre 0%. Eso no es
        // una laguna, es una negativa legitima, y se enseña.
        var it = Item(presentado: 478.08m, cubierto: 0m, delPlan: 0m);

        Assert.Equal(0m, it.Porcentaje);
    }

    [Fact]
    public void La_frase_del_plan_es_la_que_se_guarda_para_la_pantalla()
    {
        var it = Item(100m, 80m, 80m);
        it.ExplicacionDelPlan = "Su plan cubre el 80% de laboratorio clínico.";

        Assert.Contains("80%", it.ExplicacionDelPlan);
        Assert.Equal(80m, it.Porcentaje);
    }
}
