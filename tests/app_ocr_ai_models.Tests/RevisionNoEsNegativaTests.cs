using System.Collections.Generic;
using System.Linq;
using app_ocr_ai_models.Areas.Studio.Models;
using Xunit;

namespace app_ocr_ai_models.Tests;

// =============================================================================
// Un caso en revisión no es un caso rechazado.
//
// Caso 598ec576: el afiliado sube la factura y sus dos respaldos, y la pantalla
// le anuncia «Estimamos devolverle $ 0,00» con un motivo que habla de la
// factura. La resolución dice lo contrario: la regla «Factura electrónica
// válida (SRI, clave 49 díg.)» sale CUMPLE, el contrato está vigente y no hay
// carencia. Lo único que pasa es que quedó REQUIERE_CH y en ese estado el
// modelo deja todos los valores en cero.
// =============================================================================
public class RevisionNoEsNegativaTests
{
    private static ResolucionClienteVm CasoReal() => new()
    {
        EstadoPropuesto = "CONTROL_HUMANO",
        TotalPresentado = 478.08m,
        TotalCubierto   = 0m,
        TotalDeducible  = 0m,
        TotalCopago     = 0m,
        TotalNoCubierto = 0m,
        TotalPendiente  = 478.08m,
        Reglas =
        {
            new() { Familia = "Elegibilidad/Factura", Resultado = "CUMPLE",
                    Regla = "Factura electrónica válida (SRI, clave 49 díg., comprobante 01, RUC 13 díg.)" },
            new() { Familia = "Elegibilidad/Factura", Resultado = "CUMPLE",
                    Regla = "Vigencia/estado del contrato" },
            new() { Familia = "Preexistencias/Carencias", Resultado = "CUMPLE",
                    Regla = "Carencia ambulatoria / preexistencias" },
            new() { Familia = "Elegibilidad/Factura", Resultado = "REQUIERE_CH",
                    Regla = "Factura ya pagada / duplicada" },
            new() { Familia = "Copagos", Resultado = "REQUIERE_CH",
                    Regla = "PrestadorCopagos por RUC / copago por convenio",
                    Detalle = "No se pudo resolver el convenio del RUC 0916422173001 (fallo SQL)." },
            new() { Familia = "Diagnósticos/Exclusiones", Resultado = "NO_APLICA",
                    Regla = "Neoplasia maligna (ONC) / GRD múltiple" },
        }
    };

    [Fact]
    public void No_se_anuncia_una_cifra_cuando_todavia_no_hay_cifra()
    {
        var r = CasoReal();

        Assert.True(r.EnRevision);
        Assert.True(r.SinCifraTodavia);
        Assert.Equal("Su caso está en revisión", r.TitularCifra);
        Assert.DoesNotContain("devolverle", r.TitularCifra);
    }

    [Fact]
    public void Lo_que_ya_esta_comprobado_se_dice__empezando_por_la_factura()
    {
        var r = CasoReal();

        Assert.Equal(3, r.LoQueEstaEnOrden.Count);
        Assert.Contains(r.LoQueEstaEnOrden, x => x.Regla!.Contains("Factura electrónica válida"));
    }

    [Fact]
    public void Lo_pendiente_no_se_mezcla_con_lo_rechazado()
    {
        // El defecto de fondo: REQUIERE_CH y NO_CUMPLE iban al mismo cajón
        // (LeAfecta), así que «lo revisa un analista» se leía como «se lo
        // negamos».
        var r = CasoReal();

        Assert.Equal(2, r.LoQueFaltaRevisar.Count);
        Assert.Empty(r.LoQueNoSeCumple);
    }

    [Fact]
    public void Al_afiliado_no_se_le_enseña_jerga_de_auditor()
    {
        // Se le estaba enseñando literalmente «motivos_CH = 4 (duplicidad,
        // discrepancia dx, convenio no verificable/SQL, tope>250)» y «Matriz de
        // triaje (Count CH vs Count Negativa)». Nadie que no trabaje aquí
        // entiende una palabra.
        var r = CasoReal();

        var todo = string.Join(" ",
            r.LoQueEstaEnOrden.Select(x => x.ParaElCliente)
             .Concat(r.LoQueFaltaRevisar.Select(x => x.ParaElCliente)));

        Assert.DoesNotContain("SQL", todo);
        Assert.DoesNotContain("CH", todo);
        Assert.DoesNotContain("RUC", todo);
        Assert.DoesNotContain("dx", todo);
    }

    [Fact]
    public void La_maquinaria_interna_no_se_le_cuenta()
    {
        // El triaje es cosa nuestra: al afiliado no le dice nada y sólo añade
        // ruido a una pantalla en la que ya está nervioso.
        var triaje = new ReglaClienteVm
        {
            Familia = "Triaje", Resultado = "REQUIERE_CH",
            Regla = "Matriz de triaje (Count CH vs Count Negativa)"
        };

        Assert.False(triaje.SeLePuedeContar);
        Assert.Null(triaje.ParaElCliente);
    }

    [Fact]
    public void La_misma_familia_se_dice_distinto_segun_como_saliera()
    {
        var ok = new ReglaClienteVm { Familia = "Preexistencias/Carencias", Resultado = "CUMPLE",
                                      Regla = "Carencia ambulatoria / preexistencias" };
        var ch = new ReglaClienteVm { Familia = "Preexistencias/Carencias", Resultado = "REQUIERE_CH",
                                      Regla = "Carencia ambulatoria / preexistencias" };

        Assert.Contains("No tiene", ok.ParaElCliente);
        Assert.NotEqual(ok.ParaElCliente, ch.ParaElCliente);
    }

    [Fact]
    public void Con_importe_calculado_vuelve_a_haber_cifra()
    {
        var r = CasoReal();
        r.EstadoPropuesto    = "LIQUIDA_AUTO";
        r.TotalCubierto      = 382.46m;
        r.TotalDeducible     = 48.00m;
        r.TotalPendiente     = 0m;

        Assert.False(r.SinCifraTodavia);
        Assert.Equal("Le devolvemos", r.TitularCifra);
        Assert.Equal(334.46m, r.Devolvemos);
    }

    [Fact]
    public void Una_negativa_de_verdad_sigue_diciendose_como_negativa()
    {
        var r = CasoReal();
        r.EstadoPropuesto = "NEGATIVA";
        r.TotalPendiente  = 0m;

        Assert.False(r.SinCifraTodavia);
        Assert.Equal("No procede reembolso", r.TitularCifra);
    }
}
