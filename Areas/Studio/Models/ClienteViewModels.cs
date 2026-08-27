using System;
using System.Collections.Generic;
using System.Linq;
using app_ocr_ai_models.Services.Ai;

namespace app_ocr_ai_models.Areas.Studio.Models;

// =============================================================================
// REQ-020 — Lo que ve el afiliado en el portal.
//
// Estos modelos existen aparte de los del auditor por una razón que no es
// técnica: el afiliado y el auditor no miran lo mismo. Al auditor le importa el
// tipo CON_MED-FACTURA y el umbral de homologación; al afiliado le importa si
// le devuelven su dinero y qué papel le falta. Reutilizar el modelo del auditor
// obligaría a esconder la mitad de los campos en la vista, que es como se
// terminan colando tecnicismos en pantallas de cliente.
// =============================================================================

/// <summary>Pantalla 1: la cédula.</summary>
public sealed class ClienteIdentificacionVm
{
    public string? Cedula { get; set; }
    public string? Error { get; set; }
}

/// <summary>Pantalla 2: sus contratos, para que elija uno.</summary>
public sealed class ClienteContratosVm
{
    public string Cedula { get; set; } = string.Empty;
    public Guid CaseCode { get; set; }
    public List<ContratoAfiliado> Contratos { get; set; } = new();

    /// <summary>El nombre del titular, para saludar por su nombre de pila.</summary>
    public string? NombrePila
    {
        get
        {
            var n = Contratos.FirstOrDefault()?.TitularNombre;
            return string.IsNullOrWhiteSpace(n) ? null : n!.Split(' ').FirstOrDefault();
        }
    }
}

/// <summary>Un documento adjuntado por el afiliado, con lo que ya se leyó de él.</summary>
public sealed class ClienteDocumentoVm
{
    public int DocId { get; set; }
    public string? Nombre { get; set; }
    public DateTime Subido { get; set; }

    /// <summary>Si la tipificación ya pasó por él.</summary>
    public bool Leido { get; set; }

    public string? Tipo { get; set; }
    public string? TipoSoporte { get; set; }
    public bool EsFactura { get; set; }
    public decimal? Valor { get; set; }
    public string? Emisor { get; set; }
    public string? NumeroDoc { get; set; }
    public DateTime? Fecha { get; set; }

    /// <summary>Por qué se decidió ese tipo, en la frase que dejó el clasificador.</summary>
    public string? PorQue { get; set; }

    /// <summary>HOSPITAL | CLINICA | FARMACIA | LABORATORIO | CONSULTORIO | CENTRO_IMAGEN…</summary>
    public string? EmisorTipo { get; set; }

    /// <summary>
    /// La frase con la que el clasificador describe el documento: qué es y a
    /// qué gasto respalda. Está poblada en 40 de 43 documentos reales y es lo
    /// único que le dice al afiliado QUÉ subió cuando el tipo no se reconoció.
    /// </summary>
    public string? Resumen { get; set; }

    // ── ¿De quién es? (REQ-020j) ──────────────────────────────────────────
    /// <summary>COINCIDE | OTRO_BENEFICIARIO | FUERA_DEL_PLAN | NO_SE_PUDO.</summary>
    public string? PacienteCoincide { get; set; }

    // ── Qué respalda a qué (del ExpedienteDocumental) ─────────────────────
    /// <summary>
    /// La factura a la que este documento da soporte, si el expediente lo
    /// emparejó. Es lo que convierte una pila de papeles en un caso: el
    /// afiliado sube tres ficheros y no tiene forma de saber si le sirven
    /// juntos o si cada uno va por su lado.
    /// </summary>
    public int? RespaldaADocId { get; set; }

    /// <summary>Por qué respalda a esa factura, en la frase del expediente.</summary>
    public string? PorQueRespalda { get; set; }

    /// <summary>La persona del contrato a la que corresponde, si se supo.</summary>
    public int? PacienteNumeroPersona { get; set; }

    /// <summary>Nombre de esa persona, para poder ofrecer el cambio.</summary>
    public string? PacienteNombre { get; set; }

    /// <summary>Por qué se decidió así (viene sin nombres del clasificador).</summary>
    public string? PacienteJustificacion { get; set; }

    /// <summary>
    /// A quién va dirigido el reembolso AHORA MISMO. Se compara contra esto en
    /// vez de creerle al veredicto guardado.
    /// </summary>
    public int? BeneficiarioElegido { get; set; }

    /// <summary>
    /// El documento es de otra persona del MISMO contrato. No es un rechazo:
    /// es un cambio de beneficiario a un clic, y por eso se distingue del caso
    /// en que la factura es de alguien ajeno al plan.
    ///
    /// Se DECIDE aquí, comparando, y no se lee de PacienteCoincide. Medido en
    /// el caso d78ecb96: el afiliado pulsaba «Sí, el reembolso es para Martha»,
    /// la solicitud cambiaba de verdad a Martha... y la tarjeta seguía diciendo
    /// «parece ser de Martha, no de quien eligió» y volvía a ofrecer cambiar a
    /// la persona YA elegida. El veredicto se calculó una vez, contra el
    /// beneficiario de entonces, y se quedó congelado en la base.
    ///
    /// Una coincidencia es una COMPARACIÓN entre dos cosas que cambian por
    /// separado: el paciente del papel (fijo) y el beneficiario elegido
    /// (cambia a un clic). Guardar el resultado en vez de los dos operandos
    /// obliga a reclasificar tres documentos cada vez que el afiliado cambia de
    /// persona. PacienteCoincide se conserva como lo que sí es: la evidencia de
    /// lo que el clasificador supo leer.
    /// </summary>
    public bool EsDeOtroBeneficiario
    {
        get
        {
            if (EsDeFueraDelPlan) return false;
            if (!PacienteNumeroPersona.HasValue) return false;

            // Sin beneficiario elegido no hay contra qué comparar: se respeta
            // lo que dijo el clasificador.
            if (!BeneficiarioElegido.HasValue)
                return string.Equals(PacienteCoincide, "OTRO_BENEFICIARIO",
                                     StringComparison.OrdinalIgnoreCase);

            return PacienteNumeroPersona.Value != BeneficiarioElegido.Value;
        }
    }

    /// <summary>La factura es de alguien que no está cubierto por el contrato.</summary>
    public bool EsDeFueraDelPlan =>
        string.Equals(PacienteCoincide, "FUERA_DEL_PLAN", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// El aviso sobre de quién es el documento. Sólo se habla cuando hay algo
    /// que decir: si coincide, no se le da la lata.
    /// </summary>
    public string? AvisoPersona
    {
        get
        {
            // «Esta factura» sobre un informe de patología es sencillamente
            // falso: en el caso medido el aviso salía igual sobre el resultado
            // de laboratorio y sobre el informe de la colonoscopia, que no son
            // facturas. Se le llama por su nombre, que ya lo tenemos.
            if (EsDeOtroBeneficiario)
            {
                var cabeza = Leido && !EsFactura
                    ? $"Este documento ({NombresDocumento.Soporte(TipoSoporte).ToLowerInvariant()})"
                    : "Esta factura";

                return string.IsNullOrWhiteSpace(PacienteNombre)
                    ? $"{cabeza} parece ser de otra persona de su plan, no de quien eligió."
                    : $"{cabeza} parece ser de {PacienteNombre}, no de quien eligió.";
            }

            if (EsDeFueraDelPlan)
                return "El nombre que aparece en este documento no corresponde a ninguna persona "
                     + "cubierta por su contrato, así que no podemos reembolsarlo.";

            return null;
        }
    }

    public string? EmisorRuc { get; set; }

    /// <summary>La clave de acceso de 49 dígitos del SRI.</summary>
    public string? ClaveAcceso { get; set; }

    /// <summary>El número de autorización del SRI.</summary>
    public string? NumeroAutorizacion { get; set; }

    /// <summary>El médico que firma, cuando el documento lo trae.</summary>
    public string? Medico { get; set; }

    /// <summary>
    /// Las marcas que el clasificador encontró en el documento. Ya se guardaban
    /// en DocumentoTag y no se enseñaban: son la prueba visible de que se leyó
    /// de verdad, y al afiliado le confirman de un vistazo que entendimos su
    /// papel.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Los rubros de la factura, uno por uno.</summary>
    public List<ItemClienteVm> Items { get; set; } = new();

    /// <summary>Los diagnósticos que el documento menciona.</summary>
    public List<DxClienteVm> Diagnosticos { get; set; } = new();

    /// <summary>
    /// Las comprobaciones del clasificador, en cristiano y pinchables.
    ///
    /// Los tags crudos son marcas técnicas —TIENE_CLAVE_ACCESO, DETALLE_ITEMS,
    /// TOTALES— que no significan nada para un afiliado. Aquí se traducen a lo
    /// que de verdad comprueban, y cada una lleva el texto con el que buscar su
    /// sitio en el documento.
    /// </summary>
    public List<VerificacionVm> Verificaciones
    {
        get
        {
            var v = new List<VerificacionVm>();
            bool tag(string t) => Tags.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase));

            if (tag("NUMERO_FACTURA") && !string.IsNullOrWhiteSpace(NumeroDoc))
                v.Add(new VerificacionVm("Número de factura", NumeroDoc!, NumeroDoc));

            if (tag("EMISOR_IDENTIFICADO") && !string.IsNullOrWhiteSpace(Emisor))
                v.Add(new VerificacionVm("Quién la emite", Emisor!, Emisor));

            if (!string.IsNullOrWhiteSpace(EmisorRuc))
                v.Add(new VerificacionVm("RUC del prestador", EmisorRuc!, EmisorRuc));

            // El dato, no una descripción: si del RUC se enseña 1708204365001,
            // de la clave de acceso hay que enseñar la clave. Decir "tiene la
            // clave" y esconderla no le deja comprobar nada.
            if (!string.IsNullOrWhiteSpace(ClaveAcceso))
                v.Add(new VerificacionVm("Clave de acceso del SRI", ClaveAcceso!, ClaveAcceso));
            else if (tag("CLAVE_ACCESO") || tag("TIENE_CLAVE_ACCESO"))
                v.Add(new VerificacionVm("Clave de acceso del SRI",
                    "consta en el documento", "CLAVE DE ACCESO"));

            if (!string.IsNullOrWhiteSpace(NumeroAutorizacion)
                && !string.Equals(NumeroAutorizacion, ClaveAcceso, StringComparison.Ordinal))
                v.Add(new VerificacionVm("Autorización del SRI",
                    NumeroAutorizacion!, NumeroAutorizacion));
            else if (string.IsNullOrWhiteSpace(NumeroAutorizacion) && tag("AUTORIZACION"))
                v.Add(new VerificacionVm("Autorización del SRI",
                    "consta en el documento", "AUTORIZACION"));

            if (Fecha.HasValue)
                v.Add(new VerificacionVm("Fecha de emisión",
                    Fecha.Value.ToString("dd/MM/yyyy"), Fecha.Value.ToString("dd/MM/yyyy")));

            if (Valor.HasValue && Valor.Value > 0)
                v.Add(new VerificacionVm("Total facturado",
                    "$ " + Valor.Value.ToString("N2"), Valor.Value.ToString("0.00")));

            if (!string.IsNullOrWhiteSpace(Medico))
                v.Add(new VerificacionVm("Médico tratante", Medico!, Medico));

            return v;
        }
    }

    /// <summary>Cuántas hojas tiene, para el visor.</summary>
    public int Paginas { get; set; }

    /// <summary>
    /// Qué hay en cada hoja. Un archivo puede traer la factura, la receta y la
    /// orden juntas; presentarlo como un bloque escondía la mitad.
    /// </summary>
    public List<HojaClienteVm> Hojas { get; set; } = new();

    /// <summary>El archivo trae más de una clase de documento dentro.</summary>
    public bool EsMixto => Hojas
        .Select(h => h.QueEs)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count() > 1;

    /// <summary>"2 facturas, 1 receta y 2 órdenes" — el resumen de la portada.</summary>
    public string? ResumenHojas
    {
        get
        {
            if (Hojas.Count == 0) return null;
            var partes = Hojas
                .GroupBy(h => h.QueEs, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Count() == 1
                    ? g.Key.ToLowerInvariant()
                    : $"{g.Count()} {Plural(g.Key)}")
                .ToList();

            if (partes.Count == 1) return partes[0];
            return string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes[^1];
        }
    }

    private static string Plural(string txt)
    {
        var t = txt.ToLowerInvariant();
        if (t.EndsWith("z")) return t[..^1] + "ces";
        if (t.EndsWith("s")) return t;
        return t.EndsWith("a") || t.EndsWith("e") || t.EndsWith("i")
            || t.EndsWith("o") || t.EndsWith("u") ? t + "s" : t + "es";
    }

    /// <summary>Si el OCR sacó algo de texto. Sin texto no hay nada que juzgar.</summary>
    public bool TieneTexto { get; set; }

    /// <summary>
    /// El veredicto en una palabra: FACTURA | NO_ES_FACTURA | ILEGIBLE.
    /// Es lo primero que tiene que ver el afiliado, porque es lo que decide si
    /// su reembolso puede avanzar o no.
    /// </summary>
    public string Veredicto =>
        !Leido            ? "PENDIENTE"
        : !TieneTexto     ? "ILEGIBLE"
        : EsFactura       ? "FACTURA"
                          : "NO_ES_FACTURA";

    /// <summary>
    /// QUÉ es este documento. Se dice el nombre de la cosa, no lo que no es:
    /// "Orden de laboratorio" le sirve al afiliado; "esto no es una factura"
    /// lo deja igual de perdido que antes de subirlo.
    /// </summary>
    public string Titular => Veredicto switch
    {
        "PENDIENTE" => "Identificando el documento…",
        "ILEGIBLE"  => "No pudimos leerlo",
        "FACTURA"   => TipoPrestador is null
                        ? "Factura médica"
                        : $"Factura de {TipoPrestador}",
        _           => EnCristiano
    };

    /// <summary>
    /// Si el expediente YA tiene una factura válida. Un respaldo no se juzga
    /// solo: lo que hay que pedirle al afiliado depende de lo demás que trajo.
    /// </summary>
    public bool HayFacturaEnElCaso { get; set; }

    /// <summary>
    /// Lo que se le dice a un respaldo cuando la factura ya está en el
    /// expediente. No se afirma que ESTA factura cubra ESTE respaldo —eso lo
    /// decide la revisión—, sólo se deja de pedirle un papel que ya entregó.
    /// </summary>
    private const string FacturaYaEsta =
        "Nos sirve de respaldo. El gasto se paga contra la factura, y esa ya nos la subió.";

    /// <summary>
    /// Qué significa para su reembolso. Es la parte que de verdad le importa:
    /// un documento puede ser perfectamente válido y aun así no servir para
    /// que le paguen, y eso hay que decírselo en la misma tarjeta.
    /// </summary>
    public string? Consecuencia => Veredicto switch
    {
        "FACTURA" => "Con esto podemos reembolsarle.",

        "ILEGIBLE" => "No podemos usarlo hasta que se lea bien.",

        "PENDIENTE" => null,

        // Los soportes clínicos: valen como respaldo, nunca como comprobante
        // del gasto. Decir sólo "es una orden" no basta; hay que decir qué
        // falta para cobrar.
        //
        // Pero eso sólo es cierto MIENTRAS no haya factura en el expediente.
        // Medido en el caso d78ecb96: el afiliado subió la factura del Dr.
        // Muñoz —colonoscopia y dos biopsias, $478,08— junto al informe de
        // patología y al de la colonoscopia, y las dos tarjetas de respaldo le
        // pedían «necesitamos además la factura del laboratorio». Ya la tenía
        // delante. Cada tarjeta se juzgaba a sí misma sin mirar el resto del
        // expediente, y el afiliado se queda buscando un papel que ya subió.
        // El arm va DENTRO de cada respaldo de gasto, no delante de todos: si
        // se pusiera antes se tragaría también la cédula de identidad y el
        // "esto no parece un gasto médico", que hay que seguir diciendo aunque
        // haya factura.
        _ when EsSoporteDe("MEDICINA") => HayFacturaEnElCaso ? FacturaYaEsta
            : "Nos sirve de respaldo, pero necesitamos además la factura de la farmacia para poder pagarle.",

        _ when EsSoporteDe("HOSPITAL") => HayFacturaEnElCaso ? FacturaYaEsta
            : "Nos sirve de respaldo, pero necesitamos además la factura del hospital para poder pagarle.",

        _ when EsSoporteDe("EXAMEN") => HayFacturaEnElCaso ? FacturaYaEsta
            : "Nos sirve de respaldo, pero necesitamos además la factura del laboratorio o centro de imagen para poder pagarle.",

        _ when string.Equals(TipoSoporte, "CEDULA_IDENTIDAD", StringComparison.OrdinalIgnoreCase) =>
            "Nos sirve para identificarle, pero no es un gasto: necesitamos la factura.",

        // No reconocido: se dice lo que el clasificador SÍ dedujo, y se juzga si
        // parece un gasto médico LEYENDO ese resumen. Hay documentos que caen en
        // OTRO y sí son médicos —"certificado de asistencia a terapia
        // psicológica… respaldando el gasto"—, así que afirmar "no es médico" a
        // ciegas sería mentirle.
        _ when string.IsNullOrWhiteSpace(TipoSoporte)
               || string.Equals(TipoSoporte, "OTRO", StringComparison.OrdinalIgnoreCase) =>
            NoParecePorGasto
                ? "Esto no parece un gasto médico, así que no podemos reembolsarlo. "
                + "Si se equivocó de archivo, quítelo y suba la factura correcta."
                : "No pudimos encajarlo en ningún tipo conocido. Lo guardamos como respaldo, "
                + "pero recuerde que sin la factura no se puede reembolsar el gasto.",

        _ => HayFacturaEnElCaso ? FacturaYaEsta
             : "Nos sirve de respaldo, pero por sí solo no basta: sin factura no se puede reembolsar el gasto."
    };

    /// <summary>
    /// Si el propio resumen del clasificador dice que NO es un gasto médico.
    /// Se lee de lo que él escribió en vez de suponerlo por el tipo: hay
    /// documentos en OTRO que sí respaldan un gasto.
    /// </summary>
    private bool NoParecePorGasto
    {
        get
        {
            var r = (Resumen ?? string.Empty).ToLowerInvariant();
            if (r.Length == 0) return false;      // sin resumen no se afirma nada

            // Si dice explícitamente que respalda el gasto, no se contradice.
            if (r.Contains("respalda el gasto") || r.Contains("respaldando el gasto"))
                return false;

            return r.Contains("no es un documento medico")  || r.Contains("no es un documento médico")
                || r.Contains("no es documento medico")     || r.Contains("no es documento médico")
                || r.Contains("no es factura medica")       || r.Contains("no es factura médica")
                || r.Contains("no es una factura medica")   || r.Contains("no es una factura médica")
                || r.Contains("no respalda ningun gasto")   || r.Contains("no respalda ningún gasto")
                || r.Contains("no respalda gasto")
                || r.Contains("ni gasto de salud");
        }
    }

    /// <summary>Agrupa los tipos de soporte por la clase de gasto que respaldan.</summary>
    private bool EsSoporteDe(string familia)
    {
        var t = (TipoSoporte ?? string.Empty).ToUpperInvariant();
        return familia switch
        {
            "MEDICINA" => t is "RECETA_MEDICA" or "KARDEX_MEDICAMENTOS",
            "HOSPITAL" => t is "EPICRISIS" or "HISTORIA_CLINICA" or "PROTOCOLO_QUIRURGICO"
                            or "RECORD_ANESTESIA" or "HOJA_008" or "HOJA_EVOLUCION",
            "EXAMEN"   => t is "ORDEN_PROCEDIMIENTO" or "RESULTADO_LABORATORIO"
                            or "RESULTADO_IMAGEN" or "INFORME_PROCEDIMIENTO" or "INFORME_TERAPIA",
            _ => false
        };
    }

    /// <summary>De qué clase de prestador es, cuando se pudo identificar.</summary>
    public string? TipoPrestador => (EmisorTipo ?? string.Empty).ToUpperInvariant() switch
    {
        "FARMACIA"      => "farmacia",
        "LABORATORIO"   => "laboratorio",
        "HOSPITAL"      => "hospital",
        "CLINICA"       => "clínica",
        "CENTRO_MEDICO" => "centro médico",
        "CONSULTORIO"   => "consultorio",
        "CENTRO_IMAGEN" => "centro de imagen",
        "CENTRO_TERAPIA"=> "centro de terapia",
        "ODONTOLOGIA"   => "consultorio odontológico",
        "OPTICA"        => "óptica",
        _               => null
    };

    /// <summary>
    /// Qué puede HACER ahora mismo. Sólo cuando hay una acción concreta: si no
    /// hay nada que hacer, no se le da la lata.
    /// </summary>
    public string? QueHacer => Veredicto switch
    {
        // Las fotos de iPhone llegan en HEIC y el motor de lectura falla con
        // ellas a menudo (medido: 5 legibles de 19). Decir "suba mejor la foto"
        // no arregla nada; el ajuste del teléfono sí.
        "ILEGIBLE" when EsFotoDeIphone =>
            "Las fotos del iPhone a veces no las podemos leer. En su teléfono entre en "
          + "Ajustes › Cámara › Formatos y elija «Más compatible», o comparta la foto como JPG "
          + "y vuelva a subirla.",

        "ILEGIBLE" =>
            "Vuelva a subirlo con mejor luz y enfocado, o adjunte el PDF original si lo tiene.",

        _ => null
    };

    /// <summary>La extensión del fichero, en minúsculas y con el punto.</summary>
    public string Extension
    {
        get
        {
            var n = Nombre ?? string.Empty;
            var i = n.LastIndexOf('.');
            return i < 0 ? string.Empty : n[i..].ToLowerInvariant();
        }
    }

    private bool EsFotoDeIphone => Extension is ".heic" or ".heif";

    /// <summary>Nivel del aviso, para pintar la tarjeta.</summary>
    public string Tono => Veredicto switch
    {
        "FACTURA"   => "ok",
        "ILEGIBLE"  => "error",
        "PENDIENTE" => "espera",
        _           => "aviso"
    };

    /// <summary>
    /// Cómo llamar a este documento delante del afiliado. El catálogo de
    /// nombres es único (NombresDocumento): estuvo duplicado y divergió.
    /// </summary>
    public string EnCristiano =>
        !Leido      ? "Todavía lo estamos leyendo"
        : EsFactura ? NombresDocumento.Factura(Tipo, TipoPrestador)
                    : NombresDocumento.Soporte(TipoSoporte);

}

/// <summary>
/// Una factura con los papeles que la respaldan. Es la unidad que de verdad
/// entiende el afiliado: «esta factura, y esto es lo que la justifica».
/// </summary>
public sealed class GrupoDocumentosVm
{
    /// <summary>La factura. Nula en el grupo de los que no cuelgan de ninguna.</summary>
    public ClienteDocumentoVm? Factura { get; set; }

    public List<ClienteDocumentoVm> Respaldos { get; set; } = new();

    public bool EsHuerfano => Factura is null;

    /// <summary>
    /// Qué se le dice de este grupo. Una factura sola está bien; un respaldo
    /// suelto no, y hay que decir POR QUÉ y QUÉ falta — no dejarlo en una
    /// tarjeta más de la pila.
    /// </summary>
    public string? Aviso => EsHuerfano
        ? (Respaldos.Count == 1
            ? "Este documento respalda un gasto, pero no encontramos la factura de ese gasto. "
            + "Sin la factura no podemos reembolsarlo."
            : "Estos documentos respaldan un gasto, pero no encontramos la factura de ese gasto. "
            + "Sin la factura no podemos reembolsarlo.")
        : null;

    /// <summary>
    /// A quién pedírsela, con nombre — y SOLO si de verdad lo sabemos.
    ///
    /// Sale del emisor, que es la entidad que factura. NUNCA del médico que
    /// firma: medido en el caso add12bcc, los dos respaldos traían EmisorNombre
    /// nulo —el clasificador solo lo rellena en las facturas— y al caer en el
    /// médico la pantalla mandaba al afiliado a pedirle la factura a
    /// «DRA. IVETT CARIDAD MANZANARES LAGUARDIA», la patóloga que firma el
    /// informe, que no emite nada. Mandar a alguien a la puerta equivocada es
    /// peor que no darle una puerta: pierde el viaje y vuelve sin la factura.
    ///
    /// Con varios emisores distintos tampoco se elige uno: se dicen todos, o
    /// no se dice ninguno. Quedarse con el primero es inventar cuál es.
    /// </summary>
    public string? AQuienPedirla
    {
        get
        {
            if (!EsHuerfano) return null;
            var emisores = Respaldos
                .Select(r => r.Emisor?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return emisores.Count == 0 ? null : string.Join(" y ", emisores!);
        }
    }

    /// <summary>
    /// Quién firma el documento. No es quien factura, así que se dice con otro
    /// verbo: su consultorio PUEDE reemitirla, no «pídasela a él».
    /// </summary>
    public string? QuienLoFirma
    {
        get
        {
            if (!EsHuerfano || AQuienPedirla != null) return null;
            var medicos = Respaldos
                .Select(r => r.Medico?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return medicos.Count == 0 ? null : string.Join(" y ", medicos!);
        }
    }
}

/// <summary>Pantalla 3: la solicitud en curso.</summary>
public sealed class ClienteSolicitudVm
{
    public Guid CaseCode { get; set; }
    public string Cedula { get; set; } = string.Empty;
    public string? NumeroContrato { get; set; }
    public string? NombrePlan { get; set; }
    public string? NombreTitular { get; set; }
    public string Estado { get; set; } = "BORRADOR";
    public decimal? ValorPresentado { get; set; }
    public bool DatosConfirmados { get; set; }
    public ContratoAfiliado? Contrato { get; set; }

    // ── A quién va dirigido el reembolso (REQ-020c) ───────────────────────
    public string? NombreBeneficiario { get; set; }
    public string? RelacionBeneficiario { get; set; }
    public string? CedulaBeneficiario { get; set; }
    public int? EdadBeneficiario { get; set; }
    public decimal? DeducibleCubierto { get; set; }
    public bool? EnCarencia { get; set; }
    public int? DiasFinCarencia { get; set; }
    public bool? TienePreexistencias { get; set; }

    /// <summary>Cuántas personas cubre el contrato: con más de una se ofrece cambiar.</summary>
    public int TotalBeneficiarios { get; set; }

    /// <summary>
    /// Lo que a ESTA persona le queda de deducible por gastar. Es el número que
    /// de verdad le afecta al reembolso de hoy: en el caso medido (contrato
    /// 549616) Martha llevaba $52 de sus $100, así que la resolución le retuvo
    /// exactamente los $48 que faltaban. Enseñar el deducible del contrato a
    /// secas no le dice nada; enseñar lo ya cubierto tampoco. Lo accionable es
    /// lo que queda.
    /// </summary>
    public decimal? DeduciblePorCubrir
    {
        get
        {
            var total = Contrato?.DeducibleTotal;
            if (total is null) return null;
            var cubierto = DeducibleCubierto ?? 0m;
            return Math.Max(0m, total.Value - cubierto);
        }
    }

    /// <summary>
    /// Lo que hay que advertirle sobre ESTA persona antes de que presente. Una
    /// carencia sin cumplir deja el gasto fuera aunque el plan lo contemple.
    /// </summary>
    public string? AvisoBeneficiario =>
        EnCarencia == true
            ? (DiasFinCarencia is > 0
                ? $"{NombreBeneficiario} todavía está en periodo de espera: le faltan {DiasFinCarencia} días para algunos beneficios."
                : $"{NombreBeneficiario} todavía está en periodo de espera para algunos beneficios.")
            : null;

    public List<ClienteDocumentoVm> Documentos { get; set; } = new();

    /// <summary>La explicación ya generada, si existe.</summary>
    public string? ExplicacionJson { get; set; }

    /// <summary>La misma explicación, ya legible. La vista solo usa esta.</summary>
    public ExplicacionClienteVm? Explicacion { get; set; }

    // ── El proceso completo (REQ-020) ─────────────────────────────────────
    // El afiliado no solo sube papeles: cierra su caso. Estos son los mismos
    // cinco pasos que corre el auditor, con su estado real.
    public List<PasoClienteVm> Pasos { get; set; } = new();

    /// <summary>
    /// La resolución del pipeline, traducida. Es la MISMA nota que lee el
    /// auditor: si el portal recalculara, algún día diría otra cifra.
    /// </summary>
    public ResolucionClienteVm? Resolucion { get; set; }

    /// <summary>
    /// Qué toca hacer AHORA. La pantalla se organiza alrededor de esto: antes
    /// apilaba ocho bloques a la vez y el afiliado tenía que deducir por su
    /// cuenta en qué punto estaba y qué se esperaba de él.
    /// </summary>
    public string Momento
    {
        get
        {
            if (Documentos.Count == 0)                      return "SUBIR";
            if (Documentos.Any(d => !d.Leido))              return "LEYENDO";
            if (!Documentos.Any(d => d.EsFactura))          return "FALTA_FACTURA";
            if (DatosConfirmados)                           return "ENVIADO";
            if (Resolucion is { HayAlgo: true })            return "RESULTADO";
            return "REVISAR";
        }
    }

    /// <summary>La frase única que encabeza la pantalla: qué se espera de él.</summary>
    public string Instruccion => Momento switch
    {
        "SUBIR"         => "Adjunte la factura de su gasto médico",
        "LEYENDO"       => "Estamos identificando sus documentos",
        "FALTA_FACTURA" => "Nos falta la factura para poder continuar",
        "REVISAR"       => "Ya podemos calcular su reembolso",
        "RESULTADO"     => "Esto es lo que le cubre su plan",
        "ENVIADO"       => "Su solicitud está en manos de Salud S.A.",
        _               => "Su solicitud"
    };

    public int PasosHechos => Pasos.Count(p => p.Hecho);
    public int PasosTotales => Pasos.Count;

    /// <summary>Los pasos que quedan por correr, en orden.</summary>
    public List<PasoClienteVm> PasosPendientes =>
        Pasos.Where(p => !p.Hecho && !string.IsNullOrEmpty(p.Url)).ToList();

    public bool ProcesoCompleto => Pasos.Count > 0 && Pasos.All(p => p.Hecho);

    public string? NombrePila =>
        string.IsNullOrWhiteSpace(NombreTitular) ? null : NombreTitular!.Split(' ').FirstOrDefault();

    /// <summary>
    /// Los documentos como ARBOL: cada factura con lo que la respalda, y al
    /// final los respaldos que no cuelgan de ninguna.
    ///
    /// Una lista plana de tarjetas no responde la pregunta que el afiliado se
    /// hace de verdad —«¿esto que subí sirve junto o me falta algo?»—. El
    /// emparejamiento ya lo hace el expediente (`vinculos`); aquí solo se
    /// enseña, que es lo que faltaba.
    ///
    /// Mientras el expediente no haya corrido no hay vínculos, y entonces cada
    /// documento aparece por su cuenta: es lo honesto, todavía no se sabe.
    /// </summary>
    public List<GrupoDocumentosVm> Agrupados
    {
        get
        {
            var grupos = new List<GrupoDocumentosVm>();
            var facturas = Documentos.Where(d => d.EsFactura).ToList();
            var colgados = new HashSet<int>();

            foreach (var f in facturas)
            {
                var g = new GrupoDocumentosVm { Factura = f };
                foreach (var r in Documentos.Where(d => !d.EsFactura && d.RespaldaADocId == f.DocId))
                {
                    g.Respaldos.Add(r);
                    colgados.Add(r.DocId);
                }
                grupos.Add(g);
            }

            // Los que no cuelgan de ninguna factura. Si hay UNA sola factura y
            // el expediente aún no corrió, no se les cuelga a la fuerza: decir
            // «respalda a esta» sin haberlo comprobado sería inventarlo.
            var sueltos = Documentos
                .Where(d => !d.EsFactura && !colgados.Contains(d.DocId))
                .ToList();

            if (sueltos.Count > 0)
                grupos.Add(new GrupoDocumentosVm { Respaldos = sueltos });

            return grupos;
        }
    }

    public int Leidos => Documentos.Count(d => d.Leido);
    public decimal TotalFacturado => Documentos.Where(d => d.EsFactura).Sum(d => d.Valor ?? 0m);

    /// <summary>
    /// El valor que tecleó el afiliado puede no coincidir con lo facturado. Es
    /// un aviso y nunca un rechazo: quien se equivoca al teclear no está
    /// haciendo trampa, y tratarlo como fraude es la forma más rápida de perder
    /// a un cliente.
    /// </summary>
    public decimal? DiferenciaConLoTecleado =>
        ValorPresentado.HasValue && TotalFacturado > 0
            ? Math.Round(TotalFacturado - ValorPresentado.Value, 2)
            : null;

    /// <summary>
    /// Dónde puede conseguir la factura que le falta.
    ///
    /// Se le dice el prestador CONCRETO cuando lo sabemos por sus otros
    /// documentos: si trajo una receta del Dr. X o una orden del laboratorio Y,
    /// es a ellos a quien tiene que pedírsela. "Pida su factura" a secas no
    /// ayuda a nadie.
    /// </summary>
    public List<string> DondeBuscarLaFactura
    {
        get
        {
            var pistas = new List<string>();

            // Los prestadores que ya conocemos por lo que subió.
            var prestadores = Documentos
                .Where(d => d.Leido && !string.IsNullOrWhiteSpace(d.Emisor))
                .Select(d => d.Emisor!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            foreach (var pr in prestadores)
                pistas.Add($"Pídala en {pr}, que es quien le atendió: casi todos la reenvían "
                         + "por correo si da su cédula.");

            // El médico que firma, cuando lo tenemos.
            var medicos = Documentos
                .Where(d => d.Leido && !string.IsNullOrWhiteSpace(d.Medico))
                .Select(d => d.Medico!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToList();

            foreach (var m in medicos)
                pistas.Add($"Si la atención fue con {m}, su consultorio puede reemitirle la factura.");

            // Y lo que sirve siempre.
            pistas.Add("Búsquela en su correo electrónico: en Ecuador la factura electrónica "
                     + "llega por email el mismo día, normalmente como PDF y XML. "
                     + "Busque por el nombre del prestador o por «comprobante electrónico».");
            pistas.Add("Si es una farmacia o una cadena grande, entre en su portal web con su "
                       + "cédula: casi todas dejan descargar las facturas de los últimos meses.");
            pistas.Add("También puede consultarla en el portal del SRI, en «Comprobantes "
                       + "electrónicos recibidos».");

            return pistas;
        }
    }

    /// <summary>
    /// Qué necesita adjuntar, marcado contra lo que ya trajo.
    ///
    /// Arranca como guía general —lo que hace falta en cualquier reembolso— y
    /// en cuanto se sabe qué tipo de gasto es (porque ya se leyó una factura)
    /// se ajusta: si son medicinas aparece la receta, si fue hospitalización
    /// aparece la epicrisis. Así el afiliado ve la lista completa desde el
    /// primer momento y no descubre el papel que falta al final.
    /// </summary>
    public List<RequisitoVm> Requisitos
    {
        get
        {
            var leidos     = Documentos.Where(d => d.Leido).ToList();
            var hayFactura = leidos.Any(d => d.EsFactura);
            var nadaLeido  = leidos.Count == 0;

            bool TieneSoporte(string tipo) => leidos.Any(d =>
                string.Equals(d.TipoSoporte, tipo, StringComparison.OrdinalIgnoreCase));

            bool FacturaDe(string marca) => leidos.Any(d =>
                d.EsFactura && (d.Tipo ?? string.Empty).ToUpperInvariant().Contains(marca));

            var lista = new List<RequisitoVm>
            {
                new()
                {
                    Documento   = "La factura del gasto",
                    PorQue      = "Es el comprobante de lo que pagó. Sin factura válida no se puede reembolsar nada.",
                    DondeConseguirlo = "Se la entrega el médico, la farmacia o el hospital al pagar.",
                    Obligatorio = true,
                    Cumplido    = hayFactura
                }
            };

            // Medicinas: la receta es lo que justifica que se las indicaron.
            var esMedicina = FacturaDe("MED");
            if (esMedicina || nadaLeido)
            {
                lista.Add(new RequisitoVm
                {
                    Documento   = "La receta médica",
                    PorQue      = "Justifica que un médico le indicó esas medicinas. Sin ella el gasto de farmacia no se puede cubrir.",
                    DondeConseguirlo = "Se la da el médico que le atendió; también sirve la receta digital.",
                    Obligatorio = esMedicina,
                    Cumplido    = TieneSoporte("RECETA_MEDICA"),
                    SoloSi      = esMedicina ? null : "si compró medicinas"
                });
            }

            // Hospitalización: la epicrisis explica por qué le atendieron.
            var esHospital = FacturaDe("HOS");
            if (esHospital || nadaLeido)
            {
                lista.Add(new RequisitoVm
                {
                    Documento   = "La epicrisis o informe de alta",
                    PorQue      = "Es el resumen que entrega el hospital al darle el alta y explica por qué le atendieron.",
                    DondeConseguirlo = "Se pide en el área de estadística o admisiones del hospital.",
                    Obligatorio = esHospital,
                    Cumplido    = TieneSoporte("EPICRISIS") || TieneSoporte("HISTORIA_CLINICA"),
                    SoloSi      = esHospital ? null : "si estuvo hospitalizado"
                });
            }

            // Exámenes y procedimientos: la orden y el resultado.
            var esExamen = FacturaDe("LAB") || FacturaDe("IMA");
            if (esExamen || nadaLeido)
            {
                lista.Add(new RequisitoVm
                {
                    Documento   = "La orden del examen y su resultado",
                    PorQue      = "La orden muestra que el examen lo pidió un médico, y el resultado que se realizó.",
                    DondeConseguirlo = "La orden la firma su médico; el resultado lo entrega el laboratorio.",
                    Obligatorio = false,
                    Cumplido    = TieneSoporte("ORDEN_PROCEDIMIENTO")
                                  || TieneSoporte("RESULTADO_LABORATORIO")
                                  || TieneSoporte("RESULTADO_IMAGEN"),
                    SoloSi      = esExamen ? null : "si se hizo exámenes"
                });
            }

            return lista;
        }
    }

    /// <summary>
    /// Los papeles que suelen faltar, deducidos de lo ya adjuntado. Es una
    /// orientación mientras el expediente no esté cerrado, no un veredicto:
    /// por eso se redacta como sugerencia.
    /// </summary>
    public List<(string Documento, string PorQue)> FaltantesProbables
    {
        get
        {
            var faltan = new List<(string, string)>();
            if (Documentos.Count == 0) return faltan;

            var hayFactura = Documentos.Any(d => d.EsFactura);
            if (!hayFactura)
                faltan.Add(("La factura",
                    "sin la factura no se puede reembolsar nada: es el comprobante del gasto."));

            var hayMedicinas = Documentos.Any(d =>
                d.EsFactura && (d.Tipo ?? string.Empty).ToUpperInvariant().Contains("MED"));
            var hayReceta = Documentos.Any(d =>
                string.Equals(d.TipoSoporte, "RECETA_MEDICA", StringComparison.OrdinalIgnoreCase));
            if (hayMedicinas && !hayReceta)
                faltan.Add(("La receta médica",
                    "para las medicinas hace falta la receta del médico que se las indicó."));

            var hayHospital = Documentos.Any(d =>
                d.EsFactura && (d.Tipo ?? string.Empty).ToUpperInvariant().Contains("HOS"));
            var hayEpicrisis = Documentos.Any(d =>
                string.Equals(d.TipoSoporte, "EPICRISIS", StringComparison.OrdinalIgnoreCase));
            if (hayHospital && !hayEpicrisis)
                faltan.Add(("La epicrisis",
                    "es el resumen que entrega el hospital al darle el alta; explica por qué le atendieron."));

            return faltan;
        }
    }
}

/// <summary>
/// Una línea de la explicación: sirve igual para lo que se cubre y para lo que
/// no. La diferencia está en el término contractual y en si hay algo que hacer.
/// </summary>
public sealed class LineaCoberturaVm
{
    public string? Concepto { get; set; }
    public decimal? Valor { get; set; }
    public decimal? Porcentaje { get; set; }
    public string? PorQue { get; set; }

    /// <summary>DEDUCIBLE | COPAGO | CARENCIA | PREEXISTENCIA | EXCLUSION | TOPE | MORA.</summary>
    public string? Termino { get; set; }

    /// <summary>Qué puede hacer el afiliado, cuando puede hacer algo.</summary>
    public string? QueHacer { get; set; }

    public int? DocId { get; set; }
    public string? TextoEvidencia { get; set; }

    /// <summary>
    /// El término, explicado en una línea. Nombrarlo sin explicarlo es lo que
    /// hace que el afiliado llame por teléfono a preguntar qué significa.
    /// </summary>
    public string? TerminoExplicado => (Termino ?? string.Empty).ToUpperInvariant() switch
    {
        "DEDUCIBLE"     => "Deducible: la parte que le toca cubrir a usted antes de que el plan empiece a pagar.",
        "COPAGO"        => "Copago: el porcentaje que queda a su cargo después del deducible.",
        "CARENCIA"      => "Carencia: el tiempo de espera desde que entró al plan hasta que ese beneficio se puede usar.",
        "PREEXISTENCIA" => "Preexistencia: una condición que ya tenía antes de entrar al plan.",
        "EXCLUSION"     => "Exclusión: algo que su plan no cubre en ningún caso.",
        "TOPE"          => "Tope: el máximo que el plan paga por ese beneficio en el año.",
        "MORA"          => "Mora: hay cuotas del plan pendientes de pago.",
        _ => null
    };
}

/// <summary>Un dato que el afiliado debe confirmar como suyo.</summary>
public sealed class ConfirmarDatoVm
{
    public string? Campo { get; set; }
    public string? Valor { get; set; }
    public int? DocId { get; set; }
    public string? TextoEvidencia { get; set; }
}

/// <summary>Un papel que falta, con para qué sirve y dónde se pide.</summary>
public sealed class FaltanteVm
{
    public string? Documento { get; set; }
    public string? PorQue { get; set; }
    public string? ComoConseguirlo { get; set; }
    public bool Bloquea { get; set; }
}

/// <summary>
/// La explicación completa que ve el afiliado. Se arma en el controlador a
/// partir del JSON del agente: la vista no ve JSON en ningún momento.
/// </summary>
public sealed class ExplicacionClienteVm
{
    public string? Saludo { get; set; }
    public string? ResumenUnaLinea { get; set; }
    public decimal? TotalPresentado { get; set; }
    public decimal? TotalEstimadoCubierto { get; set; }
    public string? SiguientePaso { get; set; }

    public List<ConfirmarDatoVm> Confirmar { get; set; } = new();
    public List<LineaCoberturaVm> Cubierto { get; set; } = new();
    public List<LineaCoberturaVm> NoCubierto { get; set; } = new();
    public List<FaltanteVm> Faltantes { get; set; } = new();
    public List<string> Avisos { get; set; } = new();

    /// <summary>
    /// Cuando el agente no pudo dar un total estimado se dice, en vez de poner
    /// un cero que el afiliado leería como "no me cubren nada".
    /// </summary>
    public bool HayEstimado => TotalEstimadoCubierto.HasValue;
}

/// <summary>
/// Un documento que hace falta para que el reembolso se pueda resolver.
///
/// Existe para que el afiliado no descubra que le faltaba un papel DESPUES de
/// esperar quince dias. El auditor sabe de memoria que unas medicinas necesitan
/// receta; el cliente no tiene por que saberlo, y si no se le dice antes, el
/// sobre vuelve por incompleto.
/// </summary>
public sealed class RequisitoVm
{
    public string Documento { get; set; } = string.Empty;

    /// <summary>Para que sirve ese papel. Sin esto la lista parece burocracia.</summary>
    public string PorQue { get; set; } = string.Empty;

    /// <summary>Donde se consigue, cuando no es obvio.</summary>
    public string? DondeConseguirlo { get; set; }

    /// <summary>Sin el no se puede resolver el reembolso.</summary>
    public bool Obligatorio { get; set; }

    /// <summary>Ya lo trajo: se marca en verde.</summary>
    public bool Cumplido { get; set; }

    /// <summary>
    /// Solo aplica a cierto tipo de gasto (medicinas, hospitalizacion...). Se
    /// muestra cuando se detecta ese tipo, o como aviso general si aun no se
    /// ha subido nada.
    /// </summary>
    public string? SoloSi { get; set; }
}

/// <summary>Pantalla: ¿para quién es este reembolso?</summary>
public sealed class ClienteBeneficiarioVm
{
    public Guid CaseCode { get; set; }
    public string? NumeroContrato { get; set; }
    public string? NombrePlan { get; set; }

    /// <summary>El que ya estaba elegido, si se vuelve a esta pantalla.</summary>
    public int? Elegido { get; set; }

    public List<BeneficiarioAfiliado> Beneficiarios { get; set; } = new();
}

/// <summary>
/// Una hoja dentro del archivo que subió el afiliado.
///
/// Existe porque un fichero no es un documento: la gente escanea de una vez la
/// factura, la receta y la orden. El clasificador ya lo tipifica hoja por hoja;
/// lo que faltaba era enseñarlo.
/// </summary>
public sealed class HojaClienteVm
{
    public int Pagina { get; set; }
    public string? Tipo { get; set; }
    public string? TipoSoporte { get; set; }
    public bool EsFactura { get; set; }
    public decimal? Valor { get; set; }

    /// <summary>
    /// Cómo se llama esta hoja delante del afiliado. Mismo catálogo que la
    /// tarjeta: el afiliado no puede leer dos nombres distintos para lo mismo.
    /// </summary>
    public string QueEs => EsFactura
        ? NombresDocumento.Factura(Tipo, null)
        : NombresDocumento.Soporte(TipoSoporte);

    public string Icono => EsFactura ? "fa-file-text" : "fa-file-o";
}

/// <summary>
/// Cómo se llama cada documento delante del afiliado.
///
/// Vive en un solo sitio porque estuvo duplicado —en la tarjeta y en la lista
/// de hojas— y las dos copias divergieron: el mismo documento se llamaba
/// "Orden del procedimiento" arriba y "Orden de procedimiento" abajo. Dos
/// tablas iguales mantenidas a mano siempre terminan así.
/// </summary>
public static class NombresDocumento
{
    /// <summary>El nombre de una factura, según de quién sea.</summary>
    public static string Factura(string? tipoArchivo, string? tipoPrestador)
    {
        if (!string.IsNullOrWhiteSpace(tipoPrestador))
            return $"Factura de {tipoPrestador}";

        var t = (tipoArchivo ?? string.Empty).ToUpperInvariant();
        if (t.Contains("MED")) return "Factura de medicinas";
        if (t.Contains("HOS")) return "Factura de hospital";
        if (t.Contains("ODO")) return "Factura odontológica";
        if (t.Contains("LAB")) return "Factura de laboratorio";
        return "Factura médica";
    }

    /// <summary>El nombre de un soporte clínico.</summary>
    public static string Soporte(string? tipoSoporte) =>
        (tipoSoporte ?? string.Empty).ToUpperInvariant() switch
        {
            "RECETA_MEDICA"               => "Receta médica",
            "EPICRISIS"                   => "Epicrisis",
            "HISTORIA_CLINICA"            => "Historia clínica",
            "RESULTADO_LABORATORIO"       => "Resultado de laboratorio",
            "RESULTADO_IMAGEN"            => "Resultado de imagen",
            "ORDEN_PROCEDIMIENTO"         => "Orden de procedimiento",
            "INFORME_PROCEDIMIENTO"       => "Informe del procedimiento",
            "KARDEX_MEDICAMENTOS"         => "Kardex de medicamentos",
            "CEDULA_IDENTIDAD"            => "Cédula de identidad",
            "SOLICITUD_COBERTURA_SALUDSA" => "Solicitud de cobertura",
            "HONORARIOS_DESGLOSADOS"      => "Detalle de honorarios",
            "RECORD_ANESTESIA"            => "Récord de anestesia",
            "PROTOCOLO_QUIRURGICO"        => "Protocolo quirúrgico",
            "INFORME_TERAPIA"             => "Informe de terapia",
            "HOJA_008"                    => "Hoja 008 de emergencia",
            "HOJA_EVOLUCION"              => "Hoja de evolución",
            "FACTURA_MEDICA"              => "Factura médica",

            // "OTRO" y el vacío significan lo mismo: no se supo qué es. Decirlo
            // así le sirve al afiliado; "otro" no le sirve de nada.
            "OTRO" or "" or null          => "No reconocimos este documento",

            var otro => char.ToUpperInvariant(otro[0])
                        + otro[1..].Replace('_', ' ').ToLowerInvariant()
        };
}

/// <summary>Un rubro de la factura, con dónde encontrarlo en el papel.</summary>
public sealed class ItemClienteVm
{
    public string? Descripcion { get; set; }
    public string? TipoRubro { get; set; }
    public decimal? Cantidad { get; set; }
    public decimal? Valor { get; set; }
    public int? Pagina { get; set; }

    /// <summary>Cómo llamar al rubro delante del afiliado.</summary>
    public string? RubroEnCristiano => (TipoRubro ?? string.Empty).ToUpperInvariant() switch
    {
        "MED"      => "medicina",
        "CON_MED"  => "consulta médica",
        "LAB_CLI"  => "laboratorio clínico",
        "LAB_IMG"  => "imagen",
        "TER"      => "terapia",
        "INS"      => "insumos",
        "HON"      => "honorarios",
        "HOS"      => "hospitalización",
        "DER"      => "derechos",
        "" or null => null,
        var otro   => otro.Replace('_', ' ').ToLowerInvariant()
    };
}

/// <summary>Un diagnóstico mencionado en el documento.</summary>
public sealed class DxClienteVm
{
    public string? Codigo { get; set; }
    public string? Descripcion { get; set; }
}

/// <summary>
/// Una comprobación hecha sobre el documento, con el texto que permite
/// localizarla dentro del papel para enseñar el recorte.
/// </summary>
public sealed class VerificacionVm
{
    public VerificacionVm(string que, string valor, string? buscar)
    {
        Que = que; Valor = valor; Buscar = buscar;
    }

    /// <summary>Qué se comprobó, en lenguaje de cliente.</summary>
    public string Que { get; }

    /// <summary>El valor encontrado.</summary>
    public string Valor { get; }

    /// <summary>Con qué texto buscarlo en el documento para el recorte.</summary>
    public string? Buscar { get; }
}
