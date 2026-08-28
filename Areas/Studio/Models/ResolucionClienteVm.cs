using System;
using System.Collections.Generic;
using System.Linq;

namespace app_ocr_ai_models.Areas.Studio.Models;

// =============================================================================
// REQ-020 — La resolución, contada al afiliado.
//
// Decisión de fondo: el cliente lee LA MISMA nota que el auditor
// (ResolucionReembolso), no un análisis paralelo. Si el portal recalculara por
// su cuenta qué se cubre, tarde o temprano diría una cifra distinta de la que
// ve el analista, y ese día se pierde la confianza del afiliado y la del
// auditor a la vez. Aquí solo se TRADUCE: los números son los mismos.
//
// Lo único que se añade es el idioma: "reglaAplicada: posible_copago -> CH" no
// le dice nada a una persona; "el valor que le facturaron supera la tarifa que
// tenemos negociada con ese médico, así que la diferencia queda a su cargo" sí.
// =============================================================================

/// <summary>Un paso del proceso, tal como lo ve el afiliado.</summary>
public sealed class PasoClienteVm
{
    public int Numero { get; set; }
    public string Titulo { get; set; } = string.Empty;

    /// <summary>Qué se hace en este paso, en lenguaje de cliente.</summary>
    public string Descripcion { get; set; } = string.Empty;

    public string Icono { get; set; } = "fa-circle-thin";
    public bool Hecho { get; set; }

    /// <summary>URL de la acción que lo ejecuta; null si no es automatizable.</summary>
    public string? Url { get; set; }
}

/// <summary>Una línea de la liquidación propuesta.</summary>
public sealed class ItemResolucionVm
{
    public string? Descripcion { get; set; }
    public string? Procedimiento { get; set; }
    public decimal ValorPresentado { get; set; }
    public decimal ValorCubierto { get; set; }
    public decimal ValorCopago { get; set; }
    public decimal ValorDeducible { get; set; }
    public decimal ValorNoCubierto { get; set; }

    /// <summary>TOTAL | PARCIAL | NO_CUBIERTO — tal como lo emite la resolución.</summary>
    public string Estado { get; set; } = "NO_CUBIERTO";

    public string? Motivo { get; set; }
    public string? ReglaAplicada { get; set; }

    /// <summary>
    /// Por qué esta línea quedó así, en el idioma del afiliado.
    ///
    /// El campo Motivo lo escribe el modelo para el auditor: "Retenido por CH:
    /// posible duplicidad ... y discrepancia de CIE-10 factura vs informe. No se
    /// liquida en automático". Eso, en una pantalla de autoservicio, no explica
    /// nada. Se traduce de la regla, que es estable, y el texto libre se deja
    /// para el auditor.
    /// </summary>
    public string PorQueEnCristiano
    {
        get
        {
            var r = ((ReglaAplicada ?? string.Empty) + " " + (Motivo ?? string.Empty)).ToUpperInvariant();

            if (r.Contains("YA_PAGADA") || r.Contains("DUPLICID") || r.Contains("YA PAGADA"))
                return "Esta factura ya se reembolsó en otra solicitud.";
            if (r.Contains("EXCLUS"))
                return "Su plan no cubre este tipo de gasto.";
            if (r.Contains("CARENCIA"))
                return "Todavía está en el periodo de espera de este beneficio.";
            if (r.Contains("PREEXIST"))
                return "Corresponde a una condición que ya tenía al entrar al plan.";
            if (r.Contains("TOPE") || r.Contains("MAXIMO") || r.Contains("MÁXIMO"))
                return "Supera el máximo que su plan paga por este beneficio.";
            if (r.Contains("DX_DISCREPANCIA") || r.Contains("CORRELACI"))
                return "Estamos correlacionando el diagnóstico con sus informes.";
            if (r.Contains("EXTEMPOR") || r.Contains("PLAZO"))
                return "Se presentó fuera del plazo de su contrato.";

            // Cubierto y sin regla que explicar: no hay nada que decir, y decir
            // algo genérico sería ruido.
            return string.Empty;
        }
    }
    public List<string> Evidencia { get; set; } = new();

    public bool EsCubierto => !Estado.Equals("NO_CUBIERTO", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lo que dice el PLAN para el beneficio de esta prestación, leído de
    /// Pr05Beneficios. Es la fuente buena y por eso manda.
    /// </summary>
    public decimal? PorcentajeDelPlan { get; set; }

    /// <summary>La frase del plan para esta línea, compuesta con sus datos.</summary>
    public string? ExplicacionDelPlan { get; set; }

    /// <summary>
    /// El porcentaje que se enseña.
    ///
    /// Primero el del PLAN: sale de Pr05Beneficios por plan y versión, y es
    /// verificable contra la tabla.
    ///
    /// Si no lo hay, se cae al derivado de los valores de la resolución. Ese
    /// derivado tenía un problema que ahora es visible: si el modelo no pone
    /// valorCubierto —y desde REQ-027b se le pide que lo deje en null cuando no
    /// le consta— la división da 0 y en pantalla saldría «0%», que el afiliado
    /// lee como «no me cubren nada». Por eso sólo se deriva cuando hay una cifra
    /// de verdad detrás; si no, no se enseña porcentaje.
    /// </summary>
    public decimal? Porcentaje =>
        PorcentajeDelPlan
        ?? (ValorPresentado > 0 && ValorCubierto > 0
                ? Math.Round(ValorCubierto / ValorPresentado * 100m, 0)
                : null);

    /// <summary>Cómo llamar a este estado delante del afiliado.</summary>
    public string EstadoEnCristiano => Estado.ToUpperInvariant() switch
    {
        "TOTAL"       => "Cubierto por completo",
        "PARCIAL"     => "Cubierto en parte",
        "NO_CUBIERTO" => "No entra en su plan",
        _             => Estado
    };

    /// <summary>
    /// Por qué queda algo a su cargo, dicho con el término del contrato. Se
    /// deduce de los propios valores, que es la fuente más fiable.
    /// </summary>
    public string? QuedaASuCargo
    {
        get
        {
            var partes = new List<string>();
            if (ValorDeducible > 0)
                partes.Add($"{ValorDeducible:N2} de deducible (la parte que le toca cubrir antes de que el plan empiece a pagar)");
            if (ValorCopago > 0)
                partes.Add($"{ValorCopago:N2} de copago (el porcentaje que queda a su cargo)");
            if (ValorNoCubierto > 0 && Estado != "NO_CUBIERTO")
                partes.Add($"{ValorNoCubierto:N2} que su plan no cubre");
            return partes.Count == 0 ? null : string.Join(" · ", partes);
        }
    }
}

/// <summary>Una regla del contrato que se evaluó, con su término explicado.</summary>
public sealed class ReglaClienteVm
{
    public string? Familia { get; set; }
    public string? Regla { get; set; }

    /// <summary>CUMPLE | NO_CUMPLE | NO_APLICA | REQUIERE_CH.</summary>
    public string Resultado { get; set; } = string.Empty;

    public string? Detalle { get; set; }

    /// <summary>Solo se le enseñan al afiliado las que de verdad le afectan.</summary>
    public bool LeAfecta =>
        Resultado.Equals("NO_CUMPLE", StringComparison.OrdinalIgnoreCase)
        || Resultado.Equals("REQUIERE_CH", StringComparison.OrdinalIgnoreCase);

    public string ResultadoEnCristiano => Resultado.ToUpperInvariant() switch
    {
        "CUMPLE"      => "Todo en orden",
        "NO_CUMPLE"   => "No se cumple",
        "NO_APLICA"   => "No aplica a su caso",
        "REQUIERE_CH" => "Lo revisa un analista",
        _             => Resultado
    };

    /// <summary>
    /// El término del contrato detrás de esta familia de reglas, explicado en
    /// una línea. Nombrarlo sin explicarlo es lo que hace que el afiliado
    /// termine llamando por teléfono a preguntar qué significa.
    /// </summary>
    public string? TerminoExplicado => Terminos.Explicar(Familia);

    /// <summary>
    /// Qué se está comprobando, dicho para el afiliado.
    ///
    /// Lo que escribe el modelo está pensado para el auditor y en la pantalla
    /// del cliente se leía tal cual: «motivos_CH = 4 (duplicidad, discrepancia
    /// dx, convenio no verificable/SQL, tope>250)», «Matriz de triaje (Count CH
    /// vs Count Negativa)», «Sin XML no se contrasta OCR vs SRI». Nadie que no
    /// trabaje aquí entiende una palabra, y el afiliado se queda con la
    /// sensación de que algo va mal sin saber qué.
    ///
    /// Se traduce por FAMILIA y por el nombre de la regla, que son estables;
    /// el detalle libre del modelo no se le enseña.
    /// </summary>
    public string? ParaElCliente
    {
        get
        {
            var r = (Regla ?? string.Empty).ToUpperInvariant();
            var f = (Familia ?? string.Empty).ToUpperInvariant();

            // Lo que sale CUMPLE: es lo que le quita el susto, y tiene que
            // leerse como una buena noticia, no como una etiqueta interna.
            if (r.Contains("FACTURA ELECTR"))
                return "Su factura es válida y está autorizada por el SRI.";
            if (r.Contains("VIGENCIA") || r.Contains("ESTADO DEL CONTRATO"))
                return "Su contrato está vigente y al día.";
            if (r.Contains("EXTEMPOR"))
                return "Presentó el gasto dentro del plazo.";
            if (r.Contains("CONSUMIDOR FINAL"))
                return "La factura está a su nombre.";
            if (f.Contains("DEDUCIBLE"))
                return "Su deducible del año está calculado.";

            // Una regla que YA se comprobó tiene que leerse como buena noticia.
            // Estaba pasando lo contrario: en el bloque verde «Esto ya está
            // comprobado» aparecía «Que el diagnóstico encaje…», redactado como
            // si siguiera pendiente. Se arregló para carencias y se quedó a
            // medias para el resto.
            var ok = Resultado.Equals("CUMPLE", StringComparison.OrdinalIgnoreCase);

            if (r.Contains("DUPLICAD") || r.Contains("YA PAGADA"))
                return ok ? "Esta factura no se había presentado antes."
                          : "Que esta factura no se haya presentado ya en otro reembolso.";

            if (f.Contains("COPAGO") || r.Contains("CONVENIO"))
                return ok ? "El acuerdo con este prestador está confirmado."
                          : "El acuerdo que tenemos con este prestador, que decide cuánto le toca a usted.";

            if (f.Contains("DIAGNOSTIC") || f.Contains("DIAGNÓSTIC") || r.Contains("CORRELACI"))
                return ok ? "El diagnóstico encaja con sus informes médicos."
                          : "Que el diagnóstico de la factura encaje con el de sus informes médicos.";

            if (f.Contains("TOPE") || f.Contains("MONTO"))
                return ok ? "El importe entra dentro de los máximos de su plan."
                          : "Que el importe entre dentro de los máximos de su plan.";

            if (f.Contains("EXCLUSION") || f.Contains("EXCLUSIÓN"))
                return ok ? "Lo atendido no está entre las exclusiones de su contrato."
                          : "Que lo atendido no esté entre las exclusiones de su contrato.";

            // La misma familia se dice distinto segun el resultado: en la
            // lista de lo comprobado tiene que sonar a buena noticia, no a
            // enunciado de temario.
            if (f.Contains("PREEXISTENCIA") || f.Contains("CARENCIA"))
                return Resultado.Equals("CUMPLE", StringComparison.OrdinalIgnoreCase)
                    ? "No tiene tiempos de espera pendientes."
                    : "Sus tiempos de espera y las condiciones que ya tenía al entrar al plan.";

            // La matriz de triaje es maquinaria nuestra: al afiliado no le dice
            // nada y sólo añade ruido a una pantalla en la que ya está nervioso.
            if (f.Contains("TRIAJE")) return null;

            return null;
        }
    }

    /// <summary>Se le enseña sólo si sabemos decírselo en su idioma.</summary>
    public bool SeLePuedeContar => !string.IsNullOrWhiteSpace(ParaElCliente);
}

/// <summary>
/// Diccionario de los términos del contrato. Vive en código y no en el prompt
/// porque estas definiciones no deben variar de una ejecución a otra: son las
/// mismas que aparecen en la póliza.
/// </summary>
public static class Terminos
{
    public static string? Explicar(string? familia)
    {
        var f = (familia ?? string.Empty).ToUpperInvariant();

        if (f.Contains("DEDUCIBLE"))
            return "Deducible: la parte del gasto que le toca cubrir a usted antes de que el plan "
                 + "empiece a pagar. Se acumula por año de contrato, no por cada reclamo.";

        if (f.Contains("COPAGO"))
            return "Copago: el porcentaje del gasto que queda a su cargo después del deducible. "
                 + "Si su plan cubre el 80%, el copago es el 20% restante.";

        if (f.Contains("CARENCIA") || f.Contains("PREEXISTENCIA"))
            return "Carencia y preexistencia: la carencia es el tiempo de espera desde que entró al "
                 + "plan hasta que un beneficio se puede usar; la preexistencia es una condición que "
                 + "ya tenía antes de entrar. Ambas pueden dejar un gasto fuera aunque el plan lo contemple.";

        if (f.Contains("TOPE") || f.Contains("MONTO"))
            return "Tope: el máximo que su plan paga por ese beneficio en el año. Lo que pase del "
                 + "tope queda a su cargo, aunque el gasto sea válido.";

        if (f.Contains("EXCLUSION") || f.Contains("DIAGNOSTIC"))
            return "Exclusión: algo que su plan no cubre en ningún caso, normalmente por el "
                 + "diagnóstico o el tipo de tratamiento.";

        if (f.Contains("FACTURA") || f.Contains("ELEGIBILIDAD"))
            return "Elegibilidad: antes de mirar coberturas hay que comprobar que el contrato esté "
                 + "vigente, al día de pago, y que el comprobante sea una factura válida.";

        if (f.Contains("CORRELACI"))
            return "Correlación: que lo que le facturaron tenga sentido con el diagnóstico. Si una "
                 + "medicina no corresponde a la condición registrada, hace falta revisarlo.";

        if (f.Contains("TRIAJE"))
            return "Triaje: la primera revisión automática que decide si el caso puede liquidarse "
                 + "solo o necesita que lo mire una persona.";

        return null;
    }
}

/// <summary>
/// La resolución completa, lista para pintar al afiliado. Se construye leyendo
/// la nota ResolucionReembolso; los faltantes salen de AuditoriaMedicina.
/// </summary>
public sealed class ResolucionClienteVm
{
    /// <summary>LIQUIDA_AUTO | CONTROL_HUMANO | NEGATIVA.</summary>
    public string? EstadoPropuesto { get; set; }

    public decimal? Confianza { get; set; }

    public decimal TotalPresentado { get; set; }
    public decimal TotalCubierto { get; set; }
    public decimal TotalCopago { get; set; }
    public decimal TotalDeducible { get; set; }
    public decimal TotalNoCubierto { get; set; }
    /// <summary>
    /// Lo que de verdad le llega a la cuenta: lo cubierto MENOS el deducible
    /// que aún no ha consumido y el copago. Nulo si la resolución no lo trajo
    /// —cero es un valor legítimo, no una ausencia—.
    /// </summary>
    public decimal? TotalEstimadoPagar { get; set; }

    /// <summary>
    /// La cifra grande de la pantalla.
    ///
    /// Estaba mostrando <see cref="TotalCubierto"/>, que NO es lo que se
    /// recibe. Medido en el caso d78ecb96 (contrato 549616, factura de $478,08
    /// del Dr. Muñoz): la pantalla anunciaba «Le devolvemos $382,46» cuando la
    /// propia resolución decía estimadoPagar $344,06 — a Martha le quedaban
    /// $48 de su deducible de $100 por consumir. Treinta y ocho dólares de
    /// diferencia, y encima con «Deducible $48,00» impreso justo debajo,
    /// invitando a restar una cifra que ya estaba fuera.
    ///
    /// Prometer de más es la peor manera de fallar en una pantalla de dinero:
    /// el afiliado no reclama por lo que no le cubrieron, reclama por lo que
    /// le dijimos que le íbamos a dar.
    /// </summary>
    public decimal Devolvemos =>
        Math.Max(0m, TotalCubierto - TotalDeducible - TotalCopago);

    /// <summary>
    /// Si el valor a pagar que escribió el modelo cuadra con sus propias
    /// partes. No cuadraba en el caso medido: la resolución daba cubierto
    /// 382,46 (= 286,46 + 96,00 de los dos ítems ✓), deducible 48,00
    /// (= 40,44 + 7,56 ✓) y copago 0 — pero estimadoPagar 344,06 en vez de
    /// 334,46. Aplicó el deducible al 80% (48 × 0,8 = 38,40) en lugar de
    /// entero, y se quedó 9,60 por encima.
    ///
    /// Por eso la cifra grande se calcula de las partes y no se copia: es la
    /// única que cuadra con el detalle línea a línea que el afiliado tiene
    /// justo debajo, y una pantalla que se contradice a sí misma no se puede
    /// defender delante de nadie.
    /// </summary>
    public bool CuadraElPagoDelModelo =>
        TotalEstimadoPagar is null
        || Math.Abs(TotalEstimadoPagar.Value - Devolvemos) <= 0.01m;

    /// <summary>Cuánto se desvía el modelo, para poder registrarlo.</summary>
    public decimal DesviacionDelModelo =>
        TotalEstimadoPagar is null ? 0m : TotalEstimadoPagar.Value - Devolvemos;
    public decimal TotalPendiente { get; set; }

    public List<ItemResolucionVm> Items { get; set; } = new();
    public List<ReglaClienteVm> Reglas { get; set; } = new();
    public List<string> Observaciones { get; set; } = new();

    /// <summary>Documentos que faltan, según la auditoría médica.</summary>
    public List<string> Faltantes { get; set; } = new();

    /// <summary>Los hallazgos de la auditoría que el afiliado debe conocer.</summary>
    public List<string> Alertas { get; set; } = new();

    public List<ItemResolucionVm> Cubiertos =>
        Items.Where(i => i.EsCubierto).ToList();

    public List<ItemResolucionVm> NoCubiertos =>
        Items.Where(i => !i.EsCubierto).ToList();

    public List<ReglaClienteVm> ReglasQueLeAfectan =>
        Reglas.Where(r => r.LeAfecta).ToList();

    // ── Revisión NO es negativa ──────────────────────────────────────────
    //
    // El defecto que dispara todo esto (caso 598ec576): el afiliado sube los
    // tres papeles correctos y la pantalla le anuncia «Estimamos devolverle
    // $ 0,00» con un motivo que habla de la factura. Pero la resolución dice
    // justo lo contrario: la regla «Factura electrónica válida (SRI, clave 49
    // díg.)» sale CUMPLE, el contrato está vigente y no hay carencia. Lo único
    // que pasa es que el caso quedó REQUIERE_CH —lo mira una persona— y en ese
    // estado el modelo deja todos los valores en cero.
    //
    // Enseñar ese cero como si fuera el resultado es decirle que no le
    // cubrimos nada, que es falso. Y culpar a la factura, que pasó su
    // validación, es falso dos veces.

    /// <summary>El caso lo tiene que mirar una persona antes de pagar.</summary>
    public bool EnRevision =>
        (EstadoPropuesto ?? string.Empty).Equals("CONTROL_HUMANO", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Está en revisión y todavía no hay importe: no hay cifra que dar, y un
    /// cero enorme en pantalla se lee como una negativa.
    /// </summary>
    public bool SinCifraTodavia => EnRevision && TotalPendiente > 0 && Devolvemos <= 0;

    /// <summary>Lo que YA quedó comprobado. Es lo que le quita el susto.</summary>
    public List<ReglaClienteVm> LoQueEstaEnOrden =>
        Reglas.Where(r => r.Resultado.Equals("CUMPLE", StringComparison.OrdinalIgnoreCase)
                          && r.SeLePuedeContar)
              .ToList();

    /// <summary>Lo que falta comprobar. NO es un rechazo y no puede pintarse como tal.</summary>
    public List<ReglaClienteVm> LoQueFaltaRevisar =>
        Reglas.Where(r => r.Resultado.Equals("REQUIERE_CH", StringComparison.OrdinalIgnoreCase)
                          && r.SeLePuedeContar)
              .ToList();

    /// <summary>Lo que de verdad no se cumple. Esto sí deja el gasto fuera.</summary>
    public List<ReglaClienteVm> LoQueNoSeCumple =>
        Reglas.Where(r => r.Resultado.Equals("NO_CUMPLE", StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// POR QUÉ no se cubre, de verdad.
    ///
    /// La pantalla ponía siempre «fuera de su plan» debajo del importe no
    /// cubierto. Medido en el caso 9da6b9f4: los dos ítems salieron
    /// NO_CUBIERTO con la regla FACTURA_YA_PAGADA / DUPLICIDAD —la factura ya
    /// se había reembolsado en otros reclamos— y al afiliado se le decía que su
    /// colonoscopia está «fuera de su plan». Son dos cosas distintas y la
    /// diferencia le importa: una es una exclusión de su póliza, la otra es que
    /// ya cobró ese gasto.
    ///
    /// Se lee de la regla que aplicaron los propios ítems, no se supone.
    /// </summary>
    public string NotaNoCubierto
    {
        get
        {
            var reglas = string.Join(" ", NoCubiertos
                .Select(i => (i.ReglaAplicada ?? string.Empty) + " " + (i.Motivo ?? string.Empty)))
                .ToUpperInvariant();

            if (reglas.Contains("YA_PAGADA") || reglas.Contains("DUPLICID") || reglas.Contains("YA PAGADA"))
                return "esta factura ya se reembolsó antes";
            if (reglas.Contains("EXCLUS"))
                return "su plan no cubre este tipo de gasto";
            if (reglas.Contains("CARENCIA"))
                return "todavía está en periodo de espera";
            if (reglas.Contains("PREEXIST"))
                return "corresponde a una condición previa a su plan";
            if (reglas.Contains("TOPE") || reglas.Contains("MAXIMO") || reglas.Contains("MÁXIMO"))
                return "supera el máximo que cubre su plan";
            if (reglas.Contains("EXTEMPOR") || reglas.Contains("PLAZO"))
                return "se presentó fuera de plazo";
            if (reglas.Contains("FACTURA") && reglas.Contains("INVALID"))
                return "la factura no cumple los requisitos";

            // Sin regla reconocible NO se inventa un motivo: se dice lo único
            // que se sabe con certeza.
            return "no entra en este reembolso";
        }
    }

    /// <summary>
    /// El titular de la pantalla. Es la cifra que la persona vino a buscar, y
    /// se dice con el matiz correcto: cuando el caso queda en revisión, lo
    /// cubierto es una estimación y no un valor a pagar todavía.
    /// </summary>
    public string TitularCifra
    {
        get
        {
            // En revisión y sin importe todavía no se anuncia una cifra: no la
            // hay. «Estimamos devolverle $ 0,00» es la peor frase posible —
            // dice que no le damos nada cuando lo que pasa es que aún no se ha
            // calculado.
            if (SinCifraTodavia) return "Su caso está en revisión";

            return (EstadoPropuesto ?? string.Empty).ToUpperInvariant() switch
            {
                "LIQUIDA_AUTO"   => "Le devolvemos",
                "NEGATIVA"       => "No procede reembolso",
                "CONTROL_HUMANO" => "Estimamos devolverle",
                _                => "Estimamos devolverle"
            };
        }
    }

    /// <summary>
    /// Qué pasa ahora. No se adorna: si el caso lo tiene que ver una persona,
    /// se dice, porque el afiliado va a esperar y merece saber por qué.
    /// </summary>
    public string SiguientePaso => (EstadoPropuesto ?? string.Empty).ToUpperInvariant() switch
    {
        "LIQUIDA_AUTO" =>
            "Su solicitud cumple todas las reglas y puede liquidarse directamente. "
          + "Salud S.A. confirma el valor final y le acredita el reembolso.",
        "NEGATIVA" =>
            "Con lo presentado, esta solicitud no procede. Revise abajo el motivo: en varios casos "
          + "se resuelve adjuntando el documento que falta.",
        "CONTROL_HUMANO" =>
            "Su caso pasa a revisión de un analista de Salud S.A. No es un problema: significa que "
          + "hay algo que una persona debe confirmar antes de pagar. El valor de arriba es una "
          + "estimación y puede cambiar.",
        _ =>
            "Salud S.A. revisará su solicitud y le confirmará el valor final."
    };

    /// <summary>
    /// Nunca se le presenta esto como una liquidación cerrada: lo es una
    /// propuesta generada automáticamente y el afiliado tiene derecho a saberlo.
    /// </summary>
    public string Advertencia =>
        "Este cálculo es una estimación automática para que sepa a qué atenerse. "
      + "La liquidación definitiva la emite Salud S.A.";

    public bool HayAlgo => Items.Count > 0 || Reglas.Count > 0;
}
