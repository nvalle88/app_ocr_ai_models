using System;
using System.Collections.Generic;
using System.Linq;
using app_ocr_ai_models.Areas.Studio.Models;
using Xunit;

namespace app_ocr_ai_models.Tests;

// =============================================================================
// REQ-020f — Lo que el afiliado LEE en cada tarjeta.
//
// Estas pruebas existen porque la verificación anterior fue floja: comprobé el
// texto reimplementando la lógica en Python y contrastándola con la base. Eso
// demuestra que las frases se leen bien, no que el C# las produzca. Aquí se
// ejercita la clase de verdad.
//
// Los casos salen de los datos reales de db-nexus-test: de 35 documentos
// clasificados, 16 NO son factura válida y 6 caían en "otro" o "documento de
// respaldo" — que es exactamente lo que el usuario vio como "no clasifica nada".
// =============================================================================

public class TarjetaDocumentoClienteTests
{
    private static ClienteDocumentoVm Doc(
        bool leido = true,
        bool tieneTexto = true,
        bool esFactura = false,
        string? tipo = null,
        string? soporte = null,
        string? emisorTipo = null,
        string? resumen = null,
        string? nombre = "documento.pdf") =>
        new()
        {
            DocId = 1,
            Nombre = nombre,
            Leido = leido,
            TieneTexto = tieneTexto,
            EsFactura = esFactura,
            Tipo = tipo,
            TipoSoporte = soporte,
            EmisorTipo = emisorTipo,
            Resumen = resumen
        };

    // ── Lo que SÍ sirve para cobrar ──────────────────────────────────────

    [Fact]
    public void Una_factura_de_farmacia_se_nombra_y_se_dice_que_sirve()
    {
        var d = Doc(esFactura: true, tipo: "CON_MED-FACTURA", emisorTipo: "FARMACIA");

        Assert.Equal("Factura de farmacia", d.Titular);
        Assert.Equal("Con esto podemos reembolsarle.", d.Consecuencia);
        Assert.Equal("ok", d.Tono);
        Assert.Null(d.QueHacer);          // si está bien, no se le da la lata
    }

    [Fact]
    public void Una_factura_de_laboratorio_dice_de_que_prestador_es()
    {
        var d = Doc(esFactura: true, tipo: "CON_AMB-FACTURA", emisorTipo: "LABORATORIO");
        Assert.Equal("Factura de laboratorio", d.Titular);
    }

    // ── Lo que NO sirve por sí solo: el caso que motivó todo ─────────────

    [Fact]
    public void Una_receta_dice_QUE_ES_y_QUE_FALTA()
    {
        // El usuario: "no pone esto es una orden de laboratorio, necesita una
        // factura para justificar su pago". El titular es la cosa; la
        // consecuencia es lo que le falta para cobrar.
        var d = Doc(soporte: "RECETA_MEDICA");

        Assert.Equal("Receta médica", d.Titular);
        Assert.Contains("factura de la farmacia", d.Consecuencia);
        Assert.Equal("aviso", d.Tono);
    }

    [Fact]
    public void Una_orden_de_laboratorio_pide_la_factura_del_laboratorio()
    {
        var d = Doc(soporte: "ORDEN_PROCEDIMIENTO");

        Assert.Equal("Orden de procedimiento", d.Titular);
        Assert.Contains("laboratorio", d.Consecuencia);
    }

    [Fact]
    public void Una_epicrisis_pide_la_factura_del_hospital()
    {
        var d = Doc(soporte: "EPICRISIS");

        Assert.Equal("Epicrisis", d.Titular);
        Assert.Contains("factura del hospital", d.Consecuencia);
    }

    // ── Lo que antes salía como "otro" ───────────────────────────────────

    [Theory]
    [InlineData("OTRO")]
    [InlineData("")]
    [InlineData(null)]
    public void Lo_no_reconocido_se_admite_en_vez_de_decir_otro(string? soporte)
    {
        var d = Doc(soporte: soporte);

        Assert.Equal("No reconocimos este documento", d.Titular);
        Assert.DoesNotContain("respaldo", d.Titular, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Si_el_resumen_dice_que_no_es_medico_se_dice_de_frente()
    {
        // Caso real de la base: alguien subió un pedido de Amazon. El
        // clasificador ya lo había deducido; sólo faltaba enseñarlo.
        var d = Doc(soporte: "OTRO",
            resumen: "Resumen de pedido de Amazon por compra de prenda de vestir (polo/uniforme); "
                   + "no es factura medica ni gasto de salud.");

        Assert.Equal("No reconocimos este documento", d.Titular);
        Assert.Contains("no parece un gasto médico", d.Consecuencia);
        Assert.Contains("quítelo", d.Consecuencia);
    }

    [Fact]
    public void Si_el_resumen_dice_que_SI_respalda_el_gasto_no_se_le_contradice()
    {
        // También real: "Certificado de asistencia que confirma que el paciente
        // acudió a evaluación y terapia psicológica, respaldando el gasto".
        // Cae en OTRO pero SÍ es médico: afirmar lo contrario sería mentirle.
        var d = Doc(soporte: "OTRO",
            resumen: "Certificado de asistencia que confirma que el paciente acudio a evaluacion "
                   + "y terapia psicologica desde noviembre 2025, respaldando el gasto.");

        Assert.DoesNotContain("no parece un gasto médico", d.Consecuencia);
        Assert.Contains("respaldo", d.Consecuencia, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sin_resumen_no_se_afirma_que_no_sea_medico()
    {
        // Si el clasificador no dedujo nada, no se inventa un veredicto.
        var d = Doc(soporte: "OTRO", resumen: null);

        Assert.DoesNotContain("no parece un gasto médico", d.Consecuencia);
    }

    [Fact]
    public void Los_tipos_que_faltaban_ya_tienen_nombre()
    {
        Assert.Equal("Solicitud de cobertura", Doc(soporte: "SOLICITUD_COBERTURA_SALUDSA").Titular);
        Assert.Equal("Detalle de honorarios",  Doc(soporte: "HONORARIOS_DESGLOSADOS").Titular);
        Assert.Equal("Hoja 008 de emergencia", Doc(soporte: "HOJA_008").Titular);
    }

    // ── Las fotos de iPhone ──────────────────────────────────────────────

    [Fact]
    public void Una_foto_de_iphone_ilegible_da_el_ajuste_del_telefono()
    {
        // Medido: de 19 ficheros .heic sólo 5 se leyeron. "Suba mejor la foto"
        // no arregla nada; el ajuste del teléfono sí.
        var d = Doc(tieneTexto: false, nombre: "IMG_4821.HEIC");

        Assert.Equal("No pudimos leerlo", d.Titular);
        Assert.Contains("Más compatible", d.QueHacer);
        Assert.Equal("error", d.Tono);
    }

    [Fact]
    public void Otro_ilegible_recibe_el_consejo_general()
    {
        var d = Doc(tieneTexto: false, nombre: "escaneo.pdf");

        Assert.Equal("No pudimos leerlo", d.Titular);
        Assert.Contains("mejor luz", d.QueHacer);
        Assert.DoesNotContain("iPhone", d.QueHacer);
    }

    [Fact]
    public void Mientras_no_se_ha_leido_no_se_afirma_nada()
    {
        var d = Doc(leido: false);

        Assert.Equal("Identificando el documento…", d.Titular);
        Assert.Null(d.Consecuencia);      // no se inventa un veredicto
        Assert.Equal("espera", d.Tono);
    }

    // ── ¿De quién es la factura? (REQ-020j/k) ────────────────────────────

    [Fact]
    public void Si_la_factura_es_de_otro_beneficiario_se_ofrece_el_cambio()
    {
        // No se rechaza: el afiliado subió la factura correcta y se equivocó en
        // un desplegable. Hacerle perder quince días por eso sería absurdo.
        var d = Doc(esFactura: true, tipo: "CON_MED-FACTURA", emisorTipo: "FARMACIA");
        d.PacienteCoincide      = "OTRO_BENEFICIARIO";
        d.PacienteNumeroPersona = 1322956;
        d.PacienteNombre        = "Lia Andrea Chavez Borja";

        Assert.True(d.EsDeOtroBeneficiario);
        Assert.False(d.EsDeFueraDelPlan);
        Assert.Contains("Lia Andrea Chavez Borja", d.AvisoPersona);
        Assert.Contains("no de quien eligió", d.AvisoPersona);
    }

    [Fact]
    public void Sin_numero_de_persona_no_se_ofrece_un_cambio_que_no_se_puede_hacer()
    {
        // El botón manda el numeroPersona: sin él no hay a quién cambiar.
        var d = Doc(esFactura: true);
        d.PacienteCoincide = "OTRO_BENEFICIARIO";

        Assert.False(d.EsDeOtroBeneficiario);
    }

    [Fact]
    public void Una_factura_de_alguien_ajeno_al_plan_se_dice_sin_rodeos()
    {
        var d = Doc(esFactura: true);
        d.PacienteCoincide = "FUERA_DEL_PLAN";

        Assert.True(d.EsDeFueraDelPlan);
        Assert.Contains("no corresponde a ninguna persona", d.AvisoPersona);
    }

    [Fact]
    public void Si_coincide_no_se_le_da_la_lata()
    {
        var d = Doc(esFactura: true);
        d.PacienteCoincide = "COINCIDE";

        Assert.Null(d.AvisoPersona);
    }

    [Fact]
    public void Si_no_se_pudo_saber_no_se_acusa_a_nadie()
    {
        // Muchas facturas de farmacia no llevan nombre de paciente. Ausencia de
        // prueba no es prueba de nada.
        var d = Doc(esFactura: true);
        d.PacienteCoincide = "NO_SE_PUDO";

        Assert.Null(d.AvisoPersona);
        Assert.False(d.EsDeFueraDelPlan);
    }

    // ── El veredicto se compara, no se recuerda (caso d78ecb96) ──────────

    [Fact]
    public void Al_aceptar_el_cambio_el_aviso_desaparece_sin_reclasificar()
    {
        // Lo medido en el portal: el afiliado pulsaba «Sí, el reembolso es para
        // Martha», la solicitud cambiaba de verdad... y la tarjeta seguía
        // diciendo «parece ser de Martha, no de quien eligió» y volvía a
        // ofrecer cambiar a la persona ya elegida. El veredicto guardado era de
        // antes del cambio.
        var d = Doc(esFactura: true);
        d.PacienteCoincide      = "OTRO_BENEFICIARIO";   // lo que quedó grabado
        d.PacienteNumeroPersona = 5195852;               // Martha, en el papel
        d.PacienteNombre        = "Martha Adriana Cume Ortiz";
        d.BeneficiarioElegido   = 5195852;               // y ya es la elegida

        Assert.False(d.EsDeOtroBeneficiario);
        Assert.Null(d.AvisoPersona);
    }

    [Fact]
    public void Si_cambia_a_otra_persona_el_aviso_vuelve_aunque_dijera_COINCIDE()
    {
        // El reverso: la clasificación decía que coincidía porque entonces
        // estaba elegida Martha. Si ahora elige al hijo, vuelve a no cuadrar.
        var d = Doc(esFactura: true);
        d.PacienteCoincide      = "COINCIDE";
        d.PacienteNumeroPersona = 5195852;
        d.PacienteNombre        = "Martha Adriana Cume Ortiz";
        d.BeneficiarioElegido   = 5245038;               // Adrian

        Assert.True(d.EsDeOtroBeneficiario);
        Assert.Contains("Martha Adriana Cume Ortiz", d.AvisoPersona);
    }

    [Fact]
    public void A_un_informe_no_se_le_llama_factura()
    {
        // Salía «Esta factura parece ser de Martha…» sobre el informe de
        // patología y sobre el de la colonoscopia, que no son facturas.
        var d = Doc(esFactura: false);
        d.Leido                 = true;
        d.TipoSoporte           = "RESULTADO_LABORATORIO";
        d.PacienteNumeroPersona = 5195852;
        d.PacienteNombre        = "Martha Adriana Cume Ortiz";
        d.BeneficiarioElegido   = 5245038;

        Assert.True(d.EsDeOtroBeneficiario);
        Assert.DoesNotContain("Esta factura", d.AvisoPersona);
        Assert.Contains("Este documento", d.AvisoPersona);
    }

    // ── Un respaldo se juzga con el expediente delante ───────────────────

    [Fact]
    public void Con_la_factura_ya_subida_no_se_le_pide_otra_vez()
    {
        // Medido: subió la factura del Dr. Muñoz —colonoscopia y dos biopsias—
        // y los dos informes le pedían «necesitamos además la factura del
        // laboratorio». Ya la tenía delante.
        var d = Doc(esFactura: false);
        d.Leido       = true;
        d.TipoSoporte = "RESULTADO_LABORATORIO";
        d.HayFacturaEnElCaso = true;

        Assert.DoesNotContain("necesitamos además", d.Consecuencia);
        Assert.Contains("ya nos la subió", d.Consecuencia);
    }

    [Fact]
    public void Sin_factura_en_el_expediente_si_se_le_pide()
    {
        var d = Doc(esFactura: false);
        d.Leido       = true;
        d.TipoSoporte = "RESULTADO_LABORATORIO";
        d.HayFacturaEnElCaso = false;

        Assert.Contains("necesitamos además", d.Consecuencia);
    }

    [Fact]
    public void Aunque_haya_factura_una_cedula_sigue_sin_ser_un_gasto()
    {
        // El arm nuevo no puede tragarse los casos que no son respaldos de
        // gasto: la cédula sirve para identificar, nunca para cobrar.
        var d = Doc(esFactura: false);
        d.Leido       = true;
        d.TipoSoporte = "CEDULA_IDENTIDAD";
        d.HayFacturaEnElCaso = true;

        Assert.Contains("identificarle", d.Consecuencia);
    }
}

// =============================================================================
// El momento de la pantalla: qué se le pide al afiliado en cada punto.
// =============================================================================

public class MomentoPantallaTests
{
    private static ClienteSolicitudVm Vm(params ClienteDocumentoVm[] docs)
    {
        var vm = new ClienteSolicitudVm { CaseCode = Guid.NewGuid() };
        vm.Documentos.AddRange(docs);
        return vm;
    }

    private static ClienteDocumentoVm Factura() => new()
    { DocId = 1, Nombre = "f.pdf", Leido = true, TieneTexto = true, EsFactura = true, Tipo = "CON_MED-FACTURA" };

    private static ClienteDocumentoVm Receta() => new()
    { DocId = 2, Nombre = "r.pdf", Leido = true, TieneTexto = true, TipoSoporte = "RECETA_MEDICA" };

    private static ClienteDocumentoVm SinLeer() => new()
    { DocId = 3, Nombre = "x.pdf", Leido = false };

    [Fact]
    public void Sin_documentos_se_le_pide_la_factura()
    {
        var vm = Vm();
        Assert.Equal("SUBIR", vm.Momento);
        Assert.Equal("Adjunte la factura de su gasto médico", vm.Instruccion);
    }

    [Fact]
    public void Con_algo_sin_leer_se_dice_que_se_esta_identificando()
    {
        var vm = Vm(Factura(), SinLeer());
        Assert.Equal("LEYENDO", vm.Momento);
    }

    [Fact]
    public void Solo_una_receta_avisa_de_que_falta_la_factura()
    {
        // Es la prueba de fuego del caso real: el afiliado sube la receta
        // creyendo que basta.
        var vm = Vm(Receta());

        Assert.Equal("FALTA_FACTURA", vm.Momento);
        Assert.Equal("Nos falta la factura para poder continuar", vm.Instruccion);
    }

    [Fact]
    public void Con_la_factura_ya_se_puede_calcular()
    {
        var vm = Vm(Factura(), Receta());

        Assert.Equal("REVISAR", vm.Momento);
        Assert.Equal("Ya podemos calcular su reembolso", vm.Instruccion);
    }

    [Fact]
    public void Confirmada_la_solicitud_ya_no_pide_nada()
    {
        var vm = Vm(Factura());
        vm.DatosConfirmados = true;

        Assert.Equal("ENVIADO", vm.Momento);
    }

    [Fact]
    public void Los_requisitos_se_marcan_contra_lo_que_ya_trajo()
    {
        var vm = Vm(Factura());
        var factura = vm.Requisitos.First(q => q.Documento.Contains("factura", StringComparison.OrdinalIgnoreCase));

        Assert.True(factura.Cumplido);
        Assert.True(factura.Obligatorio);

        // Es factura de medicinas: la receta pasa a ser imprescindible y falta.
        var receta = vm.Requisitos.FirstOrDefault(q => q.Documento.Contains("receta", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(receta);
        Assert.True(receta!.Obligatorio);
        Assert.False(receta.Cumplido);
    }
    [Fact]
    public void Cuando_falta_la_factura_se_dice_donde_conseguirla()
    {
        var vm = Vm(Receta());
        var pistas = vm.DondeBuscarLaFactura;

        Assert.NotEmpty(pistas);
        // Lo que sirve siempre, en Ecuador: la factura electrónica llega por
        // correo el mismo día.
        Assert.Contains(pistas, x => x.Contains("correo electrónico"));
        Assert.Contains(pistas, x => x.Contains("SRI"));
    }

    [Fact]
    public void Si_sabemos_el_prestador_se_le_nombra()
    {
        // "Pida su factura" no ayuda; "pídala en FYBECA" sí.
        var receta = Receta();
        receta.Emisor = "FYBECA";
        var vm = Vm(receta);

        Assert.Contains(vm.DondeBuscarLaFactura, x => x.Contains("FYBECA"));
    }

}
