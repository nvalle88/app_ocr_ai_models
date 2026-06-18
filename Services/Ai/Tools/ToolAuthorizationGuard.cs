namespace app_tramites.Services.Ai.Tools;

// ============================================================
// REQ-019 T5 — Implementación del guardián anti-IDOR (D4).
// ============================================================

/// <summary>
/// Implementación de <see cref="IToolAuthorizationGuard"/> que cruza los identificadores
/// de afiliado presentes en el input de una tool contra el contexto de identidad del Caso.
/// </summary>
/// <remarks>
/// <b>Regla D4 (anti-IDOR):</b>
/// Los campos que identifican a un afiliado (<c>identificacion</c>, <c>cedula</c>,
/// <c>numeroDocumento</c>, <c>numeroPersona</c>, <c>numeroContrato</c>) son vectores
/// de IDOR si el modelo los puede variar libremente. Este guardián los normaliza y
/// compara contra la identidad del Caso antes de dejar salir la llamada a la API.
/// <para>
/// Casos donde la validación se omite (retorna <see langword="true"/>):
/// <list type="bullet">
///   <item><c>caseIdentity</c> es nulo o vacío (identidad del caso aún no resuelta,
///     habitual en la primera llamada a <c>resolver_contrato_por_cedula</c>).</item>
///   <item>El input no contiene ningún campo de identificador conocido.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class ToolAuthorizationGuard : IToolAuthorizationGuard
{
    // Campos del input de la tool que se consideran identificadores de afiliado
    private static readonly HashSet<string> IdentityFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "identificacion",
            "cedula",
            "numeroDocumento",
            "numeroIdentificacion",
            "numeroPersona",
            "numeroContrato",
            "numeroPersonaBeneficiario"
        };

    /// <inheritdoc />
    public bool IsAuthorized(
        string toolCode,
        IReadOnlyDictionary<string, object?> toolInput,
        string? caseIdentity)
    {
        // Si la identidad del caso no está disponible, no podemos validar → dejar pasar.
        // Esto ocurre normalmente en la primera llamada a resolver_contrato_por_cedula.
        if (string.IsNullOrWhiteSpace(caseIdentity))
            return true;

        var normalizedCaseIdentity = NormalizeId(caseIdentity);

        // Revisar todos los campos del input que sean identificadores de afiliado
        foreach (var kvp in toolInput)
        {
            if (!IdentityFields.Contains(kvp.Key))
                continue;

            var inputValue = kvp.Value?.ToString();
            if (string.IsNullOrWhiteSpace(inputValue))
                continue;

            var normalizedInput = NormalizeId(inputValue);

            // Si el campo de identificador NO coincide con la identidad del caso → IDOR
            if (!string.Equals(normalizedInput, normalizedCaseIdentity, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Normaliza un identificador: elimina guiones, espacios y ceros a la izquierda.
    /// </summary>
    private static string NormalizeId(string id) =>
        id.Replace("-", "").Replace(" ", "").TrimStart('0');
}
