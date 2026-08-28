using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using app_ocr_ai_models.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace app_ocr_ai_models.Areas.Studio.ViewComponents;

// =============================================================================
// REQ-019 — Trazabilidad uniforme: QUÉ herramientas y QUÉ skills se usaron.
//
// La primera versión mostraba lo mismo en todas las pantallas, y eso era un
// defecto, no una coincidencia:
//
//   · Las skills salían de OPAIModelPrompt SIN filtro alguno, así que pintaba
//     los prompts de los doce modelos de la base (nexus-facturas, chat-nexus,
//     gpt-4o-contrato…), tuvieran o no algo que ver con la pantalla.
//   · Las invocaciones se filtraban solo por caso, de modo que Tipificación
//     mostraba también las llamadas del auditor y las de la resolución, todas
//     revueltas y sin decir de quién era cada una.
//
// Ahora cada pantalla declara el agente del que rinde cuentas y ve exactamente
// sus llamadas y sus skills. Si no declara ninguno se muestran todas, pero con
// el agente a la vista en cada fila.
//
// Además, cuando una llamada falla se enseña el motivo real que quedó guardado
// en ToolInvocation.ResponseJson: un "error" sin causa no sirve para nada.
// =============================================================================

public sealed class ToolsUsadasViewComponent : ViewComponent
{
    private readonly OCRDbContext _db;

    public ToolsUsadasViewComponent(OCRDbContext db) => _db = db;

    /// <summary>Nombre legible del agente; el código en crudo no le dice nada al operador.</summary>
    private static string Agente(string? codigo) => (codigo ?? string.Empty).Trim() switch
    {
        "AGENTE_CLASIFICADOR_DOC" => "Clasificador de documentos",
        "AGENTE_AUDITOR_MEDICINA" => "Auditor médico",
        "AGENTE_CLAUDE"           => "Agente de resolución",
        "clasificar-rembolso"     => "Clasificador de reembolso",
        "chat-nexus"              => "Chat",
        ""                        => "(sin agente)",
        var otro                  => otro
    };

    /// <param name="modelo">
    /// Código del agente del que rinde cuentas la pantalla. Si va en null se
    /// muestran todos los del caso, identificados uno por uno.
    /// </param>
    public async Task<IViewComponentResult> InvokeAsync(Guid caseCode, string? modelo = null, int max = 60)
    {
        if (caseCode == Guid.Empty)
            return View(new ToolsUsadasVm());

        var modeloFiltro = string.IsNullOrWhiteSpace(modelo) ? null : modelo.Trim();

        // Las invocaciones se atan al caso a través de StepExecution, que es
        // quien sabe qué agente estaba corriendo.
        var consulta = from ti in _db.ToolInvocation.AsNoTracking()
                       join se in _db.StepExecution.AsNoTracking()
                           on ti.ExecutionId equals se.ExecutionId
                       where se.CaseCode == caseCode
                       select new
                       {
                           ti.ToolCode,
                           ti.IsError,
                           ti.StartDate,
                           ti.EndDate,
                           ti.RequestJson,
                           ti.ResponseJson,
                           se.ModelCode
                       };

        if (modeloFiltro != null)
            consulta = consulta.Where(x => x.ModelCode == modeloFiltro);

        var filas = await consulta
            .OrderByDescending(x => x.StartDate)
            .Take(max)
            .ToListAsync();

        var vm = new ToolsUsadasVm
        {
            CaseCode = caseCode,
            ModeloFiltro = modeloFiltro,
            AgenteFiltro = modeloFiltro is null ? null : Agente(modeloFiltro),
            // Con un solo agente sobra repetir su nombre en cada fila
            MostrarAgente = modeloFiltro is null,
            Invocaciones = filas.Select(f => new ToolUsadaVm
            {
                ToolCode = f.ToolCode ?? "(sin código)",
                Agente = Agente(f.ModelCode),
                EsError = f.IsError,
                Cuando = f.StartDate,
                DuracionSeg = f.EndDate.HasValue && f.StartDate != default
                    ? (decimal?)Math.Round((f.EndDate.Value - f.StartDate).TotalSeconds, 1)
                    : null,
                Entrada = Recorta(f.RequestJson, 130),
                // El porqué del fallo, no un "error" mudo
                Motivo = f.IsError ? MotivoDelError(f.ResponseJson) : null
            }).ToList()
        };

        // Skills = los prompts apilados de los agentes que REALMENTE corrieron
        // en este caso. Son doce modelos en la base y antes se listaban todos.
        var modelosDelCaso = modeloFiltro != null
            ? new List<string> { modeloFiltro }
            : await _db.StepExecution.AsNoTracking()
                .Where(se => se.CaseCode == caseCode && se.ModelCode != null)
                .Select(se => se.ModelCode!)
                .Distinct()
                .ToListAsync();

        // El ModelCode viene con espacios y saltos de línea en algunas filas, así
        // que la comparación se hace en memoria: son doce prompts, no hay coste.
        var prompts = await _db.OPAIModelPrompt.AsNoTracking()
            .OrderBy(mp => mp.Order)
            .Select(mp => new { mp.ModelCode, mp.PromptCode })
            .ToListAsync();

        var normalizados = modelosDelCaso
            .Select(m => m.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        vm.Skills = prompts
            .Where(p => normalizados.Contains((p.ModelCode ?? string.Empty).Trim()))
            .Select(p => (p.PromptCode ?? string.Empty).Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return View(vm);
    }

    /// <summary>
    /// Saca el mensaje que el ejecutor guardó cuando la llamada falló. El JSON
    /// tiene la forma error/message; si no se puede leer se devuelve el texto
    /// recortado, que sigue siendo mejor que nada.
    /// </summary>
    private static string? MotivoDelError(string? responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("message", out var m))
                    return Recorta(m.GetString(), 220);
                if (doc.RootElement.TryGetProperty("error", out var e))
                    return Recorta(e.GetString(), 220);
            }
        }
        catch (JsonException) { /* no era JSON: cae al recorte de abajo */ }
        return Recorta(responseJson, 220);
    }

    private static string? Recorta(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }
}

public sealed class ToolsUsadasVm
{
    public Guid CaseCode { get; set; }
    public string? ModeloFiltro { get; set; }
    public string? AgenteFiltro { get; set; }
    public bool MostrarAgente { get; set; }
    public List<ToolUsadaVm> Invocaciones { get; set; } = new();
    public List<string> Skills { get; set; } = new();

    public int Correctas => Invocaciones.Count(x => !x.EsError);
    public int ConError => Invocaciones.Count(x => x.EsError);

    /// <summary>Herramientas distintas: es lo que de verdad se consultó.</summary>
    public int Distintas => Invocaciones.Select(x => x.ToolCode)
                                        .Distinct(StringComparer.OrdinalIgnoreCase).Count();
}

public sealed class ToolUsadaVm
{
    public string ToolCode { get; set; } = string.Empty;
    public string Agente { get; set; } = string.Empty;
    public bool EsError { get; set; }
    public DateTime Cuando { get; set; }
    public decimal? DuracionSeg { get; set; }
    public string? Entrada { get; set; }
    public string? Motivo { get; set; }
}
