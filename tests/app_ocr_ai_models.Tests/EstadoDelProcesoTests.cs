using System;
using System.Collections.Generic;
using app_ocr_ai_models.Areas.Studio.Models;
using Xunit;

namespace app_ocr_ai_models.Tests;

// =============================================================================
// Cuándo un paso está hecho.
//
// Los números vienen del caso 7b99e00e, donde se reprodujo el fallo que
// reportó el usuario: "se queda congelado cuando se suben documentos, en
// identificar cada documento". Se subió 1 PDF (id 10863), se clasificó; se
// subieron 2 más (10864, 10865) y se quedaron SIN CLASIFICAR para siempre.
// =============================================================================
public class EstadoDelProcesoTests
{
    // ── La tipificación ──────────────────────────────────────────────────

    [Fact]
    public void Con_un_documento_sin_tipificar_el_paso_NO_esta_hecho()
    {
        // El fallo exacto: había una clasificación (la del 10863) y el paso se
        // daba por hecho, así que el arranque automático se saltaba la
        // tipificación y los otros dos no se leían nunca.
        var docs  = new[] { 10863, 10864, 10865 };
        var hecho = new[] { 10863 };

        Assert.False(EstadoDelProceso.TodosTipificados(docs, hecho));
    }

    [Fact]
    public void Con_todos_tipificados_el_paso_esta_hecho()
    {
        var docs = new[] { 10863, 10864, 10865 };

        Assert.True(EstadoDelProceso.TodosTipificados(docs, docs));
    }

    [Fact]
    public void Sin_documentos_no_hay_nada_que_dar_por_bueno()
    {
        Assert.False(EstadoDelProceso.TodosTipificados(Array.Empty<int>(), Array.Empty<int>()));
    }

    [Fact]
    public void Una_clasificacion_huerfana_no_tapa_un_documento_que_falta()
    {
        // Puede quedar clasificación de un documento ya borrado. Contar filas
        // en vez de comprobar cuáles daría el paso por hecho con un hueco.
        var docs  = new[] { 10864, 10865 };
        var hecho = new[] { 10863, 10864 };      // 10863 ya no existe; falta 10865

        Assert.False(EstadoDelProceso.TodosTipificados(docs, hecho));
    }

    // ── La vigencia de una nota ──────────────────────────────────────────

    [Fact]
    public void Un_documento_posterior_invalida_la_nota()
    {
        // Adjunta la factura que le faltaba: la resolución anterior ya no habla
        // de sus papeles.
        var nota  = new DateTime(2026, 8, 26, 21, 38, 52, DateTimeKind.Utc);
        var ultimo = new DateTime(2026, 8, 26, 22, 47, 24, DateTimeKind.Utc);

        Assert.False(EstadoDelProceso.NotaVigente(nota, ultimo));
    }

    [Fact]
    public void Una_nota_posterior_al_ultimo_documento_sigue_valiendo()
    {
        var ultimo = new DateTime(2026, 8, 26, 21, 29, 47, DateTimeKind.Utc);
        var nota   = new DateTime(2026, 8, 26, 21, 38, 52, DateTimeKind.Utc);

        Assert.True(EstadoDelProceso.NotaVigente(nota, ultimo));
    }

    [Fact]
    public void Sin_documentos_la_nota_no_puede_quedar_obsoleta()
    {
        Assert.True(EstadoDelProceso.NotaVigente(DateTime.UtcNow, null));
    }

    [Fact]
    public void Una_tipificacion_posterior_tambien_invalida_la_nota()
    {
        // La entrada del expediente no es el PDF, es lo que se dedujo de él.
        // En 7b99e00e el expediente se construyó con dos documentos aún sin
        // tipificar: su nota era posterior al último documento —pasaba por
        // buena por fecha— y describía un caso que ya no era el suyo.
        var ultimoDoc  = new DateTime(2026, 8, 26, 22, 47, 24, DateTimeKind.Utc);
        var nota       = new DateTime(2026, 8, 26, 22, 48, 10, DateTimeKind.Utc);
        var ultimaLect = new DateTime(2026, 8, 26, 23, 05, 31, DateTimeKind.Utc);

        // Sólo contra los documentos parecería vigente...
        Assert.True(EstadoDelProceso.NotaVigente(nota, ultimoDoc));
        // ...pero la frontera real es la entrada más reciente de las dos.
        Assert.False(EstadoDelProceso.NotaVigente(nota, ultimaLect));
    }

    // ── El invariante de la cadena ───────────────────────────────────────

    [Fact]
    public void Si_falla_la_tipificacion_no_vale_nada_de_lo_que_viene_detras()
    {
        // El expediente de 7b99e00e se construyó con dos de los tres documentos
        // sin clasificar, y su nota era posterior al último documento: por fecha
        // pasaba por buena. Un resultado calculado sobre papeles que aún no se
        // habían leído no es un resultado.
        var pasos = new List<PasoClienteVm>
        {
            new() { Numero = 1, Titulo = "Sus documentos",  Hecho = true  },
            new() { Numero = 2, Titulo = "Qué nos trajo",   Hecho = false },
            new() { Numero = 3, Titulo = "Su expediente",   Hecho = true  },
            new() { Numero = 4, Titulo = "Revisión médica", Hecho = true  },
            new() { Numero = 5, Titulo = "Qué le cubrimos", Hecho = true  },
        };

        EstadoDelProceso.PropagarPendientes(pasos);

        Assert.True (pasos[0].Hecho);
        Assert.False(pasos[1].Hecho);
        Assert.False(pasos[2].Hecho);   // el expediente cae
        Assert.False(pasos[3].Hecho);
        Assert.False(pasos[4].Hecho);
    }

    [Fact]
    public void Una_cadena_completa_se_queda_como_esta()
    {
        var pasos = new List<PasoClienteVm>
        {
            new() { Numero = 1, Hecho = true },
            new() { Numero = 2, Hecho = true },
            new() { Numero = 3, Hecho = true },
        };

        EstadoDelProceso.PropagarPendientes(pasos);

        Assert.All(pasos, p => Assert.True(p.Hecho));
    }
}
