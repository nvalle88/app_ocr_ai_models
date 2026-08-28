using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace app_ocr_ai_models.Areas.Studio.Models;

// =============================================================================
// REQ-024 — Una factura no se paga dos veces. Y eso no lo decide el modelo.
//
// Hasta ahora el hallazgo de un duplicado llegaba al agente como un dato más y
// era él quien decidía si eso cambiaba el resultado. Un dato así no se opina: si
// la factura ya entró, no vuelve a entrar. Esto lo resuelve en código, sobre lo
// que devolvió `factura_ya_pagada_bd`, y el resultado manda por encima de lo que
// haya propuesto el modelo.
//
// -- Lo que SÍ bloquea -------------------------------------------------------
// Cualquier reclamo NO anulado con esa misma clave de acceso. Da igual el
// contrato y da igual la persona: la factura es única. Medido sobre
// 001-100-000000916:
//
//     contrato 549616    Costa/IND   8 líneas   estado 27   <- el que presentaba
//     contrato 41215257  Sierra/COR  1 línea    estado 27   pagado 334,66
//
// -- Lo que NO bloquea -------------------------------------------------------
// Un reclamo ANULADO. Si se anuló, esa presentación dejó de existir y la factura
// puede volver a entrar; tratarlo como duplicado dejaría al afiliado sin cobrar
// algo que nadie le pagó. Por eso la tool expone `Anulada` y aquí se respeta.
//
// -- Por qué no se distingue "presentada" de "pagada" para bloquear ----------
// Porque el estado no es una escala fiable de "ya cobró". En el caso medido las
// dos apariciones estaban en estado 27 —transferido— y `YaPagado` valía 0 en las
// dos, porque esa bandera sólo mira el estado 10. Si se bloqueara sólo con
// YaPagado=1, esta factura habría pasado dos veces. Existir sin anular ya es
// motivo suficiente para no volver a meterla: lo que corresponde entonces es
// mirar el reclamo que ya existe, no crear otro.
// =============================================================================

/// <summary>Qué se encontró de una factura que ya estaba en el sistema.</summary>
public sealed class HallazgoFacturaVm
{
    public string? NumeroReclamo { get; init; }
    public string? Contrato { get; init; }

    /// <summary>true si es el mismo contrato que se está presentando: el más grave.</summary>
    public bool MismoContrato { get; init; }

    public string? Estado { get; init; }
    public string? FechaPresentacion { get; init; }
    public string? FechaPago { get; init; }
    public decimal? MontoPagado { get; init; }
    public bool Anulada { get; init; }
}

public sealed class VeredictoFacturaRepetida
{
    /// <summary>No puede volver a entrar.</summary>
    public bool Bloquea { get; init; }

    /// <summary>Los reclamos vivos donde ya aparece. Vacío si no bloquea.</summary>
    public List<HallazgoFacturaVm> Hallazgos { get; init; } = new();

    /// <summary>true si alguno es del contrato que se presenta.</summary>
    public bool HayEnElMismoContrato => Hallazgos.Any(h => h.MismoContrato);

    /// <summary>Se encontró algo, pero anulado: no bloquea y conviene decirlo.</summary>
    public bool SoloAnuladas { get; init; }

    /// <summary>Lo que se le dice al afiliado. Sin números de póliza ajenos.</summary>
    public string ParaElCliente =>
        !Bloquea
            ? string.Empty
            : HayEnElMismoContrato
                ? "Esta factura ya está registrada en su contrato, así que no puede volver a presentarse. "
                + "Si cree que hubo un error, indíquenoslo y revisamos la solicitud que ya existe."
                : "Esta factura ya está registrada en nuestro sistema, así que no puede volver a "
                + "presentarse. Si cree que hubo un error, indíquenoslo y lo revisamos.";
}

public static class FacturaRepetida
{
    /// <summary>
    /// Lee la respuesta de <c>factura_ya_pagada_bd</c> y decide. Una respuesta
    /// ilegible NO bloquea: negarle el reembolso a alguien porque no supimos
    /// interpretar un JSON sería el peor error posible de los dos.
    /// </summary>
    public static VeredictoFacturaRepetida Juzgar(string? respuestaJson, string? contratoActual)
    {
        if (string.IsNullOrWhiteSpace(respuestaJson))
            return new VeredictoFacturaRepetida { Bloquea = false };

        JsonElement raiz;
        try { raiz = JsonDocument.Parse(respuestaJson).RootElement; }
        catch { return new VeredictoFacturaRepetida { Bloquea = false }; }

        if (!raiz.TryGetProperty("rows", out var filas) || filas.ValueKind != JsonValueKind.Array)
            return new VeredictoFacturaRepetida { Bloquea = false };

        var vivos = new List<HallazgoFacturaVm>();
        var huboAnuladas = false;

        foreach (var f in filas.EnumerateArray())
        {
            var anulada = Bandera(f, "Anulada");
            var contrato = Texto(f, "ContratoNumero");

            if (anulada) { huboAnuladas = true; continue; }

            vivos.Add(new HallazgoFacturaVm
            {
                NumeroReclamo     = Texto(f, "NumeroReclamo"),
                Contrato          = contrato,
                MismoContrato     = !string.IsNullOrWhiteSpace(contratoActual)
                                    && string.Equals(contrato?.Trim(), contratoActual.Trim(),
                                                     StringComparison.OrdinalIgnoreCase),
                Estado            = Texto(f, "EstadoReclamo"),
                FechaPresentacion = Texto(f, "FechaPresentacion"),
                FechaPago         = Texto(f, "FechaPago"),
                MontoPagado       = Numero(f, "MontoPagadoDelReclamo"),
                Anulada           = false
            });
        }

        return new VeredictoFacturaRepetida
        {
            Bloquea      = vivos.Count > 0,
            Hallazgos    = vivos,
            SoloAnuladas = vivos.Count == 0 && huboAnuladas
        };
    }

    private static string? Texto(JsonElement f, string campo) =>
        f.TryGetProperty(campo, out var v) && v.ValueKind != JsonValueKind.Null
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())
            : null;

    private static decimal? Numero(JsonElement f, string campo) =>
        f.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.Number &&
        v.TryGetDecimal(out var d) ? d : null;

    /// <summary>Acepta 1/0, true/false y "True"/"False": según de dónde venga el JSON llega distinto.</summary>
    private static bool Bandera(JsonElement f, string campo)
    {
        if (!f.TryGetProperty(campo, out var v)) return false;
        return v.ValueKind switch
        {
            JsonValueKind.True   => true,
            JsonValueKind.False  => false,
            JsonValueKind.Number => v.TryGetInt32(out var n) && n != 0,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) ? b : v.GetString() == "1",
            _ => false
        };
    }
}
