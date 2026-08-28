using System;

namespace app_ocr_ai_models.Areas.Studio.Models;

// =============================================================================
// REQ-025 — La carencia se valida, no se menciona
//
// `enCarencia` y `diasFinCarencia` se le pasaban al modelo como un dato más
// dentro del payload, y era él quien decidía si eso cambiaba algo. Igual que
// pasaba con el porcentaje y con la factura repetida: un dato que decide si se
// paga o no se paga no puede quedar a criterio de una frase.
//
// Y la hospitalaria era peor todavía: `EnCarenciaHospitalaria` se leía del
// contrato, se guardaba en el view model... y NO se incluía en el payload que
// se le manda al agente. El modelo no podía aplicarla aunque quisiera, porque
// nunca la veía.
//
// -- Ambulatoria y hospitalaria son dos carencias distintas ------------------
// Un afiliado puede estar fuera de la carencia ambulatoria y dentro de la
// hospitalaria a la vez. Tratarlas como una sola niega consultas que sí están
// cubiertas, o paga hospitalizaciones que no lo están. Por eso aquí se evalúan
// por separado y se elige según el tipo de cobertura del gasto.
//
// -- Ante la duda, NO se niega ----------------------------------------------
// Si no se sabe el tipo de cobertura del gasto, o si el contrato no informó la
// carencia, esto NO bloquea: lo marca para que lo mire una persona. Negarle un
// reembolso a alguien por un dato que no tenemos es el peor de los dos errores.
// =============================================================================

/// <summary>Qué pasa con la carencia para un gasto concreto.</summary>
public sealed class VeredictoCarencia
{
    /// <summary>El gasto cae dentro de la carencia: no se cubre.</summary>
    public bool EnCarencia { get; init; }

    /// <summary>No se pudo decidir: falta el dato o el tipo de cobertura.</summary>
    public bool NoSeSabe { get; init; }

    /// <summary>ambulatoria | hospitalaria | null cuando no se sabe cuál aplica.</summary>
    public string? Cual { get; init; }

    /// <summary>Días que faltan para que termine, si el contrato los informó.</summary>
    public int? DiasQueFaltan { get; init; }

    /// <summary>Lo que se le dice al afiliado. Vacío si no aplica.</summary>
    public string ParaElCliente
    {
        get
        {
            if (!EnCarencia) return string.Empty;

            var tipo = Cual == "hospitalaria"
                ? "para atenciones hospitalarias"
                : "para atenciones ambulatorias";

            return DiasQueFaltan is > 0
                ? $"Su póliza todavía está en período de carencia {tipo}: faltan "
                  + $"{DiasQueFaltan} día{(DiasQueFaltan == 1 ? "" : "s")}. Durante ese tiempo "
                  + "este gasto no tiene cobertura."
                : $"Su póliza todavía está en período de carencia {tipo}, así que este gasto "
                  + "no tiene cobertura todavía.";
        }
    }
}

public static class CarenciaDelBeneficiario
{
    /// <summary>
    /// ¿Este gasto cae dentro de la carencia?
    /// </summary>
    /// <param name="enCarenciaAmbulatoria">Lo que dijo el contrato. null = no informado.</param>
    /// <param name="enCarenciaHospitalaria">Lo que dijo el contrato. null = no informado.</param>
    /// <param name="diasFinCarencia">Días que faltan, si el contrato los dio.</param>
    /// <param name="tipoCobertura">ambulatorio / hospitalario, del gasto. Vacío = no se sabe.</param>
    public static VeredictoCarencia Juzgar(bool? enCarenciaAmbulatoria,
                                           bool? enCarenciaHospitalaria,
                                           int? diasFinCarencia,
                                           string? tipoCobertura)
    {
        var tipo = (tipoCobertura ?? string.Empty).Trim().ToLowerInvariant();

        var esHospitalario = tipo.StartsWith("hosp", StringComparison.Ordinal);
        var esAmbulatorio  = tipo.StartsWith("ambu", StringComparison.Ordinal);

        // Sin saber de qué tipo es el gasto no se puede elegir carencia. Y si
        // alguna de las dos está activa, eso hay que mirarlo: se marca, no se
        // niega y no se ignora.
        if (!esHospitalario && !esAmbulatorio)
        {
            var algunaActiva = enCarenciaAmbulatoria == true || enCarenciaHospitalaria == true;
            return new VeredictoCarencia
            {
                EnCarencia = false,
                NoSeSabe = algunaActiva,
                DiasQueFaltan = diasFinCarencia
            };
        }

        var aplica = esHospitalario ? enCarenciaHospitalaria : enCarenciaAmbulatoria;

        // El contrato no informó esa carencia: no se inventa un sí ni un no.
        if (aplica is null)
            return new VeredictoCarencia
            {
                EnCarencia = false,
                NoSeSabe = true,
                Cual = esHospitalario ? "hospitalaria" : "ambulatoria",
                DiasQueFaltan = diasFinCarencia
            };

        return new VeredictoCarencia
        {
            EnCarencia = aplica.Value,
            NoSeSabe = false,
            Cual = esHospitalario ? "hospitalaria" : "ambulatoria",
            DiasQueFaltan = diasFinCarencia
        };
    }
}
