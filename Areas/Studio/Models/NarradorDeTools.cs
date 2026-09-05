using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace app_ocr_ai_models.Areas.Studio.Models;

// =============================================================================
// REQ-023 — Que el afiliado VEA trabajar al agente
//
// Hasta ahora el portal enseñaba cuatro pasos gruesos —identificar documentos,
// ordenar el expediente, revisar lo médico, aplicar el plan— y dentro de cada
// uno, cuarenta segundos de nada. El agente estaba consultando el SRI, los
// reclamos de producción, el convenio del prestador y las coberturas del plan,
// y el afiliado no veía ni uno solo de esos gestos. Le llegaba un veredicto
// caído del cielo.
//
// Esto traduce cada llamada a herramienta en una línea que el afiliado entiende,
// con su resultado:
//
//     Comprobando su factura en el SRI ................ Está registrada
//     Comprobando que no se haya pagado antes ......... No consta pagada
//     Comprobando al prestador ........................ Tiene convenio
//     Consultando qué cubre su plan ................... Cubre el 80%
//
// -- Tres reglas que NO se saltan --------------------------------------------
//
//   1. NUNCA se inventa un veredicto. Si la respuesta no se sabe leer con
//      seguridad, la línea se queda sin resultado. Una frase de más en una
//      pantalla que habla de su dinero se convierte en una reclamación.
//
//   2. NUNCA sale un dato de un tercero. factura_ya_pagada_bd devuelve el
//      contrato y la persona del reclamo donde se pagó, y ese reclamo puede ser
//      de OTRO afiliado. Al cliente se le dice que ya consta presentada; jamás
//      de quién.
//
//   3. NUNCA se enseña el nombre técnico de la herramienta. `factura_ya_pagada_bd`
//      no le dice nada a nadie, y `Lr04DetalleReclamo` menos.
// =============================================================================

/// <summary>Una línea de lo que el agente está haciendo, en lenguaje del afiliado.</summary>
public sealed class PasoDelAgenteVm
{
    /// <summary>Para no repetir líneas ya pintadas: el id de la invocación.</summary>
    public long Id { get; init; }

    /// <summary>«Comprobando su factura en el SRI».</summary>
    public string Que { get; init; } = string.Empty;

    /// <summary>«Está registrada». Vacío cuando no se sabe leer: se calla, no se inventa.</summary>
    public string? Resultado { get; init; }

    /// <summary>ok | atencion | fallo | neutro — sólo para el color del icono.</summary>
    public string Tono { get; init; } = "neutro";

    /// <summary>Segundos que tardó, si ya terminó.</summary>
    public double? Segundos { get; init; }
}

public static class NarradorDeTools
{
    /// <summary>
    /// Qué se está haciendo, por herramienta. Lo que NO está aquí se narra en
    /// genérico: es preferible una línea sosa a una inventada.
    /// </summary>
    private static readonly Dictionary<string, string> Titulos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["obtener_factura_repositorio"]        = "Comprobando su factura en el SRI",
        ["cargar_factura_desde_sri"]           = "Trayendo su factura desde el SRI",
        ["factura_ya_pagada_bd"]               = "Comprobando que esa factura no se haya pagado antes",
        ["buscar_factura_repetida_bd"]         = "Comprobando que no nos la haya presentado ya",
        ["resolver_contrato_por_cedula"]       = "Localizando su contrato",
        ["resolver_convenio_por_ruc"]          = "Comprobando al prestador que le atendió",
        ["codigo_liquidacion_y_cobertura"]     = "Consultando qué cubre su plan para eso",
        ["cobertura_beneficio_plan"]           = "Consultando qué cubre su plan para eso",
        ["consultar_deducible_contrato"]       = "Consultando su deducible",
        ["consultar_deducibles_coberturas_plan"] = "Consultando los deducibles de su plan",
        ["consultar_coberturas_plan"]          = "Revisando las coberturas de su plan",
        ["consultar_coberturas_convenio"]      = "Revisando lo acordado con ese prestador",
        ["consultar_beneficio_convenio"]       = "Revisando lo acordado con ese prestador",
        ["consultar_sobre_bd"]                 = "Consultando su solicitud",
        ["consultar_detalle_sobre_bd"]         = "Consultando el detalle de su solicitud",
        ["consultar_liquidacion_sobre_bd"]     = "Consultando cómo se liquidó",
        ["historial_reembolsos_cliente_bd"]    = "Revisando sus reembolsos anteriores",
        ["buscar_sobres_cliente_bd"]           = "Buscando sus solicitudes anteriores",
        ["consultar_preexistencias_por_cedula"]= "Revisando las condiciones de su póliza",
        ["consultar_diagnosticos_preexistentes"] = "Revisando las condiciones de su póliza",
        ["buscar_medicina_catalogo_cf"]        = "Buscando el medicamento en el catálogo",
        ["buscar_medicina_prestador_vademecum"] = "Buscando el medicamento en el vademécum",
        ["validar_medicina_vademecum"]         = "Comprobando el medicamento",
        ["obtener_documentos_sobre_armonix"]   = "Recogiendo los documentos de su solicitud",

        // Las que mas usa el chat del afiliado, y que estaban SIN frase: las
        // tres lineas que veia Nestor decian "Consultando sus datos" porque
        // caian aqui al generico. Una lista de tres lineas identicas no informa
        // de nada, y encima tapa que cada una hizo algo distinto.
        ["condiciones_del_plan"]               = "Leyendo las condiciones de su contrato",
        ["buscar_prestador_convenio"]          = "Buscando prestadores con convenio",
        ["tarifario_prestador"]                = "Consultando el precio negociado",
        ["consultar_autorizaciones"]           = "Buscando sus autorizaciones",
        ["buscar_sucursales_cerca"]            = "Buscando sucursales por esa zona",
        // REQ-040: sin estas, las siete tools nuevas decian "Consultando sus
        // datos" y el afiliado no veia QUE se estaba mirando, que es justo
        // lo que hace util el globo de espera.
        ["consultar_mis_reembolsos"]           = "Buscando sus reembolsos",
        ["consultar_detalle_sobre"]            = "Viendo en que va ese sobre",
        ["consultar_liquidacion_sobre"]        = "Revisando su liquidacion",
        ["consultar_documentos_sobre"]         = "Buscando los documentos que subio",
        ["consultar_ticket_sobre"]             = "Buscando el ticket de su tramite",
        ["consultar_atencion_ticket"]          = "Leyendo la atencion de su ticket",
        ["consultar_coberturas_plan_prestador"] = "Viendo que le cubren en ese prestador",
        // REQ-042: el precio de la consulta se pregunta por dos lados a la vez
        // -lo negociado y la regla general del contrato- y sin estas dos lineas
        // el afiliado veia el mismo "Consultando sus datos" repetido.
        ["precio_consulta_medico"]             = "Calculando cuanto le costaria la consulta",
        ["copago_del_prestador"]               = "Viendo si ese prestador tiene copago negociado",
    };

    /// <summary>
    /// Traduce una invocación en una línea para el afiliado. <paramref name="respuesta"/>
    /// es el ResponseJson tal cual quedó guardado.
    /// </summary>
    public static PasoDelAgenteVm Narrar(long id, string? toolCode, string? respuesta,
                                         bool esError, DateTime? inicio, DateTime? fin)
    {
        var code = (toolCode ?? string.Empty).Trim();
        var titulo = Titulos.TryGetValue(code, out var t) ? t : Generico(code);

        double? segundos = (inicio.HasValue && fin.HasValue)
            ? Math.Round((fin.Value - inicio.Value).TotalSeconds, 1)
            : null;

        // Aún corriendo: hay fila pero no hay fin.
        if (!fin.HasValue)
            return new PasoDelAgenteVm { Id = id, Que = titulo, Tono = "neutro" };

        if (esError)
            return new PasoDelAgenteVm
            {
                Id = id, Que = titulo, Segundos = segundos, Tono = "fallo",
                // Sin detalle tecnico: al afiliado no le sirve un stack ni un 500.
                Resultado = "No se pudo consultar ahora"
            };

        var (texto, tono) = Veredicto(code, respuesta);
        return new PasoDelAgenteVm
        {
            Id = id, Que = titulo, Resultado = texto,
            Tono = tono, Segundos = segundos
        };
    }

    /// <summary>
    /// El resultado en una frase. Devuelve (null, "ok") cuando la respuesta no se
    /// sabe leer: la línea sale sin veredicto y no pasa nada. Inventarlo sí pasaría.
    /// </summary>
    private static (string? Texto, string Tono) Veredicto(string code, string? respuesta)
    {
        if (string.IsNullOrWhiteSpace(respuesta)) return (null, "ok");

        JsonElement raiz;
        try { raiz = JsonDocument.Parse(respuesta).RootElement; }
        catch { return (null, "ok"); }

        switch (code.ToLowerInvariant())
        {
            case "obtener_factura_repositorio":
                return EstadoOk(raiz)
                    ? ("Está registrada y coincide", "ok")
                    : ("No consta todavía; la buscamos en el SRI", "atencion");

            case "cargar_factura_desde_sri":
                // OJO: este endpoint contesta HTTP 200 con Estado "Error" cuando el
                // SRI no devuelve el documento. Mirar solo el codigo HTTP diria que
                // fue bien.
                return EstadoOk(raiz)
                    ? ("La trajimos del SRI", "ok")
                    : ("El SRI no la reconoce", "atencion");

            case "factura_ya_pagada_bd":
            case "buscar_factura_repetida_bd":
                // Regla 2: se dice QUE consta, nunca a nombre de quién ni en qué
                // contrato. Ese reclamo puede ser de otro afiliado, y decirle a
                // alguien el número de póliza de un tercero es un incidente.
                //
                // La búsqueda NO se limita al contrato que se presenta: la
                // factura es única, y si ya se pagó en cualquier sitio no se
                // vuelve a pagar.
                return Filas(raiz) == 0
                    ? ("No consta pagada antes", "ok")
                    : ("Esa factura ya consta presentada", "atencion");

            case "resolver_convenio_por_ruc":
                return Filas(raiz) > 0 || EstadoOk(raiz)
                    ? ("Trabaja con Salud S.A.", "ok")
                    : ("No trabaja con Salud S.A.", "atencion");

            case "resolver_contrato_por_cedula":
                return Filas(raiz) > 0 || EstadoOk(raiz)
                    ? ("Contrato localizado", "ok")
                    : ("No encontramos el contrato", "atencion");

            case "codigo_liquidacion_y_cobertura":
            case "cobertura_beneficio_plan":
                return Cobertura(raiz);

            // Decir cuantos salieron, que es lo unico verdadero y ademas util:
            // "Sin resultados" a secas no distingue entre no haber encontrado
            // nada y no haber buscado bien.
            case "consultar_mis_reembolsos":
            case "consultar_ticket_sobre":
            {
                var filas = Filas(raiz);
                if (filas < 0) return (null, "ok");
                return filas == 0
                    ? ("No encontre ninguno", "atencion")
                    : ($"{filas} encontrado(s)", "ok");
            }

            case "consultar_liquidacion_sobre":
                // Vacio NO es un fallo: significa que aun no se liquida, y asi
                // hay que decirlo. Marcarlo como error haria que el modelo se
                // disculpara por algo que es simplemente el estado del tramite.
                return (EstadoOk(raiz) ? "Consultado" : "Todavia sin liquidar", "ok");

            case "consultar_detalle_sobre":
            case "consultar_documentos_sobre":
            case "consultar_atencion_ticket":
            case "consultar_coberturas_plan_prestador":
                return (EstadoOk(raiz) ? "Consultado" : null, "ok");

            case "buscar_sucursales_cerca":
            {
                var filas = Filas(raiz);
                if (filas < 0) return (null, "ok");
                if (filas == 0) return ("Ninguna por esa zona", "atencion");
                // ComoSeBusco dice si de verdad ubico el sitio. Cuando no, la
                // lista es de toda la ciudad y anunciar "5 cerca" seria mentir.
                //
                // Se busca "NO se ubico" en cualquier posicion y no el principio
                // exacto de la frase: la version anterior comparaba con
                // StartsWith("NO se pudo ubicar") y al reescribir el texto en
                // REQ-042a habria dejado de casar, sin fallar y sin avisar.
                // Y hay un tercer caso nuevo: la zona se ubico pero es ancha
                // -una avenida, un nombre repetido-, y ahi la distancia existe
                // pero es aproximada, asi que tampoco se anuncia como "cerca".
                var como = Texto(raiz, "ComoSeBusco") ?? string.Empty;
                if (como.Contains("NO se ubico", StringComparison.OrdinalIgnoreCase))
                    return ($"{filas}, pero de toda la ciudad", "atencion");
                if (como.Contains("no es un punto sino una zona", StringComparison.OrdinalIgnoreCase))
                    return ($"{filas} por esa zona", "ok");
                return ($"{filas} cerca", "ok");
            }

            // El precio de la consulta. Lo que el afiliado espera leer es UNA
            // cifra -la suya-, asi que el veredicto la adelanta en vez de decir
            // cuantas filas vinieron.
            case "precio_consulta_medico":
            {
                var filas = Filas(raiz);
                if (filas < 0) return (null, "ok");
                if (filas == 0) return ("No se pudo calcular", "atencion");
                var paga = Texto(raiz, "UstedPaga");
                return string.IsNullOrWhiteSpace(paga)
                    ? ("Calculado", "ok")
                    : ($"Usted pondria US$ {paga}", "ok");
            }

            // Vacia NO es un fallo: quiere decir que ese prestador no tiene nada
            // negociado y que manda la regla general. Pintarlo en ambar seria
            // alarmar por lo normal -de 2.270 convenios solo 860 tienen copago-.
            case "copago_del_prestador":
            {
                var filas = Filas(raiz);
                if (filas < 0) return (null, "ok");
                return filas == 0
                    ? ("Sin copago negociado", "ok")
                    : ($"{filas} valor(es) negociados", "ok");
            }

            case "buscar_prestador_convenio":
            {
                var filas = Filas(raiz);
                if (filas < 0) return (null, "ok");
                return filas == 0
                    ? ("Ninguno con esos datos", "atencion")
                    : ($"{filas} con convenio", "ok");
            }

            case "tarifario_prestador":
            {
                var filas = Filas(raiz);
                if (filas < 0) return (null, "ok");
                // Solo 213 convenios tienen tarifario: que no haya precio es lo
                // normal, no una averia. Se dice sin alarmar.
                return filas == 0
                    ? ("No consta el precio", "atencion")
                    : ($"{filas} precios", "ok");
            }

            case "consultar_autorizaciones":
            {
                var filas = Filas(raiz);
                if (filas < 0) return (null, "ok");
                return filas == 0
                    ? ("No consta ninguna", "atencion")
                    : ($"{filas} encontradas", "ok");
            }

            case "condiciones_del_plan":
            {
                var filas = Filas(raiz);
                if (filas < 0) return (null, "ok");
                return filas == 0
                    ? ("No consta esa condicion", "atencion")
                    : ("Encontrada en su contrato", "ok");
            }

            default:
                // Sin regla propia: se dice si trajo algo, que es verdad y no
                // compromete a nada.
                var n = Filas(raiz);
                if (n < 0) return (null, "ok");
                return n == 0 ? ("Sin resultados", "neutro") : ("Consultado", "ok");
        }
    }

    /// <summary>
    /// La cobertura merece frase propia: es la cifra que le importa. Se lee
    /// PorcentajeQueAplica de la primera fila, y se respeta la Alerta: si la
    /// herramienta avisó de ambigüedad, aquí NO se canta un porcentaje.
    /// </summary>
    private static (string?, string) Cobertura(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("rows", out var filas) ||
            filas.ValueKind != JsonValueKind.Array || filas.GetArrayLength() == 0)
            return ("Su plan no lista esa prestación", "atencion");

        // Antes de nada: si la consulta se hizo con la región o la cobertura
        // equivocadas, el beneficio SI existe. Decirle al afiliado que su plan no
        // lo cubre seria negarle algo que tiene, por un parametro nuestro.
        if (filas[0].TryGetProperty("Alerta", out var aviso) &&
            aviso.ValueKind == JsonValueKind.String &&
            (aviso.GetString() ?? string.Empty).StartsWith("FILTRO EQUIVOCADO", StringComparison.Ordinal))
            return ("Lo está revisando un especialista", "atencion");

        var f = filas[0];

        // Genérico: el procedimiento no se identificó y se catalogó como
        // misceláneo, igual que hace la liquidación real. Al afiliado NO se le
        // dice «no se identificó» —no es cosa suya y no puede hacer nada— pero
        // tampoco se le canta un porcentaje como si fuera el de su
        // procedimiento, porque son los topes del genérico. Se le dice que lo
        // está revisando una persona, que es lo que va a pasar.
        if (f.TryGetProperty("EsGenerico", out var gen) &&
            (gen.ValueKind == JsonValueKind.True ||
             (gen.ValueKind == JsonValueKind.String && gen.GetString() == "True")))
            return ("Lo está revisando un especialista", "atencion");

        if (f.TryGetProperty("Alerta", out var al) && al.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(al.GetString()))
            return ("Necesita una comprobación más", "atencion");

        if (f.TryGetProperty("PorcentajeQueAplica", out var p) &&
            p.ValueKind is JsonValueKind.Number &&
            p.TryGetDecimal(out var pct) && pct > 0 && pct <= 100)
            return ($"Su plan cubre el {pct.ToString("0.##", CultureInfo.GetCultureInfo("es-EC"))}%", "ok");

        return (null, "ok");
    }

    private static bool EstadoOk(JsonElement raiz) =>
        raiz.TryGetProperty("Estado", out var e) &&
        e.ValueKind == JsonValueKind.String &&
        string.Equals(e.GetString(), "OK", StringComparison.OrdinalIgnoreCase);

    /// <summary>rowCount de las herramientas SQL. -1 = no aplica.</summary>
    private static int Filas(JsonElement raiz) =>
        raiz.TryGetProperty("rowCount", out var n) && n.ValueKind == JsonValueKind.Number
            ? n.GetInt32()
            : -1;

    /// <summary>
    /// Un campo de texto de la PRIMERA fila de una herramienta SQL. Sirve para
    /// las columnas que la propia consulta usa para explicarse —ComoSeBusco,
    /// QueSignifica—, que dicen algo que el conteo de filas no dice.
    /// </summary>
    private static string? Texto(JsonElement raiz, string campo)
    {
        if (!raiz.TryGetProperty("rows", out var filas) ||
            filas.ValueKind != JsonValueKind.Array ||
            filas.GetArrayLength() == 0)
            return null;

        var primera = filas[0];
        return primera.ValueKind == JsonValueKind.Object &&
               primera.TryGetProperty(campo, out var v) &&
               v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
    }

    /// <summary>
    /// Para una herramienta que aún no tiene frase: NO se enseña su código. Se
    /// dice lo único cierto y neutro que se puede decir.
    /// </summary>
    private static string Generico(string _) => "Consultando sus datos";
}
