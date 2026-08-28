using app_tramites.Models.ModelAi;
using System.Text;
using System.Text.Json;

namespace app_tramites.Services.Ai;

// REQ-019: DRY — extrae la lógica compartida de resolución de system prompt y construcción
// del contexto OCR que estaba duplicada entre ProcessOrchestrator y ChatController.
//
// REQ-019 (inteligencia del agente):
//  - BuildCaseContext / ExtractCedulaFromCaseContext: leen la Note "ContextoSobre"
//    (JSON persistido al importar) para que el agente conozca sobre/contrato/producto/
//    región/persona SIN tener que adivinarlos del OCR.
//  - ResolveSystemPrompt ahora APILA los prompts vinculados (OPAIModelPrompt en orden)
//    después del SystemPrompt — el equivalente a "skills" en Markdown componibles.

/// <summary>
/// Métodos de utilidad estáticos compartidos entre <see cref="ProcessOrchestrator"/>
/// y <see cref="app_ocr_ai_models.Areas.Studio.Controllers.ChatController"/>
/// para construir el contexto OCR/caso y resolver el system prompt del agente.
/// </summary>
public static class OcrPromptHelper
{
    /// <summary>Título de la Note que guarda el contexto estructurado del sobre importado.</summary>
    public const string ContextoSobreNoteTitle = "ContextoSobre";

    /// <summary>Título de la Note que guarda la resolución de reembolso (pre-liquidación IA).</summary>
    public const string ResolucionReembolsoNoteTitle = "ResolucionReembolso";

    /// <summary>Título de la Note que guarda el dictamen del agente de auditoría médica.</summary>
    public const string AuditoriaMedicinaNoteTitle = "AuditoriaMedicina";

    /// <summary>Título de la Note que guarda el expediente documental (clasificación + entidades + vínculos IA).</summary>
    public const string ExpedienteNoteTitle = "ExpedienteDocumental";

    /// <summary>
    /// Construye el contexto OCR concatenado de todos los documentos del caso.
    /// Solo incluye archivos que tienen texto OCR extraído.
    /// </summary>
    /// <param name="files">Colección de <see cref="DataFile"/> del caso.</param>
    /// <returns>
    /// Texto multibloque donde cada documento está delimitado por una cabecera
    /// <c>--- Documento: {nombre} ---</c>.
    /// Devuelve cadena vacía si ningún archivo tiene texto.
    /// </returns>
    public static string BuildOcrContext(IEnumerable<DataFile> files)
    {
        var sb = new StringBuilder();
        foreach (var f in files)
        {
            if (!string.IsNullOrWhiteSpace(f.Text))
                sb.AppendLine($"--- Documento: {f.OriginalName} ---").AppendLine(f.Text);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Devuelve el contexto estructurado del sobre (JSON de la Note "ContextoSobre"
    /// persistida al importar): número de sobre, contrato, producto, región, persona,
    /// titular, cédula (si se conoce). Cadena vacía si el caso no tiene contexto.
    /// </summary>
    /// <param name="notes">Notas del caso.</param>
    public static string BuildCaseContext(IEnumerable<Note>? notes)
    {
        var lista = notes?.ToList() ?? new List<Note>();

        var sb = new StringBuilder();

        var ctx = lista.FirstOrDefault(n =>
            string.Equals(n.Title, ContextoSobreNoteTitle, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(ctx?.Detail))
        {
            sb.AppendLine(ctx!.Detail);
        }

        // El proceso ALIMENTA HACIA ADELANTE: lo que ya dictaminaron los pasos
        // anteriores entra como contexto del siguiente. Sin esto el orden de los
        // pasos seria decorativo: la resolucion volveria a razonar desde cero e
        // ignoraria la auditoria clinica, que es justo su insumo.
        AgregarUltima(sb, lista, ExpedienteNoteTitle,
            "Expediente documental ya construido (arbol de evidencia y ficha del cliente)");
        AgregarUltima(sb, lista, AuditoriaMedicinaNoteTitle,
            "DICTAMEN DE AUDITORIA MEDICA (paso previo, vinculante): pertinencia, "
            + "correlacion diagnostico-procedimiento y hallazgos. Debes respetarlo y citarlo "
            + "en la liquidacion; si te apartas de el, explica por que");

        return sb.ToString();
    }

    /// <summary>
    /// Anexa al contexto la nota mas reciente con ese titulo, rotulada para que
    /// el agente sepa que es un resultado YA producido y no lo repita.
    /// </summary>
    private static void AgregarUltima(StringBuilder sb, List<Note> notas, string titulo, string rotulo)
    {
        var n = notas
            .Where(x => string.Equals(x.Title, titulo, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(n?.Detail)) return;

        sb.AppendLine()
          .AppendLine($"## {rotulo}")
          .AppendLine(n!.Detail);
    }

    /// <summary>
    /// Extrae la cédula del beneficiario desde el contexto del sobre (si se conoce).
    /// Se usa como <c>CaseIdentity</c> del guardián anti-IDOR (D4): con cédula real,
    /// el guard impide que las tools consulten datos de OTRO afiliado.
    /// </summary>
    /// <param name="notes">Notas del caso.</param>
    /// <returns>La cédula, o null si el contexto no la tiene.</returns>
    public static string? ExtractCedulaFromCaseContext(IEnumerable<Note>? notes)
        => ExtractStringFromCaseContext(notes, "cedula");

    /// <summary>
    /// Extrae un valor string por clave del contexto del sobro (Note "ContextoSobre"):
    /// numeroSobre / numeroContrato / nombreTitular / cedula / producto / codigoRegion, etc.
    /// Tolerante a JSON ausente o malformado.
    /// </summary>
    public static string? ExtractStringFromCaseContext(IEnumerable<Note>? notes, string clave)
    {
        var json = BuildCaseContext(notes);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, clave, StringComparison.OrdinalIgnoreCase))
                {
                    var val = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.ToString();
                    return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
                }
            }
        }
        catch (JsonException) { }
        return null;
    }

    private static string? ExtractCedulaLegacy(IEnumerable<Note>? notes)
    {
        var json = BuildCaseContext(notes);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "cedula", StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.String)
                {
                    var val = prop.Value.GetString();
                    return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
                }
            }
        }
        catch (JsonException)
        {
            // Contexto malformado: mejor sin identidad (guard hace bypass) que romper el chat.
        }

        return null;
    }

    /// <summary>
    /// Resuelve el system prompt del agente para un paso de proceso orquestado.
    /// Base: SystemPrompt del Agent (o primer prompt si no hay) + los prompts
    /// vinculados en orden (skills apilables). Fallback con nombre del paso.
    /// </summary>
    /// <param name="agent">Agente IA del proceso.</param>
    /// <param name="step">Paso de proceso en ejecución (se usa en el fallback).</param>
    /// <returns>System prompt a usar en la llamada al modelo.</returns>
    public static string ResolveSystemPrompt(Agent agent, ProcessStep step)
    {
        var stacked = ResolveStackedPrompt(agent);
        if (!string.IsNullOrWhiteSpace(stacked))
            return stacked;

        return $"Eres un asistente IA especializado en análisis OCR. Ejecuta el paso: {step.StepName ?? step.StepOrder.ToString()}.";
    }

    /// <summary>
    /// Resuelve el system prompt del agente para el chat interactivo ad-hoc (sin paso de proceso).
    /// Base: SystemPrompt del Agent (o primer prompt si no hay) + los prompts
    /// vinculados en orden (skills apilables). Fallback genérico.
    /// </summary>
    /// <param name="agent">Agente IA seleccionado para el chat.</param>
    /// <returns>System prompt a usar en la llamada al modelo.</returns>
    public static string ResolveSystemPrompt(Agent agent)
    {
        var stacked = ResolveStackedPrompt(agent);
        return !string.IsNullOrWhiteSpace(stacked)
            ? stacked
            : "Eres un asistente IA especializado en análisis de documentos OCR. Responde con claridad y precisión.";
    }

    /// <summary>
    /// Compone el prompt "apilado": SystemPrompt del agente + TODOS los prompts
    /// vinculados (OPAIModelPrompt, en orden). Cada prompt vinculado funciona como
    /// una "skill" en Markdown que extiende al agente sin tocar código.
    /// </summary>
    private static string ResolveStackedPrompt(Agent agent)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(agent.SystemPrompt))
            sb.AppendLine(agent.SystemPrompt.Trim());

        var vinculados = agent.OPAIModelPrompt?
            .OrderBy(p => p.Order)
            .Select(p => p.PromptCodeNavigation?.Content)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();

        if (vinculados is { Count: > 0 })
        {
            foreach (var contenido in vinculados)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine(contenido!.Trim());
            }
        }

        return sb.ToString().Trim();
    }
}
