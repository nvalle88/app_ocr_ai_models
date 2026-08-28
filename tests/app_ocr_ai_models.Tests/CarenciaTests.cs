using app_ocr_ai_models.Areas.Studio.Models;
using Xunit;

namespace app_ocr_ai_models.Tests;

// =============================================================================
// La carencia decide si un gasto se paga o no. Los dos errores cuestan:
// aplicarla de más niega un reembolso legítimo, no aplicarla paga algo que la
// póliza todavía no cubre.
//
// Y son DOS carencias, no una: un afiliado puede estar fuera de la ambulatoria
// y dentro de la hospitalaria al mismo tiempo.
// =============================================================================
public class CarenciaTests
{
    [Fact]
    public void Ambulatoria_activa_niega_un_gasto_ambulatorio()
    {
        var v = CarenciaDelBeneficiario.Juzgar(
            enCarenciaAmbulatoria: true, enCarenciaHospitalaria: false,
            diasFinCarencia: 12, tipoCobertura: "Ambulatorio");

        Assert.True(v.EnCarencia);
        Assert.Equal("ambulatoria", v.Cual);
        Assert.Contains("faltan 12 días", v.ParaElCliente);
    }

    [Fact]
    public void Ambulatoria_activa_NO_niega_un_gasto_hospitalario()
    {
        // Aqui esta el fallo que se corrige: tratarlas como una sola carencia.
        var v = CarenciaDelBeneficiario.Juzgar(
            enCarenciaAmbulatoria: true, enCarenciaHospitalaria: false,
            diasFinCarencia: 12, tipoCobertura: "Hospitalario");

        Assert.False(v.EnCarencia);
        Assert.Equal("hospitalaria", v.Cual);
        Assert.Equal(string.Empty, v.ParaElCliente);
    }

    [Fact]
    public void Hospitalaria_activa_niega_un_gasto_hospitalario()
    {
        var v = CarenciaDelBeneficiario.Juzgar(false, true, 40, "hospitalario");

        Assert.True(v.EnCarencia);
        Assert.Equal("hospitalaria", v.Cual);
        Assert.Contains("hospitalarias", v.ParaElCliente);
    }

    [Fact]
    public void Hospitalaria_activa_NO_niega_una_consulta_ambulatoria()
    {
        var v = CarenciaDelBeneficiario.Juzgar(false, true, 40, "Ambulatorio");

        Assert.False(v.EnCarencia);
        Assert.Equal("ambulatoria", v.Cual);
    }

    [Fact]
    public void Sin_carencia_no_dice_nada()
    {
        var v = CarenciaDelBeneficiario.Juzgar(false, false, 0, "Ambulatorio");

        Assert.False(v.EnCarencia);
        Assert.False(v.NoSeSabe);
        Assert.Equal(string.Empty, v.ParaElCliente);
    }

    // ── Ante la duda, ni negar ni ignorar ───────────────────────────────────

    [Fact]
    public void Sin_saber_el_tipo_de_gasto_no_niega_pero_lo_marca()
    {
        // Si hay una carencia activa y no se sabe si el gasto es ambulatorio u
        // hospitalario, no se puede decidir. Ni negar -seria injusto- ni pasar
        // de largo -podria pagarse algo sin cobertura-.
        var v = CarenciaDelBeneficiario.Juzgar(true, false, 12, tipoCobertura: null);

        Assert.False(v.EnCarencia);
        Assert.True(v.NoSeSabe);
    }

    [Fact]
    public void Sin_carencias_activas_y_sin_tipo_no_hay_nada_que_marcar()
    {
        var v = CarenciaDelBeneficiario.Juzgar(false, false, null, "");

        Assert.False(v.EnCarencia);
        Assert.False(v.NoSeSabe);
    }

    [Fact]
    public void Si_el_contrato_no_informo_la_carencia_no_se_inventa()
    {
        var v = CarenciaDelBeneficiario.Juzgar(
            enCarenciaAmbulatoria: null, enCarenciaHospitalaria: null,
            diasFinCarencia: null, tipoCobertura: "Ambulatorio");

        Assert.False(v.EnCarencia);
        Assert.True(v.NoSeSabe);
    }

    [Theory]
    [InlineData("AMBULATORIO")]
    [InlineData("ambulatoria")]
    [InlineData("Ambulatorio ")]
    public void El_tipo_se_reconoce_venga_como_venga(string tipo)
    {
        var v = CarenciaDelBeneficiario.Juzgar(true, false, 5, tipo);
        Assert.True(v.EnCarencia);
    }

    [Fact]
    public void Sin_dias_informados_el_mensaje_sigue_siendo_claro()
    {
        var v = CarenciaDelBeneficiario.Juzgar(true, false, null, "Ambulatorio");

        Assert.True(v.EnCarencia);
        Assert.DoesNotContain("faltan", v.ParaElCliente);
        Assert.Contains("no tiene cobertura", v.ParaElCliente);
    }
}
