using System;
using app_ocr_ai_models.Areas.Studio.Models;
using Xunit;

namespace app_ocr_ai_models.Tests;

// =============================================================================
// Lo que el afiliado LEE mientras el agente trabaja.
//
// Estas cadenas no son de un log: salen en la pantalla de una persona que está
// esperando a saber cuánto le devuelven. Un veredicto de más aquí es una
// reclamación, y un dato de otro afiliado aquí es un incidente.
//
// Las respuestas de ejemplo están copiadas de ToolInvocation de db-nexus-test,
// no inventadas: si el formato real cambia, estos tests se caen, que es
// exactamente lo que tienen que hacer.
// =============================================================================
public class NarradorDeToolsTests
{
    private static readonly DateTime T0 = new(2026, 8, 28, 10, 0, 0);
    private static readonly DateTime T1 = T0.AddSeconds(1.4);

    // ── Nunca el nombre técnico ─────────────────────────────────────────────

    [Theory]
    [InlineData("factura_ya_pagada_bd")]
    [InlineData("codigo_liquidacion_y_cobertura")]
    [InlineData("resolver_convenio_por_ruc")]
    [InlineData("una_tool_que_no_existe_todavia")]
    public void Nunca_ensena_el_codigo_de_la_herramienta(string code)
    {
        var v = NarradorDeTools.Narrar(1, code, "{\"rowCount\":0,\"rows\":[]}", false, T0, T1);

        Assert.DoesNotContain("_", v.Que);
        Assert.DoesNotContain(code, v.Que, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(v.Que));
    }

    // ── Nunca un dato de un tercero ─────────────────────────────────────────

    [Fact]
    public void Factura_ya_pagada_no_filtra_el_contrato_ni_la_persona_de_otro()
    {
        // Copiado de una respuesta real: el reclamo donde se pagó puede ser de
        // OTRO afiliado, con su contrato y su numero de persona dentro.
        const string real = """
        {"rowCount":1,"rows":[{"NumeroReclamo":2133124323,"NumeroAlcance":0,
        "Region":"Sierra","CodigoProducto":"COR","ContratoNumero":41215257,
        "PersonaNumero":5195852,"MontoPagadoDelReclamo":143.42,"YaPagado":1}]}
        """;

        var v = NarradorDeTools.Narrar(1, "factura_ya_pagada_bd", real, false, T0, T1);

        Assert.Equal("Esa factura ya consta presentada", v.Resultado);
        foreach (var secreto in new[] { "41215257", "5195852", "2133124323", "143.42", "Sierra" })
            Assert.DoesNotContain(secreto, v.Que + " " + v.Resultado);
    }

    [Fact]
    public void Sin_reclamos_dice_que_no_consta_pagada()
    {
        var v = NarradorDeTools.Narrar(1, "factura_ya_pagada_bd",
                                       "{\"rowCount\":0,\"rows\":[]}", false, T0, T1);
        Assert.Equal("No consta pagada antes", v.Resultado);
        Assert.Equal("ok", v.Tono);
    }

    // ── El SRI: el 200 con Estado Error no puede leerse como exito ──────────

    [Fact]
    public void Cargar_del_sri_con_estado_error_no_se_canta_como_exito()
    {
        // Este endpoint contesta HTTP 200 y Estado "Error" cuando el SRI no
        // devuelve el documento. Verificado contra pruebas.
        const string real = """
        {"Estado":"Error","Datos":null,
         "Mensajes":["SRI no devolvio ningun documento con la clave: 3003202601..."]}
        """;

        var v = NarradorDeTools.Narrar(1, "cargar_factura_desde_sri", real, false, T0, T1);

        Assert.Equal("El SRI no la reconoce", v.Resultado);
        Assert.Equal("atencion", v.Tono);
    }

    [Fact]
    public void Factura_en_el_repositorio_se_canta_como_encontrada()
    {
        const string real = """
        {"Estado":"OK","Datos":{"InfoTributaria":{"Ambiente":2,"Ruc":"0916422173001"}}}
        """;
        var v = NarradorDeTools.Narrar(1, "obtener_factura_repositorio", real, false, T0, T1);

        Assert.Equal("Está registrada y coincide", v.Resultado);
        Assert.Equal("ok", v.Tono);
    }

    // ── La cobertura: la cifra que le importa ───────────────────────────────

    [Fact]
    public void Cobertura_canta_el_porcentaje_real()
    {
        const string real = """
        {"rowCount":1,"rows":[{"CodigoProcedimiento":504001,
          "CodigoBeneficio":"A003","PorcentajeQueAplica":80.00,"Alerta":null}]}
        """;
        var v = NarradorDeTools.Narrar(1, "codigo_liquidacion_y_cobertura", real, false, T0, T1);

        Assert.Equal("Su plan cubre el 80%", v.Resultado);
    }

    [Fact]
    public void Con_alerta_de_ambiguedad_NO_se_canta_un_porcentaje()
    {
        // Medido: 1 de cada 3 llaves tiene porcentajes distintos segun la
        // cobertura. Cuando la tool avisa, decirle un numero al afiliado seria
        // decirle uno de varios posibles, al azar.
        const string real = """
        {"rowCount":2,"rows":[{"PorcentajeQueAplica":100.00,
          "Alerta":"AMBIGUO: no se paso codigoCobertura y este beneficio tiene porcentajes DISTINTOS"}]}
        """;
        var v = NarradorDeTools.Narrar(1, "codigo_liquidacion_y_cobertura", real, false, T0, T1);

        Assert.Equal("Necesita una comprobación más", v.Resultado);
        Assert.DoesNotContain("%", v.Resultado!);
    }

    [Fact]
    public void Sin_fila_de_plan_no_se_dice_que_no_cubre()
    {
        // "El plan no lista esa prestacion" NO es "no se cubre": es que no hay
        // fila. Confundirlos le niega a alguien algo que si tiene.
        var v = NarradorDeTools.Narrar(1, "codigo_liquidacion_y_cobertura",
                                       "{\"rowCount\":0,\"rows\":[]}", false, T0, T1);

        Assert.Equal("Su plan no lista esa prestación", v.Resultado);
        Assert.DoesNotContain("no cubre", v.Resultado!, StringComparison.OrdinalIgnoreCase);
    }

    // ── Cuando no se sabe leer, se calla ────────────────────────────────────

    [Fact]
    public void Respuesta_ilegible_no_inventa_veredicto()
    {
        var v = NarradorDeTools.Narrar(1, "obtener_factura_repositorio",
                                       "esto no es json", false, T0, T1);
        Assert.Null(v.Resultado);
    }

    [Fact]
    public void Herramienta_desconocida_no_inventa_veredicto_ni_nombre()
    {
        var v = NarradorDeTools.Narrar(1, "tool_nueva_de_manana", "{}", false, T0, T1);

        Assert.Equal("Consultando sus datos", v.Que);
        Assert.Null(v.Resultado);       // {} no trae rowCount: no se sabe, se calla
    }

    // ── El error: motivo humano, sin tripas ─────────────────────────────────

    [Fact]
    public void Un_fallo_no_ensena_el_error_tecnico()
    {
        const string tripas = """
        {"error":"System.Data.SqlClient.SqlException: Timeout expired. at Microsoft.Data..."}
        """;
        var v = NarradorDeTools.Narrar(1, "consultar_deducible_contrato", tripas, true, T0, T1);

        Assert.Equal("No se pudo consultar ahora", v.Resultado);
        Assert.Equal("fallo", v.Tono);
        Assert.DoesNotContain("Exception", v.Resultado!);
        Assert.DoesNotContain("Timeout", v.Resultado!);
    }

    // ── Todavia corriendo ───────────────────────────────────────────────────

    [Fact]
    public void Mientras_corre_no_hay_veredicto_ni_reloj()
    {
        var v = NarradorDeTools.Narrar(1, "obtener_factura_repositorio", null, false, T0, null);

        Assert.Equal("Comprobando su factura en el SRI", v.Que);
        Assert.Null(v.Resultado);
        Assert.Null(v.Segundos);
    }

    [Fact]
    public void Cuando_termina_reporta_lo_que_tardo()
    {
        var v = NarradorDeTools.Narrar(1, "obtener_factura_repositorio",
                                       "{\"Estado\":\"OK\"}", false, T0, T1);
        Assert.Equal(1.4, v.Segundos);
    }
}
