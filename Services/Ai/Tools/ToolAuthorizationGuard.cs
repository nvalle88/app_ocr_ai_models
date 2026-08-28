using System;
using System.Collections.Generic;
using System.Linq;

namespace app_tramites.Services.Ai.Tools;

/// <summary>
/// Implementación de <see cref="IToolAuthorizationGuard"/> que cruza los identificadores
/// de afiliado presentes en el input de una tool contra el contexto de identidad del Caso.
/// </summary>
/// <remarks>
/// <b>Regla D4 (anti-IDOR):</b> los campos que identifican a un afiliado son vectores de
/// IDOR si el modelo los puede variar libremente. Este guardián los compara contra la
/// identidad del Caso antes de dejar salir la llamada.
///
/// <para><b>REQ-020d — cada clase se compara con la suya.</b> La versión anterior comparaba
/// TODOS esos campos contra una sola cadena, la cédula del caso. Un número de contrato no
/// puede coincidir nunca con una cédula, así que toda llamada que llevara
/// <c>numeroContrato</c> quedaba denegada sin proteger de nada: hay 3 llamadas de
/// <c>consultar_deducible_contrato</c> rechazadas por ese motivo en la auditoría.</para>
///
/// <para><b>El contrato se valida por su llave completa.</b> El número de contrato NO es
/// único: 322.560 de 1.864.098 se repiten entre combinaciones de región y producto. La
/// llave es región + producto + número, y se arma con los campos del MISMO input. Si el
/// input no trae región y producto, el contrato no se puede identificar y por tanto no se
/// juzga por él — pero el número de persona, que sí es único globalmente, sigue
/// validándose y es el que soporta la protección en esas llamadas.</para>
///
/// <para><b>Varias identidades legítimas.</b> Un contrato cubre al titular y a sus
/// dependientes, y todos son sujetos del mismo caso: consultar las preexistencias de un
/// hijo por su cédula es correcto.</para>
///
/// <para>La validación de una clase se omite cuando no se conoce ningún valor de esa
/// clase. Es el mismo estado de desconocimiento que "identidad sin resolver", aplicado a
/// una sola clase: no se puede juzgar, así que no se rechaza. Rechazar ahí es lo que
/// producía el falso negativo que este cambio corrige.</para>
/// </remarks>
public sealed class ToolAuthorizationGuard : IToolAuthorizationGuard
{
    /// <summary>Campos del input que son una cédula.</summary>
    private static readonly HashSet<string> CamposCedula =
        new(StringComparer.OrdinalIgnoreCase)
        { "cedula", "identificacion", "numeroDocumento", "numeroIdentificacion" };

    /// <summary>Campos del input que son un número de persona (único global).</summary>
    private static readonly HashSet<string> CamposPersona =
        new(StringComparer.OrdinalIgnoreCase)
        { "numeroPersona", "numeroPersonaBeneficiario" };

    /// <inheritdoc />
    public bool IsAuthorized(
        string toolCode,
        IReadOnlyDictionary<string, object?> toolInput,
        string? caseIdentity)
        => IsAuthorized(toolCode, toolInput, IdentidadCaso.DeCedula(caseIdentity));

    /// <inheritdoc />
    public bool IsAuthorized(
        string toolCode,
        IReadOnlyDictionary<string, object?> toolInput,
        IdentidadCaso? identidad)
    {
        // Sin ninguna identidad conocida no hay contra qué validar → dejar pasar.
        // Es lo que ocurre en la primera llamada, la que averigua quién es.
        if (identidad is null || identidad.Vacia)
            return true;

        string? Campo(string nombre) =>
            toolInput.TryGetValue(nombre, out var v) ? v?.ToString() : null;

        // ── Cédulas y números de persona: valor a valor ──────────────────
        foreach (var kvp in toolInput)
        {
            var esCedula  = CamposCedula.Contains(kvp.Key);
            var esPersona = CamposPersona.Contains(kvp.Key);
            if (!esCedula && !esPersona) continue;

            var valor = kvp.Value?.ToString();
            if (string.IsNullOrWhiteSpace(valor)) continue;

            var permitidos = esCedula ? identidad.Cedulas : identidad.Personas;

            // No se conoce ningún valor de esta clase: no se puede juzgar.
            if (permitidos.Count == 0) continue;

            if (!permitidos.Contains(IdentidadCaso.Normalizar(valor)))
                return false;                      // pide datos de otra persona
        }

        // ── El contrato: por su llave completa, nunca por el número suelto ──
        var numeroContrato = Campo("numeroContrato");
        if (!string.IsNullOrWhiteSpace(numeroContrato) && identidad.Contratos.Count > 0)
        {
            var llave = IdentidadCaso.Llave(
                Campo("region") ?? Campo("codigoRegion"),
                Campo("codigoProducto") ?? Campo("producto"),
                numeroContrato);

            // Sin región y producto el contrato no queda identificado: 17% de los
            // números se repiten, así que compararlo suelto no validaría nada.
            // No se juzga por él; la cédula y el número de persona ya se juzgaron.
            if (llave != null && !identidad.Contratos.Contains(llave))
                return false;                      // pide datos de otro contrato
        }

        return true;
    }
}
