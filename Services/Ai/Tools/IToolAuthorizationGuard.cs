namespace app_tramites.Services.Ai.Tools;

// ============================================================
// REQ-019 T5 — Guardián anti-IDOR (D4).
// ============================================================

/// <summary>
/// Guardián runtime anti-IDOR (D4): impide que un agente consulte datos
/// de un contrato/persona distinto al contexto de identidad del Caso.
/// </summary>
/// <remarks>
/// <b>Regla de autorización (D4):</b>
/// Toda tool que incluya un identificador de afiliado en su input
/// (<c>numeroContrato</c>, <c>numeroPersona</c>, <c>identificacion</c>, etc.)
/// debe pertenecer al mismo titular que el contexto del Caso en ejecución.
/// <para>
/// El contexto de identidad del Caso se pasa como <c>caseIdentity</c> (cédula/id del titular,
/// extraída del <c>ProcessCase</c> o del primer DataFile OCR del caso).
/// Si <c>caseIdentity</c> es nulo/vacío, la validación se omite
/// (el caso aún no tiene identidad registrada — la primera tool la resuelve).
/// </para>
/// <para>
/// Si la validación falla, el guard devuelve <see langword="false"/>; el executor
/// registra un <see cref="app_tramites.Models.ModelAi.ToolInvocation"/> con
/// <c>IsError=true</c> y lanza <see cref="UnauthorizedAccessException"/>.
/// </para>
/// </remarks>
public interface IToolAuthorizationGuard
{
    /// <summary>
    /// Valida que los identificadores de afiliado presentes en el input de la tool
    /// correspondan al contexto de identidad del Caso.
    /// </summary>
    /// <param name="toolCode">Código de la tool que se va a invocar.</param>
    /// <param name="toolInput">
    /// Diccionario de parámetros del input de la tool, tal como los envió el modelo.
    /// </param>
    /// <param name="caseIdentity">
    /// Cédula o número de documento del titular del Caso (puede ser <see langword="null"/>
    /// si aún no fue resuelta — en ese caso la validación se omite).
    /// </param>
    /// <returns>
    /// <see langword="true"/> si el acceso está autorizado o no hay identidad que validar;
    /// <see langword="false"/> si se detectó un intento de acceso a datos de otro afiliado.
    /// </returns>
    bool IsAuthorized(
        string toolCode,
        IReadOnlyDictionary<string, object?> toolInput,
        string? caseIdentity);
}
