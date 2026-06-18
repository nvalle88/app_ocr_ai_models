using app_tramites.Models.ModelAi;
using System.Text;

namespace app_tramites.Services.Ai;

// REQ-019: DRY — extrae la lógica compartida de resolución de system prompt y construcción
// del contexto OCR que estaba duplicada entre ProcessOrchestrator y ChatController.
// Comportamiento idéntico al código original en ambos consumidores.

/// <summary>
/// Métodos de utilidad estáticos compartidos entre <see cref="ProcessOrchestrator"/>
/// y <see cref="app_ocr_ai_models.Areas.Studio.Controllers.ChatController"/>
/// para construir el contexto OCR y resolver el system prompt del agente.
/// </summary>
public static class OcrPromptHelper
{
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
    /// Resuelve el system prompt del agente para un paso de proceso orquestado.
    /// Prioridad: SystemPrompt del Agent → primer OPAIPrompt activo → fallback con nombre del paso.
    /// </summary>
    /// <param name="agent">Agente IA del proceso.</param>
    /// <param name="step">Paso de proceso en ejecución (se usa en el fallback).</param>
    /// <returns>System prompt a usar en la llamada al modelo.</returns>
    public static string ResolveSystemPrompt(Agent agent, ProcessStep step)
    {
        if (!string.IsNullOrWhiteSpace(agent.SystemPrompt))
            return agent.SystemPrompt;

        var promptContent = agent.OPAIModelPrompt
            ?.OrderBy(p => p.Order)
            .Select(p => p.PromptCodeNavigation?.Content)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        if (!string.IsNullOrWhiteSpace(promptContent))
            return promptContent;

        return $"Eres un asistente IA especializado en análisis OCR. Ejecuta el paso: {step.StepName ?? step.StepOrder.ToString()}.";
    }

    /// <summary>
    /// Resuelve el system prompt del agente para el chat interactivo ad-hoc (sin paso de proceso).
    /// Prioridad: SystemPrompt del Agent → primer OPAIPrompt activo → fallback genérico.
    /// </summary>
    /// <param name="agent">Agente IA seleccionado para el chat.</param>
    /// <returns>System prompt a usar en la llamada al modelo.</returns>
    public static string ResolveSystemPrompt(Agent agent)
    {
        if (!string.IsNullOrWhiteSpace(agent.SystemPrompt))
            return agent.SystemPrompt;

        var promptContent = agent.OPAIModelPrompt
            ?.OrderBy(p => p.Order)
            .Select(p => p.PromptCodeNavigation?.Content)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        return !string.IsNullOrWhiteSpace(promptContent)
            ? promptContent
            : "Eres un asistente IA especializado en análisis de documentos OCR. Responde con claridad y precisión.";
    }
}
