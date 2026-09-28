using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using app_ocr_ai_models.Data;
using app_tramites.Models.ModelAi;
using app_tramites.Models.ViewModel;
using app_tramites.Services.Ai;
using Microsoft.EntityFrameworkCore;

namespace app_ocr_ai_models.Services.Anexos;

// ============================================================
// REQ-046 — Implementación de la ingesta de anexos.
//   1) OCR del PDF (Azure Document Intelligence, reusa IOcrIngestService).
//   2) Claude (CLAUDE_FOUNDRY) estructura el texto en JSON normalizado.
//   3) Persiste en AnexoContrato/Anexo/AnexoCobertura/AnexoCarencia/
//      AnexoExclusion/AnexoClausula (upsert por plan/versión).
//   Deja traza completa en AnexoIngesta (OCR crudo + JSON + estado).
// ============================================================

/// <inheritdoc />
public sealed class AnexoIngestService : IAnexoIngestService
{
    private const string ClaudeConfigCode = "CLAUDE_FOUNDRY";

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private readonly OCRDbContext _db;
    private readonly IOcrIngestService _ocr;
    private readonly AiCompletionServiceFactory _factory;
    private readonly ILogger<AnexoIngestService> _logger;

    public AnexoIngestService(
        OCRDbContext db,
        IOcrIngestService ocr,
        AiCompletionServiceFactory factory,
        ILogger<AnexoIngestService> logger)
    {
        _db      = db      ?? throw new ArgumentNullException(nameof(db));
        _ocr     = ocr     ?? throw new ArgumentNullException(nameof(ocr));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger  = logger  ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AnexoIngestaResult> CargarYEstructurarAsync(
        OcrFile file,
        string? tipoContratoHint,
        string? codigoPlanHint,
        string? usuario,
        CancellationToken ct = default)
    {
        var result = new AnexoIngestaResult();

        // ── Traza inicial ─────────────────────────────────────────────────
        var ingesta = new AnexoIngesta
        {
            Archivo     = file.FileName ?? "anexo",
            Estado      = "RECIBIDO",
            CreatedBy   = usuario,
            CreatedDate = DateTime.UtcNow
        };
        _db.AnexoIngesta.Add(ingesta);
        await _db.SaveChangesAsync(ct);
        result.IngestaId = ingesta.Id;

        // ── 1) OCR (POR PÁGINA: la procedencia necesita saber en qué hoja está cada cosa) ──
        string ocrText, blobUrl, textoPaginado;
        try
        {
            var ocrRes    = await _ocr.ProcessFileDetailedAsync(file);
            blobUrl       = ocrRes.Url;
            ocrText       = ocrRes.Text;
            textoPaginado = ConstruirTextoPaginado(ocrRes);   // con marcas === PÁGINA n ===
            ingesta.ArchivoUri = blobUrl;
            ingesta.TextoOcr   = ocrText;
            ingesta.Estado     = "OCR_OK";
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            ingesta.Estado  = "ERROR";
            ingesta.Mensaje = "OCR falló: " + ex.Message;
            await _db.SaveChangesAsync(ct);
            result.Estado = "ERROR";
            result.Advertencias.Add(ingesta.Mensaje);
            _logger.LogWarning(ex, "[REQ-046] OCR falló para anexo {Archivo}.", file.FileName);
            return result;
        }

        if (string.IsNullOrWhiteSpace(ocrText))
        {
            ingesta.Estado  = "ERROR";
            ingesta.Mensaje = "El OCR no devolvió texto.";
            await _db.SaveChangesAsync(ct);
            result.Estado = "ERROR";
            result.Advertencias.Add(ingesta.Mensaje);
            return result;
        }

        // ── 2) Estructurar con Claude ─────────────────────────────────────
        var config = await _db.OPAIConfiguration
            .FirstOrDefaultAsync(c => c.Code == ClaudeConfigCode && c.IsActive, ct);
        if (config == null)
            throw new InvalidOperationException(
                $"[REQ-046] No existe la configuración '{ClaudeConfigCode}' activa para estructurar el anexo.");

        var extraido = await ExtraerEstructuraAsync(config, textoPaginado, tipoContratoHint, codigoPlanHint, ct);
        ingesta.JsonExtraido = extraido.Json;
        ingesta.Estado       = "ESTRUCTURADO";
        await _db.SaveChangesAsync(ct);
        result.Json = extraido.Json;

        if (extraido.Data == null)
        {
            ingesta.Estado  = "ERROR";
            ingesta.Mensaje = "Claude no devolvió un JSON estructurado válido.";
            await _db.SaveChangesAsync(ct);
            result.Estado = "ERROR";
            result.Advertencias.Add(ingesta.Mensaje);
            return result;
        }

        // ── 3) Persistir en la biblioteca ─────────────────────────────────
        try
        {
            await PersistirAsync(extraido.Data, tipoContratoHint, codigoPlanHint, blobUrl, usuario, result, ct);

            ingesta.ContratoId = result.ContratoId;
            ingesta.AnexoId    = result.AnexoIds.FirstOrDefault() == 0 ? (int?)null : result.AnexoIds.FirstOrDefault();
            ingesta.Estado     = "GUARDADO";
            await _db.SaveChangesAsync(ct);
            result.Estado = "GUARDADO";
        }
        catch (Exception ex)
        {
            ingesta.Estado  = "ERROR";
            ingesta.Mensaje = "Al guardar: " + ex.Message;
            await _db.SaveChangesAsync(ct);
            result.Estado = "ERROR";
            result.Advertencias.Add(ingesta.Mensaje);
            _logger.LogWarning(ex, "[REQ-046] Error al persistir el anexo {Archivo}.", file.FileName);
        }

        return result;
    }

    // ── Claude: OCR → JSON ────────────────────────────────────────────────
    private async Task<(string? Json, ExtraccionAnexo? Data)> ExtraerEstructuraAsync(
        OPAIConfiguration config, string ocrText, string? tipoHint, string? planHint, CancellationToken ct)
    {
        var system = new StringBuilder()
            .AppendLine("Eres un extractor de condiciones de contratos y anexos de salud de Saludsa (medicina prepagada).")
            .AppendLine("Recibes el TEXTO OCR de un contrato base (con cláusulas) o de un ANEXO de un plan (con las tablas de coberturas, topes, deducibles, carencias y exclusiones).")
            .AppendLine("El texto viene marcado por página con líneas '=== PÁGINA n ==='.")
            .AppendLine("Devuelve ÚNICAMENTE un JSON válido con la forma exacta que se indica. NO inventes datos: si un dato no consta en el texto, omite el campo o ponlo en null.")
            .AppendLine("Los porcentajes son números (80, no \"80%\"). Los montos son números sin separadores de miles.")
            .AppendLine("PROCEDENCIA OBLIGATORIA: por CADA cobertura, carencia, exclusión y cláusula, incluye 'pagina' (el número de la '=== PÁGINA n ===' donde aparece) y 'textoOrigen' (una frase VERBATIM y corta, copiada tal cual del texto de esa página, que permita ubicar el dato en el PDF). El textoOrigen debe existir literalmente en el OCR de esa página.")
            .AppendLine("Si el documento es un anexo de plan, llena 'anexos' con sus coberturas/carencias/exclusiones y deja 'clausulas' vacío.")
            .AppendLine("Si el documento es el contrato base, llena 'clausulas' y 'contrato'; 'anexos' puede ir vacío.")
            .ToString();

        var forma = """
        { "contrato": {"tipo":"Individual|Tradicional|OptimusPlus|Oncologico|Corporativo","codigoAcess":"","nombre":"","version":"","vigencia":""},
          "anexos": [ {"codigoPlan":"","nombrePlan":"","codigoProducto":"","version":"","resumenCondiciones":"",
             "coberturas":[{"beneficio":"","codigoBeneficio":"","porcentaje":0,"tope":0,"monedaTope":"USD","deducible":0,"copago":"","periodo":"anual|por evento","ambito":"nacional|internacional|red","notas":"","pagina":0,"textoOrigen":""}],
             "carencias":[{"beneficio":"","diasCarencia":0,"notas":"","pagina":0,"textoOrigen":""}],
             "exclusiones":[{"texto":"","clausulaRef":"","pagina":0,"textoOrigen":""}] } ],
          "clausulas": [ {"ordinal":"","numeral":"","literal":"","titulo":"","texto":"","pagina":0,"textoOrigen":""} ] }
        """;

        var user = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(tipoHint)) user.AppendLine($"Pista tipo de contrato: {tipoHint}");
        if (!string.IsNullOrWhiteSpace(planHint)) user.AppendLine($"Pista código de plan: {planHint}");
        user.AppendLine("Forma del JSON de salida:").AppendLine(forma).AppendLine();
        user.AppendLine("TEXTO OCR:").AppendLine(Truncar(ocrText, 120_000));

        var req = new AiCompletionRequest
        {
            SystemPrompt = system,
            // Un anexo con muchas coberturas produce un JSON largo; con 8000 se
            // truncaba a medias y no parseaba. Opus admite salidas grandes.
            UserMessage  = user.ToString(),
            MaxTokens    = 32000,
            ThinkingMode = "off"
        };

        try
        {
            var svc = _factory.Create(config);
            var res = await svc.CompleteAsync(req, ct);
            var json = ExtractJson(res.Text);
            if (string.IsNullOrWhiteSpace(json)) return (res.Text, null);
            var data = JsonSerializer.Deserialize<ExtraccionAnexo>(json, JsonOpts);
            return (json, data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REQ-046] Claude no pudo estructurar el anexo.");
            return (null, null);
        }
    }

    // ── Persistencia (upsert por plan/versión) ────────────────────────────
    private async Task PersistirAsync(
        ExtraccionAnexo data, string? tipoHint, string? planHint, string blobUrl,
        string? usuario, AnexoIngestaResult result, CancellationToken ct)
    {
        // Contrato base
        AnexoContrato? contrato = null;
        var tipo = data.Contrato?.Tipo ?? tipoHint;
        if (!string.IsNullOrWhiteSpace(tipo) || !string.IsNullOrWhiteSpace(data.Contrato?.CodigoAcess))
        {
            var codAcess = data.Contrato?.CodigoAcess;
            contrato = await _db.AnexoContrato.FirstOrDefaultAsync(c =>
                (codAcess != null && c.CodigoAcess == codAcess) ||
                (c.Tipo == tipo && c.Version == data.Contrato!.Version), ct);

            if (contrato == null)
            {
                contrato = new AnexoContrato
                {
                    Tipo        = string.IsNullOrWhiteSpace(tipo) ? "Individual" : tipo!,
                    CodigoAcess = codAcess,
                    Nombre      = data.Contrato?.Nombre ?? tipo ?? "Contrato",
                    Version     = data.Contrato?.Version,
                    Vigencia    = data.Contrato?.Vigencia,
                    ArchivoUri  = blobUrl,
                    CreatedBy   = usuario,
                    CreatedDate = DateTime.UtcNow
                };
                _db.AnexoContrato.Add(contrato);
                await _db.SaveChangesAsync(ct);
            }
            result.ContratoId = contrato.Id;

            // Cláusulas del contrato base (reemplazo total del set)
            if (data.Clausulas is { Count: > 0 })
            {
                var viejas = _db.AnexoClausula.Where(x => x.ContratoId == contrato.Id);
                _db.AnexoClausula.RemoveRange(viejas);
                foreach (var cl in data.Clausulas)
                {
                    if (string.IsNullOrWhiteSpace(cl.Texto)) continue;
                    _db.AnexoClausula.Add(new AnexoClausula
                    {
                        ContratoId  = contrato.Id,
                        Ordinal     = Cortar(cl.Ordinal, 60),
                        Numeral     = Cortar(cl.Numeral, 20),
                        Literal     = Cortar(cl.Literal, 20),
                        Titulo      = Cortar(cl.Titulo, 200),
                        Texto       = cl.Texto!,
                        Pagina      = cl.Pagina,
                        TextoOrigen = Cortar(cl.TextoOrigen, 2000),
                        CreatedDate = DateTime.UtcNow
                    });
                    result.Clausulas++;
                }
                await _db.SaveChangesAsync(ct);
            }
        }

        // Anexos por plan
        var anexos = data.Anexos ?? new List<ExtraccionAnexoPlan>();
        foreach (var a in anexos)
        {
            // Prioridad para la clave del anexo: código explícito → pista del operador →
            // nombre del plan (muchos anexos POOL no traen un "código" pero sí el nombre).
            var codigoPlan = a.CodigoPlan;
            if (string.IsNullOrWhiteSpace(codigoPlan)) codigoPlan = planHint;
            if (string.IsNullOrWhiteSpace(codigoPlan)) codigoPlan = a.NombrePlan;
            if (string.IsNullOrWhiteSpace(codigoPlan))
            {
                result.Advertencias.Add("Un anexo llegó sin código ni nombre de plan; se omitió.");
                continue;
            }
            codigoPlan = codigoPlan.Trim();
            if (codigoPlan.Length > 40) codigoPlan = codigoPlan.Substring(0, 40);

            var anexo = await _db.Anexo.FirstOrDefaultAsync(x =>
                x.CodigoPlan == codigoPlan && x.Version == a.Version && x.IsActive, ct);

            if (anexo == null)
            {
                anexo = new Anexo
                {
                    ContratoId         = contrato?.Id,
                    CodigoPlan         = codigoPlan!,
                    NombrePlan         = Cortar(a.NombrePlan, 200),
                    CodigoProducto     = Cortar(a.CodigoProducto, 20),
                    Version            = Cortar(a.Version, 40),
                    ArchivoUri         = blobUrl,
                    ResumenCondiciones = a.ResumenCondiciones,
                    Estado             = "ACTIVO",
                    CreatedBy          = usuario,
                    CreatedDate        = DateTime.UtcNow
                };
                _db.Anexo.Add(anexo);
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                anexo.ContratoId         = contrato?.Id ?? anexo.ContratoId;
                anexo.NombrePlan         = Cortar(a.NombrePlan, 200) ?? anexo.NombrePlan;
                anexo.CodigoProducto     = Cortar(a.CodigoProducto, 20) ?? anexo.CodigoProducto;
                anexo.ResumenCondiciones = a.ResumenCondiciones ?? anexo.ResumenCondiciones;
                anexo.ArchivoUri         = blobUrl;
                anexo.ModifiedDate       = DateTime.UtcNow;
                // Reemplazar sets hijos
                _db.AnexoCobertura.RemoveRange(_db.AnexoCobertura.Where(x => x.AnexoId == anexo.Id));
                _db.AnexoCarencia.RemoveRange(_db.AnexoCarencia.Where(x => x.AnexoId == anexo.Id));
                _db.AnexoExclusion.RemoveRange(_db.AnexoExclusion.Where(x => x.AnexoId == anexo.Id));
                await _db.SaveChangesAsync(ct);
            }
            result.AnexoIds.Add(anexo.Id);

            foreach (var c in a.Coberturas ?? new List<ExtraccionCobertura>())
            {
                if (string.IsNullOrWhiteSpace(c.Beneficio)) continue;
                _db.AnexoCobertura.Add(new AnexoCobertura
                {
                    AnexoId         = anexo.Id,
                    Beneficio       = Cortar(c.Beneficio, 200)!,
                    CodigoBeneficio = Cortar(c.CodigoBeneficio, 20),
                    Porcentaje      = c.Porcentaje,
                    Tope            = c.Tope,
                    MonedaTope      = Cortar(c.MonedaTope, 10),
                    Deducible       = c.Deducible,
                    Copago          = Cortar(c.Copago, 100),
                    Periodo         = Cortar(c.Periodo, 60),
                    Ambito          = Cortar(c.Ambito, 60),
                    Notas           = Cortar(c.Notas, 500),
                    Pagina          = c.Pagina,
                    TextoOrigen     = Cortar(c.TextoOrigen, 2000),
                    CreatedDate     = DateTime.UtcNow
                });
                result.Coberturas++;
            }
            foreach (var c in a.Carencias ?? new List<ExtraccionCarencia>())
            {
                if (string.IsNullOrWhiteSpace(c.Beneficio)) continue;
                _db.AnexoCarencia.Add(new AnexoCarencia
                {
                    AnexoId      = anexo.Id,
                    Beneficio    = Cortar(c.Beneficio, 200)!,
                    DiasCarencia = c.DiasCarencia,
                    Notas        = Cortar(c.Notas, 500),
                    Pagina       = c.Pagina,
                    TextoOrigen  = Cortar(c.TextoOrigen, 2000),
                    CreatedDate  = DateTime.UtcNow
                });
                result.Carencias++;
            }
            foreach (var x in a.Exclusiones ?? new List<ExtraccionExclusion>())
            {
                if (string.IsNullOrWhiteSpace(x.Texto)) continue;
                _db.AnexoExclusion.Add(new AnexoExclusion
                {
                    AnexoId     = anexo.Id,
                    ContratoId  = contrato?.Id,
                    Texto       = Cortar(x.Texto, 1000)!,
                    ClausulaRef = Cortar(x.ClausulaRef, 200),
                    Pagina      = x.Pagina,
                    TextoOrigen = Cortar(x.TextoOrigen, 2000),
                    CreatedDate = DateTime.UtcNow
                });
                result.Exclusiones++;
            }
            await _db.SaveChangesAsync(ct);
        }
    }

    // ── Utilidades ────────────────────────────────────────────────────────
    private static string Truncar(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    private static string? Cortar(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : (s.Length <= max ? s : s.Substring(0, max));

    /// <summary>Arma el texto OCR con marcas '=== PÁGINA n ===' para que Claude
    /// pueda anotar de qué página salió cada dato (procedencia).</summary>
    private static string ConstruirTextoPaginado(OcrResultado ocr)
    {
        if (ocr.Paginas == null || ocr.Paginas.Count == 0)
            return ocr.Text ?? string.Empty;
        var sb = new StringBuilder();
        foreach (var p in ocr.Paginas.OrderBy(p => p.PageNumber))
            sb.AppendLine($"=== PÁGINA {p.PageNumber} ===")
              .AppendLine(p.Text ?? string.Empty)
              .AppendLine();
        return sb.ToString();
    }

    private static string? ExtractJson(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var start = texto.IndexOf('{');
        if (start < 0) return null;
        int depth = 0; bool inStr = false; char prev = '\0';
        for (var i = start; i < texto.Length; i++)
        {
            var c = texto[i];
            if (inStr) { if (c == '"' && prev != '\\') inStr = false; }
            else
            {
                if (c == '"') inStr = true;
                else if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return texto.Substring(start, i - start + 1); }
            }
            prev = c;
        }
        return null;
    }

    // ── DTOs de deserialización del JSON de Claude ────────────────────────
    private sealed class ExtraccionAnexo
    {
        [JsonPropertyName("contrato")]  public ExtraccionContrato? Contrato { get; set; }
        [JsonPropertyName("anexos")]    public List<ExtraccionAnexoPlan>? Anexos { get; set; }
        [JsonPropertyName("clausulas")] public List<ExtraccionClausula>? Clausulas { get; set; }
    }
    private sealed class ExtraccionContrato
    {
        public string? Tipo { get; set; }
        public string? CodigoAcess { get; set; }
        public string? Nombre { get; set; }
        public string? Version { get; set; }
        public string? Vigencia { get; set; }
    }
    private sealed class ExtraccionAnexoPlan
    {
        public string? CodigoPlan { get; set; }
        public string? NombrePlan { get; set; }
        public string? CodigoProducto { get; set; }
        public string? Version { get; set; }
        public string? ResumenCondiciones { get; set; }
        public List<ExtraccionCobertura>? Coberturas { get; set; }
        public List<ExtraccionCarencia>? Carencias { get; set; }
        public List<ExtraccionExclusion>? Exclusiones { get; set; }
    }
    private sealed class ExtraccionCobertura
    {
        public string? Beneficio { get; set; }
        public string? CodigoBeneficio { get; set; }
        public decimal? Porcentaje { get; set; }
        public decimal? Tope { get; set; }
        public string? MonedaTope { get; set; }
        public decimal? Deducible { get; set; }
        public string? Copago { get; set; }
        public string? Periodo { get; set; }
        public string? Ambito { get; set; }
        public string? Notas { get; set; }
        public int? Pagina { get; set; }
        public string? TextoOrigen { get; set; }
    }
    private sealed class ExtraccionCarencia
    {
        public string? Beneficio { get; set; }
        public int? DiasCarencia { get; set; }
        public string? Notas { get; set; }
        public int? Pagina { get; set; }
        public string? TextoOrigen { get; set; }
    }
    private sealed class ExtraccionExclusion
    {
        public string? Texto { get; set; }
        public string? ClausulaRef { get; set; }
        public int? Pagina { get; set; }
        public string? TextoOrigen { get; set; }
    }
    private sealed class ExtraccionClausula
    {
        public string? Ordinal { get; set; }
        public string? Numeral { get; set; }
        public string? Literal { get; set; }
        public string? Titulo { get; set; }
        public string? Texto { get; set; }
        public int? Pagina { get; set; }
        public string? TextoOrigen { get; set; }
    }
}
