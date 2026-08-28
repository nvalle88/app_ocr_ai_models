using System;
using System.Linq;
using app_ocr_ai_models.Areas.Studio.Models;
using Xunit;

namespace app_ocr_ai_models.Tests;

// =============================================================================
// Una factura no se paga dos veces.
//
// Esta regla decide si una solicitud entra o no entra, así que los dos errores
// posibles cuestan dinero en direcciones opuestas: dejar pasar un duplicado paga
// dos veces lo mismo, y bloquear de más le niega a alguien un reembolso legítimo.
// Por eso hay tests de las dos orillas, no solo de la que preocupa hoy.
//
// Los JSON de ejemplo salen de ToolInvocation de db-nexus-test, con la factura
// 001-100-000000916, que aparece en dos contratos distintos de la misma persona.
// =============================================================================
public class FacturaRepetidaTests
{
    [Fact]
    public void Sin_hallazgos_no_bloquea()
    {
        var v = FacturaRepetida.Juzgar("{\"rowCount\":0,\"rows\":[]}", "549616");

        Assert.False(v.Bloquea);
        Assert.Empty(v.Hallazgos);
        Assert.Equal(string.Empty, v.ParaElCliente);
    }

    [Fact]
    public void En_el_MISMO_contrato_bloquea_y_lo_dice()
    {
        // El caso real: 8 lineas en el contrato 549616, que es el que se presenta.
        const string real = """
        {"rowCount":1,"rows":[{"NumeroReclamo":2133124388,"ContratoNumero":549616,
          "Region":"Costa","CodigoProducto":"IND","EstadoReclamo":27,
          "FechaPresentacion":"2026-08-04 00:00","YaPagado":0,"Anulada":0}]}
        """;
        var v = FacturaRepetida.Juzgar(real, "549616");

        Assert.True(v.Bloquea);
        Assert.True(v.HayEnElMismoContrato);
        Assert.Contains("ya está registrada en su contrato", v.ParaElCliente);
    }

    [Fact]
    public void En_OTRO_contrato_tambien_bloquea()
    {
        // La factura es unica: da igual el contrato y da igual la persona.
        const string real = """
        {"rowCount":1,"rows":[{"NumeroReclamo":2133124323,"ContratoNumero":41215257,
          "Region":"Sierra","CodigoProducto":"COR","EstadoReclamo":27,
          "MontoPagadoDelReclamo":334.66,"YaPagado":0,"Anulada":0}]}
        """;
        var v = FacturaRepetida.Juzgar(real, "549616");

        Assert.True(v.Bloquea);
        Assert.False(v.HayEnElMismoContrato);
    }

    [Fact]
    public void No_filtra_el_contrato_ajeno_al_cliente()
    {
        const string real = """
        {"rowCount":1,"rows":[{"NumeroReclamo":2133124323,"ContratoNumero":41215257,
          "PersonaNumero":5195852,"EstadoReclamo":27,"Anulada":0}]}
        """;
        var v = FacturaRepetida.Juzgar(real, "549616");

        // El texto para el afiliado no puede llevar la poliza de otro.
        foreach (var secreto in new[] { "41215257", "5195852", "2133124323" })
            Assert.DoesNotContain(secreto, v.ParaElCliente);
    }

    // ── La otra orilla: bloquear de más también cuesta ───────────────────────

    [Fact]
    public void Un_reclamo_ANULADO_no_bloquea()
    {
        // Si se anulo, esa presentacion dejo de existir y la factura puede volver
        // a entrar. Tratarla como duplicado dejaria al afiliado sin cobrar algo
        // que nadie le pago.
        const string real = """
        {"rowCount":1,"rows":[{"NumeroReclamo":2133124323,"ContratoNumero":549616,
          "EstadoReclamo":10,"MontoPagadoDelReclamo":334.66,"YaPagado":0,"Anulada":1}]}
        """;
        var v = FacturaRepetida.Juzgar(real, "549616");

        Assert.False(v.Bloquea);
        Assert.True(v.SoloAnuladas);
    }

    [Fact]
    public void Anulada_y_viva_a_la_vez_bloquea_por_la_viva()
    {
        const string real = """
        {"rowCount":2,"rows":[
          {"NumeroReclamo":1,"ContratoNumero":549616,"EstadoReclamo":10,"Anulada":1},
          {"NumeroReclamo":2,"ContratoNumero":549616,"EstadoReclamo":27,"Anulada":0}]}
        """;
        var v = FacturaRepetida.Juzgar(real, "549616");

        Assert.True(v.Bloquea);
        Assert.False(v.SoloAnuladas);
        Assert.Single(v.Hallazgos);          // la anulada no cuenta como hallazgo
    }

    [Fact]
    public void Estado_27_bloquea_aunque_YaPagado_sea_cero()
    {
        // YaPagado solo mira el estado 10. En el caso medido las DOS apariciones
        // estaban en estado 27 con YaPagado=0: bloquear solo con YaPagado habria
        // dejado pasar la factura dos veces.
        const string real = """
        {"rowCount":1,"rows":[{"NumeroReclamo":2133124323,"ContratoNumero":549616,
          "EstadoReclamo":27,"YaPagado":0,"Anulada":0}]}
        """;
        Assert.True(FacturaRepetida.Juzgar(real, "549616").Bloquea);
    }

    // ── Ante la duda, NO bloquear ───────────────────────────────────────────

    [Fact]
    public void Una_respuesta_ilegible_no_bloquea()
    {
        // Negarle el reembolso a alguien porque no supimos leer un JSON seria el
        // peor de los dos errores.
        Assert.False(FacturaRepetida.Juzgar("esto no es json", "549616").Bloquea);
        Assert.False(FacturaRepetida.Juzgar(null, "549616").Bloquea);
        Assert.False(FacturaRepetida.Juzgar("{\"algo\":1}", "549616").Bloquea);
    }

    [Fact]
    public void Sin_saber_el_contrato_actual_sigue_bloqueando()
    {
        // No saber cual es el contrato en curso no puede convertir un duplicado
        // en un no-duplicado: solo se pierde el matiz de "mismo contrato".
        const string real = """
        {"rowCount":1,"rows":[{"NumeroReclamo":1,"ContratoNumero":549616,"Anulada":0}]}
        """;
        var v = FacturaRepetida.Juzgar(real, null);

        Assert.True(v.Bloquea);
        Assert.False(v.HayEnElMismoContrato);
    }

    [Theory]
    [InlineData("\"Anulada\":true")]
    [InlineData("\"Anulada\":1")]
    [InlineData("\"Anulada\":\"True\"")]
    [InlineData("\"Anulada\":\"1\"")]
    public void La_bandera_de_anulada_se_lee_venga_como_venga(string bandera)
    {
        // Segun de donde salga el JSON llega como bit, como bool o como texto.
        var json = "{\"rowCount\":1,\"rows\":[{\"NumeroReclamo\":1,\"ContratoNumero\":549616," + bandera + "}]}";
        Assert.False(FacturaRepetida.Juzgar(json, "549616").Bloquea);
    }
}
