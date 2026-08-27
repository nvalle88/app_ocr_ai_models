using System.Collections.Generic;
using System.Linq;
using app_ocr_ai_models.Areas.Studio.Models;
using Xunit;

namespace app_ocr_ai_models.Tests;

// =============================================================================
// El árbol: qué factura tiene qué respaldos.
//
// El afiliado sube tres ficheros y la pantalla le devolvía tres tarjetas
// iguales. La pregunta que se hace —«¿esto sirve junto o me falta algo?»— no
// la respondía nadie, aunque el expediente YA emparejaba los documentos y lo
// guardaba en `vinculos`.
//
// Cifras del caso d78ecb96: factura 10842 del Dr. Muñoz, respaldada por el
// informe de patología (10843) y el de colonoscopia (10844).
// =============================================================================
public class ArbolDocumentosTests
{
    private static ClienteDocumentoVm Doc(int id, bool factura, int? respaldaA = null,
                                          string? emisor = null) => new()
    {
        DocId = id,
        Leido = true,
        EsFactura = factura,
        Emisor = emisor,
        RespaldaADocId = respaldaA,
        Nombre = "doc" + id + ".pdf"
    };

    [Fact]
    public void Cada_respaldo_cuelga_de_su_factura()
    {
        var vm = new ClienteSolicitudVm
        {
            Documentos =
            {
                Doc(10842, factura: true),
                Doc(10843, factura: false, respaldaA: 10842),
                Doc(10844, factura: false, respaldaA: 10842),
            }
        };

        var grupos = vm.Agrupados;

        Assert.Single(grupos);
        Assert.Equal(10842, grupos[0].Factura!.DocId);
        Assert.Equal(new[] { 10843, 10844 }, grupos[0].Respaldos.Select(r => r.DocId));
    }

    [Fact]
    public void Dos_facturas_no_se_mezclan_sus_respaldos()
    {
        var vm = new ClienteSolicitudVm
        {
            Documentos =
            {
                Doc(1, factura: true),
                Doc(2, factura: true),
                Doc(3, factura: false, respaldaA: 2),
            }
        };

        var grupos = vm.Agrupados;

        Assert.Equal(2, grupos.Count);
        Assert.Empty(grupos.Single(g => g.Factura!.DocId == 1).Respaldos);
        Assert.Single(grupos.Single(g => g.Factura!.DocId == 2).Respaldos);
    }

    [Fact]
    public void Un_respaldo_sin_factura_se_señala_y_se_dice_a_quien_pedirla()
    {
        // Lo que reportó el usuario: subir un soporte solo y que la pantalla no
        // explique que hace falta la factura.
        var vm = new ClienteSolicitudVm
        {
            Documentos = { Doc(10843, factura: false, emisor: "Laboratorio de Patología Dr. Fernando Camacho A.") }
        };

        var grupos = vm.Agrupados;

        Assert.Single(grupos);
        Assert.True(grupos[0].EsHuerfano);
        Assert.Contains("no encontramos la factura", grupos[0].Aviso);
        Assert.Equal("Laboratorio de Patología Dr. Fernando Camacho A.", grupos[0].AQuienPedirla);
    }

    [Fact]
    public void Nunca_se_manda_al_afiliado_a_pedirle_la_factura_a_quien_solo_firma()
    {
        // Medido en add12bcc: los respaldos traen EmisorNombre nulo —el
        // clasificador solo lo rellena en las facturas— y la pantalla mandaba a
        // pedirle la factura a la patologa que firma el informe, que no emite
        // nada. Mandar a la puerta equivocada es peor que no dar puerta.
        var doc = Doc(10870, factura: false);
        doc.Medico = "DRA. IVETT CARIDAD MANZANARES LAGUARDIA";

        var g = new ClienteSolicitudVm { Documentos = { doc } }.Agrupados.Single();

        Assert.True(g.EsHuerfano);
        Assert.Null(g.AQuienPedirla);
        Assert.Equal("DRA. IVETT CARIDAD MANZANARES LAGUARDIA", g.QuienLoFirma);
    }

    [Fact]
    public void Con_varios_emisores_se_nombran_todos_y_no_se_elige_uno()
    {
        var a = Doc(1, factura: false, emisor: "Laboratorio Camacho");
        var b = Doc(2, factura: false, emisor: "Gastromuñoz");

        var g = new ClienteSolicitudVm { Documentos = { a, b } }.Agrupados.Single();

        Assert.Contains("Laboratorio Camacho", g.AQuienPedirla);
        Assert.Contains("Gastromuñoz", g.AQuienPedirla);
    }

    [Fact]
    public void Sabiendo_quien_emite_no_se_habla_del_que_firma()
    {
        var d = Doc(1, factura: false, emisor: "Laboratorio Camacho");
        d.Medico = "DRA. IVETT";

        var g = new ClienteSolicitudVm { Documentos = { d } }.Agrupados.Single();

        Assert.Equal("Laboratorio Camacho", g.AQuienPedirla);
        Assert.Null(g.QuienLoFirma);
    }

    [Fact]
    public void Sin_expediente_todavia_no_se_cuelga_nada_a_la_fuerza()
    {
        // Recién subidos no hay vínculos. Colgar el respaldo de la única
        // factura porque "seguro que es esa" sería inventarlo: puede ser de
        // otro gasto, y el afiliado se fiaría de un emparejamiento que nadie
        // comprobó.
        var vm = new ClienteSolicitudVm
        {
            Documentos =
            {
                Doc(1, factura: true),
                Doc(2, factura: false),      // sin RespaldaADocId
            }
        };

        var grupos = vm.Agrupados;

        Assert.Equal(2, grupos.Count);
        Assert.Empty(grupos[0].Respaldos);
        Assert.True(grupos[1].EsHuerfano);
    }

    [Fact]
    public void Un_respaldo_que_apunta_a_una_factura_que_ya_no_esta_queda_suelto()
    {
        // El afiliado puede borrar la factura equivocada y dejar el vínculo
        // apuntando al vacío. Debe salir como huérfano, no desaparecer.
        var vm = new ClienteSolicitudVm
        {
            Documentos = { Doc(2, factura: false, respaldaA: 999) }
        };

        Assert.True(vm.Agrupados.Single().EsHuerfano);
    }
}
