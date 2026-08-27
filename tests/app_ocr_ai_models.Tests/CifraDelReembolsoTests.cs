using app_ocr_ai_models.Areas.Studio.Models;
using Xunit;

namespace app_ocr_ai_models.Tests;

// =============================================================================
// La cifra grande de la pantalla del afiliado.
//
// Es la única línea de toda la aplicación que una persona va a leer como una
// promesa de dinero. Las cifras de estas pruebas son las REALES del caso
// d78ecb96 — contrato 549616, factura de $478,08 del Dr. Muñoz por una
// colonoscopia y dos biopsias, con Martha a $48 de terminar su deducible.
// =============================================================================
public class CifraDelReembolsoTests
{
    /// <summary>La resolución tal cual la escribió el modelo, campo a campo.</summary>
    private static ResolucionClienteVm CasoReal() => new()
    {
        EstadoPropuesto    = "LIQUIDA_AUTO",
        TotalPresentado    = 478.08m,
        TotalCubierto      = 382.46m,   // 286,46 + 96,00 de los dos ítems
        TotalDeducible     = 48.00m,    //  40,44 +  7,56
        TotalCopago        = 0m,
        TotalNoCubierto    = 95.62m,    //  71,62 + 24,00
        TotalEstimadoPagar = 344.06m    // lo que dijo el modelo: 9,60 de más
    };

    [Fact]
    public void No_se_anuncia_lo_cubierto_como_si_fuera_lo_que_se_recibe()
    {
        // El defecto medido: la pantalla decía «Le devolvemos $382,46» cuando
        // ese valor todavía no descuenta el deducible que le queda por gastar.
        var r = CasoReal();

        Assert.NotEqual(r.TotalCubierto, r.Devolvemos);
        Assert.Equal(334.46m, r.Devolvemos);
    }

    [Fact]
    public void La_cifra_cuadra_con_el_desglose_que_se_pinta_al_lado()
    {
        // Debajo del titular se imprimen deducible y no cubierto. Si los tres
        // números no suman lo presentado, el afiliado hace la resta y no le da.
        var r = CasoReal();

        Assert.Equal(r.TotalPresentado,
                     r.Devolvemos + r.TotalDeducible + r.TotalCopago + r.TotalNoCubierto);
    }

    [Fact]
    public void El_descuadre_del_modelo_se_detecta_y_se_mide()
    {
        var r = CasoReal();

        Assert.False(r.CuadraElPagoDelModelo);
        Assert.Equal(9.60m, r.DesviacionDelModelo);
    }

    [Fact]
    public void Cuando_el_modelo_acierta_no_se_marca_descuadre()
    {
        var r = CasoReal();
        r.TotalEstimadoPagar = 334.46m;

        Assert.True(r.CuadraElPagoDelModelo);
        Assert.Equal(0m, r.DesviacionDelModelo);
    }

    [Fact]
    public void Sin_valor_del_modelo_no_se_acusa_descuadre()
    {
        // Una resolución que no trae el campo no está equivocada: no opina.
        var r = CasoReal();
        r.TotalEstimadoPagar = null;

        Assert.True(r.CuadraElPagoDelModelo);
        Assert.Equal(334.46m, r.Devolvemos);
    }

    [Fact]
    public void Un_deducible_que_se_lo_come_todo_da_cero_y_no_negativo()
    {
        // Alguien que empieza el año presenta $80 con deducible de $100: no
        // cobra nada, pero tampoco puede salir una cifra en rojo en pantalla.
        var r = new ResolucionClienteVm
        {
            TotalPresentado = 80m,
            TotalCubierto   = 64m,
            TotalDeducible  = 100m,
            TotalNoCubierto = 16m
        };

        Assert.Equal(0m, r.Devolvemos);
    }
}
