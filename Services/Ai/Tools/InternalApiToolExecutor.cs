using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using app_ocr_ai_models.Data;
using app_tramites.Models.ModelAi;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace app_tramites.Services.Ai.Tools;

// ============================================================
// REQ-019 T5 — Executor genérico de tools (InternalApi + Armonix).
// D2: solo ejecuta tools vinculadas+IsEnabled al agente.
// D4: aplica guardián anti-IDOR antes de salir a la API.
// ============================================================

/// <summary>
/// Implementación de <see cref="IToolExecutor"/> para los binding types
/// <c>InternalApi</c> y <c>Armonix</c> (mismo executor HTTP, distinta baseUrl).
/// Lee <c>OPAITool.BindingConfig</c>, arma el request, inyecta el token Saludsa,
/// y persiste <see cref="ToolInvocation"/> en BD.
/// </summary>
/// <remarks>
/// Dispatcher por <c>BindingType</c>:
/// <list type="bullet">
///   <item><c>InternalApi</c> — APIs internas (api-contrato, api-prestador).</item>
///   <item><c>Armonix</c>    — APIs de Armonix (api-armonix); mismo flujo HTTP.</item>
///   <item><c>Mcp</c>        — Reconocido pero lanza <see cref="NotImplementedException"/>.
///     Comentario: "MCP-ready: futuro servidor MCP".</item>
/// </list>
/// Las <c>baseUrl</c> con placeholder (<c>{api-contrato}</c>, etc.) se resuelven
/// desde configuración (B1/T0a). No hay red si no están configuradas.
/// </remarks>
public sealed class InternalApiToolExecutor : IToolExecutor
{
    private readonly OCRDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly app_tramites.Services.Zendesk.ParametrosZendesk? _parametrosZendesk;
    private readonly ISaludsaTokenProvider _tokenProvider;
    private readonly IToolAuthorizationGuard _authGuard;
    private readonly IConfiguration _config;
    private readonly ILogger<InternalApiToolExecutor> _logger;

    private static readonly JsonSerializerOptions SerializerOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Inicializa el executor con sus dependencias.
    /// </summary>
    /// <param name="db">Contexto EF para persistir <see cref="ToolInvocation"/>.</param>
    /// <param name="httpClientFactory">Factory de HttpClient para llamadas a la API.</param>
    /// <param name="tokenProvider">Proveedor de token OAuth2 Saludsa.</param>
    /// <param name="authGuard">Guardián anti-IDOR (D4).</param>
    /// <param name="config">Configuración de la aplicación (resolución de baseUrl).</param>
    /// <param name="logger">Logger.</param>
    // ── Memoria de los servidores que no contestan ──────────────────────
    //
    // Medido en el caso 598ec576: OCHO llamadas a tools SQL fallaron con
    // "A network-related or instance-specific error occurred", y CADA UNA tardo
    // ~30 segundos —el Connect Timeout de la cadena— antes de rendirse. Cuatro
    // minutos de espera muerta en un solo caso, sin un solo dato a cambio. Y
    // encima repetidas: historial_reembolsos_cliente_bd lo intento 3 veces y
    // resolver_convenio_por_ruc 2, siempre contra la misma maquina inalcanzable.
    //
    // Que un host este caido es una propiedad del HOST, no de la consulta. Se
    // recuerda un rato corto: lo justo para no repetir el castigo dentro de la
    // misma resolucion, y lo bastante poco para que en cuanto vuelva la VPN se
    // reintente solo. No se cachea NADA de datos: solo el hecho de que no
    // contesta.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>
        _noContesta = new(StringComparer.OrdinalIgnoreCase);

    private const int SegundosRecordandoElHostCaido = 60;

    /// <summary>Si ese destino se dio por caido hace poco, no se vuelve a intentar todavia.</summary>
    private static bool SigueCaido(string clave)
    {
        if (!_noContesta.TryGetValue(clave, out var hasta)) return false;
        if (DateTime.UtcNow < hasta) return true;

        // Ya cumplio: se olvida y se le da otra oportunidad.
        _noContesta.TryRemove(clave, out _);
        return false;
    }

    private static void ApuntarCaido(string clave) =>
        _noContesta[clave] = DateTime.UtcNow.AddSeconds(SegundosRecordandoElHostCaido);

    // ── El mismo caso no pregunta dos veces lo mismo ─────────────────────────
    //
    // Medido sobre los ocho ultimos casos del portal: resolver_contrato_por_cedula
    // se llamo 2,4 veces POR CASO, factura_ya_pagada_bd 2,5 y resolver_convenio_
    // por_ruc 2,0. Son lecturas, y dentro de un mismo caso la respuesta es la
    // misma: cada repeticion es una ida a la VPN y unos segundos de espera que el
    // afiliado paga mirando una pantalla quieta.
    //
    // El memo vive DIEZ MINUTOS y se llavea por caso + tool + argumentos. No es
    // una cache de datos de negocio: es no repetir la misma pregunta dentro de la
    // misma conversacion. Pasados los diez minutos se vuelve a preguntar, porque
    // un caso que se retoma al rato merece datos frescos.
    //
    // Solo se memoriza lo que SALIO BIEN. Un fallo se reintenta: cachearlo
    // convertiria un tropiezo de red en una respuesta equivocada durante diez
    // minutos.
    private static readonly Microsoft.Extensions.Caching.Memory.MemoryCache _memo =
        new(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions { SizeLimit = 4000 });

    private static readonly TimeSpan VidaDelMemo = TimeSpan.FromMinutes(10);

    /// <summary>La misma pregunta: mismo caso, misma tool y mismos argumentos.</summary>
    private static string LlaveDelMemo(string caso, string toolCode,
                                       IReadOnlyDictionary<string, object?> entrada)
    {
        var partes = entrada
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => kv.Key.ToLowerInvariant() + "=" + (kv.Value?.ToString() ?? string.Empty));
        return caso + "|" + toolCode + "|" + string.Join("&", partes);
    }

    public InternalApiToolExecutor(
        OCRDbContext db,
        IHttpClientFactory httpClientFactory,
        ISaludsaTokenProvider tokenProvider,
        IToolAuthorizationGuard authGuard,
        IConfiguration config,
        ILogger<InternalApiToolExecutor> logger,
        app_tramites.Services.Zendesk.ParametrosZendesk? parametrosZendesk = null)
    {
        // Opcional a proposito: si alguien monta el ejecutor sin el lector de
        // parametros, las demas tools siguen funcionando y solo la de Zendesk
        // avisa. Un servicio que falta no debe tumbar el resto del catalogo.
        _parametrosZendesk = parametrosZendesk;
        _db              = db              ?? throw new ArgumentNullException(nameof(db));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _tokenProvider   = tokenProvider   ?? throw new ArgumentNullException(nameof(tokenProvider));
        _authGuard       = authGuard       ?? throw new ArgumentNullException(nameof(authGuard));
        _config          = config          ?? throw new ArgumentNullException(nameof(config));
        _logger          = logger          ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <summary>
    /// Ejecuta la tool y, si falla y tiene alternativa declarada, la intenta por
    /// el otro camino.
    ///
    /// El caso que lo motivó: desde Azure no se alcanza el gateway de Saludsa
    /// —puerto 443 sin abrir, 8 segundos— pero sí las bases SQL. Antes eso
    /// dejaba muda a la tool y el afiliado se quedaba sin respuesta. Ahora, si
    /// la tool declara <c>fallbackTool</c> en su BindingConfig, se prueba esa.
    ///
    /// Sólo se encadena UNA vez: la alternativa de la alternativa no se sigue.
    /// Un ciclo entre dos tools que se apunten mutuamente colgaría la conversación.
    /// </summary>
    public async Task<string> ExecuteAsync(
        string toolCode,
        string agentCode,
        IReadOnlyDictionary<string, object?> toolInput,
        long executionId,
        string? caseIdentity = null,
        CancellationToken ct = default)
    {
        try
        {
            return await EjecutarUnaAsync(toolCode, agentCode, toolInput, executionId,
                                          caseIdentity, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException
                                   && !ct.IsCancellationRequested)
        {
            // El acceso denegado NO se reintenta por otro camino: si el guardián
            // anti-IDOR dijo que no, la respuesta es no, venga por donde venga.
            var alterna = await AlternativaDeAsync(toolCode, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(alterna))
                throw;

            _logger.LogWarning(ex,
                "[T5] La tool '{Tool}' fallo. Se intenta por su alternativa '{Alterna}'.",
                toolCode, alterna);

            var json = await EjecutarUnaAsync(alterna!, agentCode, toolInput, executionId,
                                              caseIdentity, ct).ConfigureAwait(false);

            // Se dice POR DONDE vino. Presentar el resultado del camino B como si
            // fuera el A seria dar por equivalente lo que no lo es: el API aplica
            // reglas de negocio que una consulta no reproduce entera.
            return AnadirDeDondeVino(json, toolCode, alterna!);
        }
    }

    /// <summary>El <c>fallbackTool</c> declarado por una tool, si lo tiene.</summary>
    private async Task<string?> AlternativaDeAsync(string toolCode, CancellationToken ct)
    {
        try
        {
            var cfg = await _db.OPAITool.AsNoTracking()
                .Where(t => t.Code == toolCode)
                .Select(t => t.BindingConfig)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(cfg)) return null;

            using var doc = JsonDocument.Parse(cfg);
            return doc.RootElement.TryGetProperty("fallbackTool", out var f)
                   && f.ValueKind == JsonValueKind.String
                ? f.GetString()
                : null;
        }
        catch
        {
            // Buscar la alternativa NO puede tumbar el error original: si esto
            // falla, que se propague el fallo de verdad, que es el informativo.
            return null;
        }
    }

    /// <summary>Marca la respuesta con el camino por el que llego.</summary>
    private static string AnadirDeDondeVino(string json, string pedida, string usada)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return json;

            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject()) prop.WriteTo(w);
                w.WriteString("_porDondeVino",
                    $"'{pedida}' no respondio, asi que esto sale de '{usada}', que consulta la "
                    + "base directamente. Puede no traer todo lo que calcula el servicio.");
                w.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            return json;
        }
    }

    private async Task<string> EjecutarUnaAsync(
        string toolCode,
        string agentCode,
        IReadOnlyDictionary<string, object?> toolInput,
        long executionId,
        string? caseIdentity = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentCode);

        // ¿Ya se pregunto esto en este mismo caso? Entonces no se vuelve a
        // preguntar. Sin caso identificado no hay memo: no habria con que llavear.
        var llaveMemo = string.IsNullOrWhiteSpace(caseIdentity)
            ? null
            : LlaveDelMemo(caseIdentity!, toolCode, toolInput);

        if (llaveMemo != null && _memo.TryGetValue(llaveMemo, out var guardado)
            && guardado is string yaSabido)
        {
            _logger.LogDebug("[T5] '{Tool}' ya se habia consultado en este caso: no se repite.", toolCode);
            return yaSabido;
        }

        // ── 1. Cargar la tool del catálogo ───────────────────────────────
        var tool = await _db.OPAITool
            .FirstOrDefaultAsync(t => t.Code == toolCode && t.IsActive, ct)
            .ConfigureAwait(false);

        if (tool == null)
            throw new InvalidOperationException(
                $"[T5] Tool '{toolCode}' no encontrada o inactiva en el catálogo.");

        // ── 2. D2: Verificar que la tool está vinculada al agente (IsEnabled) ──
        var modelTool = await _db.OPAIModelTool
            .FirstOrDefaultAsync(mt =>
                mt.ModelCode == agentCode
                && mt.ToolCode == toolCode
                && mt.IsEnabled, ct)
            .ConfigureAwait(false);

        if (modelTool == null)
            throw new UnauthorizedAccessException(
                $"[T5 D2] Tool '{toolCode}' no está habilitada para el agente '{agentCode}'. " +
                "Vincúlela en OPAIModelTool con IsEnabled=1.");

        // ── 3. Deserializar BindingConfig ────────────────────────────────
        if (string.IsNullOrWhiteSpace(tool.BindingConfig))
            throw new InvalidOperationException(
                $"[T5] Tool '{toolCode}' no tiene BindingConfig configurado.");

        ToolBindingConfig binding;
        try
        {
            binding = JsonSerializer.Deserialize<ToolBindingConfig>(
                tool.BindingConfig,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("BindingConfig deserializó como null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"[T5] BindingConfig de la tool '{toolCode}' no es JSON válido: {ex.Message}", ex);
        }

        // ── 4. Dispatcher por BindingType ────────────────────────────────
        if (string.Equals(tool.BindingType, "Mcp", StringComparison.OrdinalIgnoreCase))
        {
            // MCP-ready: futuro servidor MCP — no implementado en esta iteración (T5).
            // El soporte MCP se añadirá cuando se disponga del servidor MCP Saludsa.
            throw new NotImplementedException(
                "[T5] BindingType 'Mcp' no está implementado. " +
                "MCP-ready: se integrará cuando exista el servidor MCP Saludsa.");
        }

        // InternalApi y Armonix comparten el mismo executor HTTP; Sql va a BD directa.
        var esSql = string.Equals(tool.BindingType, "Sql", StringComparison.OrdinalIgnoreCase);
        if (!esSql
            && !string.Equals(tool.BindingType, "InternalApi", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(tool.BindingType, "Armonix", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(tool.BindingType, "Zendesk", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"[T5] BindingType '{tool.BindingType}' no soportado por InternalApiToolExecutor. " +
                "Tipos soportados: InternalApi, Armonix, Sql, Zendesk.");
        }

        // ── 5. D4: Guardián anti-IDOR ────────────────────────────────────
        // La identidad no es una sola cédula: un contrato cubre al titular y a
        // sus dependientes, y el input puede traer contrato o número de persona,
        // que son otras clases de identificador. Se arma el conjunto completo a
        // partir del contexto del caso (REQ-020d).
        var identidad = await ResolverIdentidadCasoAsync(executionId, caseIdentity, ct)
            .ConfigureAwait(false);

        if (!_authGuard.IsAuthorized(toolCode, toolInput, identidad))
        {
            var denialJson = JsonSerializer.Serialize(new
            {
                error   = "IDOR_DENIED",
                message = $"[T5 D4] Los identificadores de afiliado en el input de la tool " +
                          $"'{toolCode}' no corresponden a este caso ({identidad}). Acceso denegado."
            });

            // Registrar el intento en ToolInvocation con IsError=true
            await PersistInvocationAsync(
                executionId, toolCode,
                requestJson:  JsonSerializer.Serialize(toolInput),
                responseJson: denialJson,
                isError:      true,
                start:        DateTime.UtcNow,
                ct:           ct)
                .ConfigureAwait(false);

            _logger.LogWarning(
                "[T5 D4] IDOR_DENIED tool={ToolCode} agente={AgentCode} caseIdentity={CaseIdentity}",
                toolCode, agentCode, caseIdentity);

            throw new UnauthorizedAccessException(
                $"[T5 D4] Tool '{toolCode}': identificadores de afiliado no coinciden con el contexto del Caso.");
        }

        // ── 6-SQL. REQ-019: BindingType 'Sql' — consulta read-only a BD ──
        //   Misma disciplina que el path HTTP: guard D4 ya validado arriba,
        //   se persiste ToolInvocation, y el resultado JSON vuelve al modelo.
        //   La consulta viene del catálogo (OPAITool.BindingConfig, autoría admin);
        //   el modelo solo aporta VALORES de parámetros (parametrizados) → sin inyección.
        if (esSql)
        {
            var startSql   = DateTime.UtcNow;
            var reqJsonSql = JsonSerializer.Serialize(toolInput);
            string respJsonSql;
            bool sqlError;

            try
            {
                respJsonSql = await ExecuteSqlToolAsync(binding, toolInput, identidad, ct).ConfigureAwait(false);
                sqlError = false;
            }
            catch (Exception ex)
            {
                respJsonSql = JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
                sqlError = true;
                _logger.LogWarning(ex, "[T5 Sql] Error ejecutando tool SQL '{ToolCode}'.", toolCode);
            }

            await PersistInvocationAsync(
                executionId, toolCode, reqJsonSql, respJsonSql, sqlError, startSql, ct)
                .ConfigureAwait(false);

            if (sqlError)
                throw new InvalidOperationException(
                    $"[T5 Sql] La tool '{toolCode}' respondió con error. Detalle: {respJsonSql}");

            return respJsonSql;
        }

        // ── 6-Zendesk. La atencion del ticket, contada al afiliado ───────
        //
        // No devuelve el ticket entero: eso trae notas internas, correos del
        // personal y campos de gestion que no son del afiliado y que ademas
        // ahogan al modelo. Se devuelve el estado, las fechas y SOLO los
        // comentarios PUBLICOS, que es lo que la persona ya podria leer en su
        // propia consulta.
        if (string.Equals(tool.BindingType, "Zendesk", StringComparison.OrdinalIgnoreCase))
        {
            var inicioZd = DateTime.UtcNow;
            var reqZd = JsonSerializer.Serialize(toolInput);
            string respZd;
            bool errZd;
            try
            {
                respZd = await ConsultarZendeskAsync(binding, toolInput, ct).ConfigureAwait(false);
                errZd = false;
            }
            catch (Exception ex)
            {
                respZd = JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
                errZd = true;
                _logger.LogWarning(ex, "[T5 Zendesk] Error en la tool '{ToolCode}'.", toolCode);
            }

            await PersistInvocationAsync(executionId, toolCode, reqZd, respZd, errZd, inicioZd, ct)
                .ConfigureAwait(false);

            if (errZd)
                throw new InvalidOperationException(
                    $"[T5 Zendesk] La tool '{toolCode}' respondio con error. Detalle: {respZd}");
            return respZd;
        }

        // ── 6. Resolver baseUrl desde configuración ──────────────────────
        var resolvedBaseUrl = ResolveBaseUrl(binding.BaseUrl);

        // ── 7. Obtener token Saludsa ─────────────────────────────────────
        IReadOnlyDictionary<string, string> authHeaders;
        try
        {
            authHeaders = await _tokenProvider.GetAuthHeadersAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[T5 B2] No se pudo obtener token OAuth2 Saludsa para tool '{ToolCode}'.", toolCode);
            throw;
        }

        // ── 8. Armar y ejecutar el request HTTP ──────────────────────────
        var startDate   = DateTime.UtcNow;
        var requestJson = JsonSerializer.Serialize(toolInput);
        string responseJson;
        bool isError;

        try
        {
            responseJson = await CallApiAsync(
                binding, resolvedBaseUrl, authHeaders, toolInput, ct)
                .ConfigureAwait(false);
            isError = false;
        }
        catch (Exception ex)
        {
            responseJson = JsonSerializer.Serialize(new
            {
                error   = ex.GetType().Name,
                message = ex.Message
            });
            isError = true;
            _logger.LogWarning(ex, "[T5] Error invocando tool '{ToolCode}'.", toolCode);
        }

        // ── 8-bis. Recortar la respuesta a lo que esta tool sirve ────────
        //
        // ObtenerDetalleSobre trae cabecera, estados, liquidacion y ficheros en
        // base64 -1,69 MB en un sobre real- en UNA sola respuesta. Cuatro tools
        // pequenas sobre el mismo endpoint, cada una devolviendo lo suyo, le
        // ahorran al modelo lo que no pidio.
        //
        // Se recorta DESPUES de registrar nada y ANTES de devolver, para que en
        // ToolInvocation quede lo que se envio al modelo y no otra cosa: si el
        // registro y la respuesta difieren, diagnosticar se vuelve adivinar.
        if (!isError && (binding.Pick.Count > 0 || binding.Omit.Count > 0))
            responseJson = RecortarDatos(responseJson, binding.Pick, binding.Omit);

        // Y una trampa de este gateway: contesta HTTP 200 con Estado "Error"
        // dentro del cuerpo -la ventana de mantenimiento de reembolsos, por
        // ejemplo-. Sin esto la tool "no falla", asi que su alternativa nunca se
        // intenta y al afiliado le llega un cuerpo de error crudo.
        if (!isError && EsErrorEnElCuerpo(responseJson))
        {
            isError = true;
            _logger.LogWarning("[T5] '{ToolCode}': HTTP correcto pero Estado=Error en el cuerpo.",
                               toolCode);
        }

        // ── 9. Persistir ToolInvocation ──────────────────────────────────
        await PersistInvocationAsync(
            executionId, toolCode,
            requestJson, responseJson, isError,
            startDate, ct)
            .ConfigureAwait(false);

        if (isError)
            throw new InvalidOperationException(
                $"[T5] La tool '{toolCode}' respondió con error. Detalle: {responseJson}");

        // Solo se memoriza lo que salio bien.
        if (llaveMemo != null)
        {
            using var entrada = _memo.CreateEntry(llaveMemo);
            entrada.Value = responseJson;
            entrada.AbsoluteExpirationRelativeToNow = VidaDelMemo;
            entrada.Size = 1;
        }

        return responseJson;
    }


    /// <summary>
    /// Deja en <c>Datos</c> solo los campos pedidos y quita los sobrantes. Lo
    /// demas del sobre -Estado, Mensajes- se conserva: el modelo los necesita
    /// para saber si la respuesta vale.
    /// </summary>
    private static string RecortarDatos(string json, List<string> pick, List<string> omit)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return json;

            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!prop.NameEquals("Datos") || prop.Value.ValueKind != JsonValueKind.Object)
                    {
                        prop.WriteTo(w);
                        continue;
                    }
                    w.WritePropertyName("Datos");
                    w.WriteStartObject();
                    foreach (var campo in prop.Value.EnumerateObject())
                    {
                        if (pick.Count > 0 &&
                            !pick.Contains(campo.Name, StringComparer.OrdinalIgnoreCase)) continue;
                        if (omit.Contains(campo.Name, StringComparer.OrdinalIgnoreCase)) continue;
                        campo.WriteTo(w);
                    }
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            // Un recorte que falla NO puede tumbar la respuesta: se devuelve
            // entera, que es peor pero no es mentira.
            return json;
        }
    }

    /// <summary>
    /// El gateway responde HTTP 200 con <c>Estado: "Error"</c> en el cuerpo.
    /// Sin mirar dentro, una tool caida parece sana.
    /// </summary>
    private static bool EsErrorEnElCuerpo(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("Estado", out var e)
                && e.ValueKind == JsonValueKind.String
                && string.Equals(e.GetString(), "Error", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// Lee un ticket de Zendesk y lo resume PARA EL AFILIADO.
    ///
    /// Devuelve el estado, las fechas y solo los comentarios PUBLICOS. El ticket
    /// entero trae notas internas, correos del personal y campos de gestion:
    /// nada de eso es del afiliado, y ademas ahoga al modelo.
    ///
    /// El token y la raiz salen de la tabla de parametros, emparejados por
    /// sufijo. Y ojo con la instancia: los tickets de reembolso viven en
    /// servicioexperience1562940791; preguntar en la otra devuelve 404, que
    /// parece "no existe" y no lo es.
    /// </summary>
    private async Task<string> ConsultarZendeskAsync(
        ToolBindingConfig binding,
        IReadOnlyDictionary<string, object?> toolInput,
        CancellationToken ct)
    {
        var parametros = _parametrosZendesk
            ?? throw new InvalidOperationException(
                "[T5 Zendesk] ParametrosZendesk no esta registrado en el contenedor.");

        var sufijo = string.IsNullOrWhiteSpace(binding.Connection)
            ? app_tramites.Services.Zendesk.ParametrosZendesk.Reembolso
            : binding.Connection!;
        var (token, raiz) = await parametros.ObtenerAsync(sufijo, ct).ConfigureAwait(false);

        if (!toolInput.TryGetValue("ticket", out var crudo) || crudo == null)
            throw new InvalidOperationException(
                "[T5 Zendesk] Falta 'ticket'. Sale de consultar_ticket_sobre.");
        if (!int.TryParse(crudo.ToString(), out var ticket) || ticket <= 0)
            throw new InvalidOperationException($"[T5 Zendesk] 'ticket' no es un numero: {crudo}");

        using var cli = _httpClientFactory.CreateClient();
        cli.Timeout = TimeSpan.FromSeconds(45);
        cli.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + token);
        cli.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

        var rt = await cli.GetAsync($"{raiz}/api/v2/tickets/{ticket}.json", ct).ConfigureAwait(false);
        if (!rt.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"[T5 Zendesk] El ticket {ticket} respondio {(int)rt.StatusCode}. "
                + (rt.StatusCode == System.Net.HttpStatusCode.NotFound
                   ? "404 puede significar que el ticket esta en la OTRA instancia de Zendesk, no que no exista."
                   : string.Empty));

        using var doc = JsonDocument.Parse(await rt.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var t = doc.RootElement.GetProperty("ticket");
        string? Campo(string n) => t.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String
                                   ? v.GetString() : null;

        // Los comentarios, solo los publicos y solo los ultimos: un ticket
        // largo tiene decenas y al afiliado le importa el final.
        var publicos = new List<object>();
        try
        {
            var rc = await cli.GetAsync($"{raiz}/api/v2/tickets/{ticket}/comments.json", ct)
                              .ConfigureAwait(false);
            if (rc.IsSuccessStatusCode)
            {
                using var dc = JsonDocument.Parse(
                    await rc.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                if (dc.RootElement.TryGetProperty("comments", out var cs)
                    && cs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var com in cs.EnumerateArray())
                    {
                        if (!com.TryGetProperty("public", out var pub) || !pub.GetBoolean()) continue;
                        var txt = com.TryGetProperty("plain_body", out var b) ? b.GetString() : null;
                        publicos.Add(new
                        {
                            cuando = com.TryGetProperty("created_at", out var f) ? f.GetString() : null,
                            texto = txt != null && txt.Length > 600 ? txt[..600] : txt
                        });
                    }
                    if (publicos.Count > 4) publicos = publicos.Skip(publicos.Count - 4).ToList();
                }
            }
        }
        catch (Exception ex)
        {
            // Que fallen los comentarios NO puede tumbar la respuesta: el estado
            // del ticket ya contesta la mitad de la pregunta.
            _logger.LogWarning(ex, "[T5 Zendesk] No se pudieron leer los comentarios del {Ticket}.", ticket);
        }

        return JsonSerializer.Serialize(new
        {
            ticket,
            asunto = Campo("subject"),
            estado = Campo("status"),
            QueSignificaElEstado = Campo("status") switch
            {
                "new"     => "Recibido, todavia sin asignar a nadie.",
                "open"    => "Abierto: alguien lo esta atendiendo.",
                "pending" => "Pendiente: se espera algo del afiliado. Mira los comentarios: suele decir que falta.",
                "hold"    => "En espera de un tercero, no del afiliado.",
                "solved"  => "Resuelto. Si el afiliado no esta de acuerdo, aun se puede reabrir.",
                "closed"  => "Cerrado y ya no se puede reabrir: haria falta un caso nuevo.",
                _         => null
            },
            creado = Campo("created_at"),
            ultimoMovimiento = Campo("updated_at"),
            comentariosPublicos = publicos,
            NotaParaElAgente = "Los comentarios internos NO se devuelven a proposito. "
                             + "Explica el estado con tus palabras, no pegues el texto crudo."
        });
    }

    // ── HTTP ─────────────────────────────────────────────────────────────

    private async Task<string> CallApiAsync(
        ToolBindingConfig binding,
        string resolvedBaseUrl,
        IReadOnlyDictionary<string, string> authHeaders,
        IReadOnlyDictionary<string, object?> toolInput,
        CancellationToken ct)
    {
        var httpClient = _httpClientFactory.CreateClient("SaludsaInternalApi");

        using var requestMessage = BuildRequest(binding, resolvedBaseUrl, toolInput);

        // Inyectar cabeceras de autenticación Saludsa
        foreach (var (name, value) in authHeaders)
            requestMessage.Headers.TryAddWithoutValidation(name, value);

        using var response = await httpClient.SendAsync(requestMessage, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"[T5] API respondió {(int)response.StatusCode}: {body}",
                null, response.StatusCode);

        return body;
    }

    private static HttpRequestMessage BuildRequest(
        ToolBindingConfig binding,
        string resolvedBaseUrl,
        IReadOnlyDictionary<string, object?> toolInput)
    {
        var method = binding.Method.ToUpperInvariant() == "POST"
            ? HttpMethod.Post
            : HttpMethod.Get;

        var uriBuilder = new UriBuilder(resolvedBaseUrl.TrimEnd('/') + binding.Path);

        // paramMap va a la query string SIEMPRE, no solo en GET.
        //
        // Estaba limitado a GET, y con eso una tool POST cuyos parametros viajan
        // por la URL no podia funcionar: la peticion salia sin ellos. Medido con
        // cargar_factura_desde_sri (POST /api/Sri?claveAcceso=...), que devolvia
        // 404 porque el servicio recibia la llamada sin clave.
        //
        // No es un caso raro: en el repositorio de comprobantes el POST que trae
        // la factura del SRI y la guarda lleva su unico parametro en la query.
        // El body sigue armandose aparte con bodyMap; las dos cosas conviven.
        if (binding.ParamMap.Count > 0)
        {
            // Armar query string con los campos de paramMap
            var queryParams = new List<string>();
            foreach (var (inputKey, queryKey) in binding.ParamMap)
            {
                if (toolInput.TryGetValue(inputKey, out var val) && val != null)
                {
                    var texto = NormalizarParametroSaludsa(queryKey, val.ToString()!);
                    queryParams.Add($"{Uri.EscapeDataString(queryKey)}={Uri.EscapeDataString(texto)}");
                }
            }
            if (queryParams.Count > 0)
                uriBuilder.Query = string.Join("&", queryParams);
        }

        var request = new HttpRequestMessage(method, uriBuilder.Uri);

        if (method == HttpMethod.Post)
        {
            // Construir body JSON según bodyMap (soporta dot notation para objetos anidados)
            var bodyNode = new JsonObject();
            if (binding.BodyMap.Count > 0)
            {
                foreach (var fieldPath in binding.BodyMap)
                {
                    var inputKey = fieldPath.Contains('.') ? fieldPath.Split('.')[^1] : fieldPath;
                    if (toolInput.TryGetValue(inputKey, out var val) && val != null)
                        SetNestedJsonValue(bodyNode, fieldPath, val);
                }
            }
            else
            {
                // Sin bodyMap: enviar todos los campos del input como body plano
                foreach (var (k, v) in toolInput)
                    if (v != null)
                        bodyNode[k] = JsonValue.Create(v.ToString());
            }

            request.Content = new StringContent(
                bodyNode.ToJsonString(),
                Encoding.UTF8,
                "application/json");
        }

        return request;
    }

    /// <summary>
    /// Establece un valor en un <see cref="JsonObject"/> usando notación dot
    /// (ej: <c>filter.region</c> → <c>{ "filter": { "region": value } }</c>).
    /// </summary>
    /// <summary>
    /// Corrige los dos formatos que las APIs de Saludsa exigen y que el modelo
    /// no puede adivinar. Es una red de seguridad: aunque el InputSchema ya lo
    /// declare, el modelo puede escribir "CEDULA" y la API responderia
    /// "No existen datos de: contratos" sin decir que el problema es el formato.
    ///
    ///  · <c>tipoDocumento</c>: la API solo acepta <c>C</c> (cédula) o
    ///    <c>P</c> (pasaporte). "CEDULA", "CED", "CI", "IDENTIFICACION" → C;
    ///    "PASAPORTE", "PASS" → P.
    ///  · <c>numeroDocumento</c>: la cédula debe ir CON el cero inicial, a 10
    ///    dígitos. OJO: es lo contrario de las tablas de Saludsa, que la
    ///    guardan sin el cero — no "arreglar" esto quitándolo.
    /// </summary>
    private static string NormalizarParametroSaludsa(string queryKey, string valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return valor;

        if (queryKey.Equals("tipoDocumento", StringComparison.OrdinalIgnoreCase))
        {
            var v = valor.Trim().ToUpperInvariant();
            if (v is "C" or "P") return v;
            if (v.StartsWith("CED") || v is "CI" or "IDENTIFICACION" or "DNI") return "C";
            if (v.StartsWith("PAS")) return "P";
            return valor.Trim();
        }

        if (queryKey.Equals("numeroDocumento", StringComparison.OrdinalIgnoreCase))
        {
            var soloDigitos = new string(valor.Where(char.IsDigit).ToArray());
            // 9 dígitos = cédula a la que se le comió el cero inicial
            if (soloDigitos.Length == 9) return soloDigitos.PadLeft(10, '0');
            return soloDigitos.Length > 0 ? soloDigitos : valor.Trim();
        }

        return valor;
    }

    private static void SetNestedJsonValue(JsonObject root, string dotPath, object value)
    {
        var parts = dotPath.Split('.');
        var current = root;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            var key = parts[i];
            if (current[key] is not JsonObject nested)
            {
                nested = new JsonObject();
                current[key] = nested;
            }
            current = nested;
        }

        current[parts[^1]] = JsonValue.Create(value.ToString());
    }

    // ── SQL (BindingType "Sql") ──────────────────────────────────────────

    /// <summary>
    /// Ejecuta la consulta SELECT read-only de una tool SQL contra la BD de negocio.
    /// Salvaguardas: solo SELECT (una sentencia, sin DML/DDL por palabra completa),
    /// parámetros SIEMPRE parametrizados (el modelo no toca el SQL), tope de filas
    /// y truncado del JSON de salida.
    /// </summary>
    private async Task<string> ExecuteSqlToolAsync(
        ToolBindingConfig binding,
        IReadOnlyDictionary<string, object?> toolInput,
        IdentidadCaso? identidad,
        CancellationToken ct)
    {
        var query = binding.Query;
        if (string.IsNullOrWhiteSpace(query))
            throw new InvalidOperationException("[T5 Sql] La tool no tiene 'query' en su BindingConfig.");

        // Guard 1: solo lectura, una sola sentencia.
        //
        // Se admite tambien WITH, porque una consulta con CTE sigue siendo una
        // lectura y hay tools que la necesitan: codigo_liquidacion_y_cobertura
        // resuelve primero el procedimiento -o cae al generico- y despues cruza
        // el plan, y eso sin CTE se convierte en dos idas a la base.
        //
        // Admitir WITH no abre nada, porque los guardias 2 y 3 siguen enteros:
        // `WITH x AS (SELECT 1) DELETE FROM t` cae en el guardia 3 por la palabra
        // DELETE, igual que caeria sin el WITH delante. Lo que protege de verdad
        // es la lista de verbos y la sentencia unica, no la primera palabra.
        var trimmed = query.Trim();
        if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("WITH",   StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "[T5 Sql] Solo se permiten consultas de lectura (SELECT, o WITH ... SELECT).");
        if (trimmed.TrimEnd(';').Contains(';'))
            throw new InvalidOperationException("[T5 Sql] Solo se permite una sentencia por tool.");
        if (Regex.IsMatch(trimmed,
                @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|EXEC|EXECUTE|MERGE|TRUNCATE|GRANT|REVOKE|INTO)\b",
                RegexOptions.IgnoreCase))
            throw new InvalidOperationException("[T5 Sql] La consulta contiene palabras no permitidas (solo lectura).");

        // Conexión: nombre lógico de ConnectionStrings (default SaludConsultas).
        var connName = string.IsNullOrWhiteSpace(binding.Connection) ? "SaludConsultas" : binding.Connection;
        var connStr  = _config.GetConnectionString(connName);
        if (string.IsNullOrWhiteSpace(connStr))
            throw new InvalidOperationException(
                $"[T5 Sql] La cadena de conexión 'ConnectionStrings:{connName}' no está configurada.");

        var maxRows = binding.MaxRows > 0 ? Math.Min(binding.MaxRows, 200) : 50;

        // El destino, no la consulta: si la maquina no contesta, no contesta
        // para ninguna tool que vaya contra ella.
        var destino = "sql:" + connName;
        if (SigueCaido(destino))
            throw new InvalidOperationException(
                $"[T5 Sql] La base '{connName}' no respondio hace unos segundos y se dio por "
                + "no disponible; no se reintenta todavia para no bloquear el analisis. "
                + "Compruebe la conexion (VPN) y vuelva a lanzarlo.");

        await using var conn = new SqlConnection(connStr);
        try
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
        }
        catch (SqlException)
        {
            // Solo el FALLO DE CONEXION apunta el destino como caido. Un error
            // de la consulta en si (columna inexistente, timeout de comando) es
            // problema de esa tool, no del servidor, y no debe silenciar a las
            // demas.
            ApuntarCaido(destino);
            throw;
        }

        // Sesenta, no treinta. Medido desde web-nexus-test llamando a la consulta
        // real de consultar_autorizaciones:
        //
        //     1a llamada (en frio)  24.891 ms hasta la primera fila
        //     2a llamada             3.736 ms
        //     3a llamada             2.916 ms
        //
        // En caliente sobra con tres segundos, pero el chat la llama SIEMPRE en
        // frio -es una tool que se usa de vez en cuando- y ahi 30 no dan. Al
        // afiliado le salia "no se pudo consultar ahora" por 5 segundos de
        // margen. Sesenta deja aire sin dejar la peticion colgada para siempre.
        await using var cmd = new SqlCommand(query, conn) { CommandTimeout = 60 };

        // ── Parametrización: por cada @param del SQL, el valor del input ─────────
        //
        // El tipo NO es un detalle. Todos los parámetros iban como NVARCHAR, y la
        // mitad de las columnas contra las que se comparan son VARCHAR. Cuando se
        // comparan un varchar y un nvarchar, SQL Server convierte LA COLUMNA
        // -nvarchar tiene más precedencia-, el predicado deja de poder usar el
        // índice y la consulta se come la tabla entera.
        //
        // Medido sobre factura_ya_pagada_bd, con su índice IdxNroFactNumConvenio
        // delante y las mismas 9 filas de resultado:
        //
        //     NroFacturaPrestador varchar(30) = @p nvarchar ....... 47.034 ms
        //     NroFacturaPrestador varchar(30) = @p varchar ........      0 ms
        //
        // Cuarenta y siete segundos contra un CommandTimeout de 30: la herramienta
        // no iba lenta, MORÍA, y al afiliado le salía "No se pudo consultar ahora".
        //
        // La regla: si el valor es ASCII puro va como VARCHAR, y entonces sirve
        // para los dos casos —contra columna varchar hay seek, y contra columna
        // nvarchar es el PARÁMETRO el que se promueve, así que la columna y su
        // índice quedan intactos—. Si trae acentos o ñ va como NVARCHAR, porque
        // varchar no los guardaría bien; esos son nombres, y las columnas de
        // nombres ya son nvarchar.
        var inputCi = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in toolInput) inputCi[k] = v;

        foreach (Match m in Regex.Matches(query, @"@([A-Za-z_][A-Za-z0-9_]*)"))
        {
            var pName = m.Groups[1].Value;
            if (cmd.Parameters.Contains("@" + pName)) continue;

            var texto = inputCi.TryGetValue(pName, out var raw) && raw != null
                ? raw.ToString()
                : null;

            // @personas lo pone el SISTEMA, no el modelo. Estrecha la busqueda
            // en dbo.Autorizacion (4.555.053 filas) usando
            // IdxPersonContratoRegionProducto, que empieza por PersonaNumero;
            // ContratoNumero no encabeza ningun indice.
            //
            // Ayuda, pero MODESTAMENTE: medido con las dos consultas calientes,
            // 187 ms filtrando solo por contrato contra 128 ms con la persona
            // delante. Es 1,5x, no el orden de magnitud que supuse al escribirlo.
            // Lo que de verdad mataba la tool era el arranque en frio -25 s- y
            // eso lo cubre el CommandTimeout, no este filtro.
            //
            // Se rellena desde la identidad del caso -no desde el input- por
            // dos razones: el modelo se olvidaria la mitad de las veces, y esos
            // numeros ya estan validados por el guardian anti-IDOR, asi que no
            // ensanchan lo que el afiliado puede ver. Van TODAS las personas del
            // contrato: en uno familiar el titular ve a los suyos, como hasta
            // ahora.
            if (texto == null &&
                string.Equals(pName, "personas", StringComparison.OrdinalIgnoreCase) &&
                identidad is { Personas.Count: > 0 })
                texto = string.Join(",", identidad.Personas);

            var tipo = EsAsciiPuro(texto)
                ? System.Data.SqlDbType.VarChar
                : System.Data.SqlDbType.NVarChar;

            cmd.Parameters.Add(new SqlParameter("@" + pName, tipo, 400)
            {
                Value = (object?)texto ?? DBNull.Value
            });
        }

        // Ejecutar y materializar filas como diccionarios (JSON-friendly).
        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (rows.Count < maxRows && await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var val = reader.IsDBNull(i) ? null : reader.GetValue(i);
                row[reader.GetName(i)] = val switch
                {
                    null            => null,
                    DateTime dt     => dt.ToString("yyyy-MM-dd HH:mm"),
                    decimal or double or float or int or long or short or byte or bool => val,
                    _               => val.ToString()
                };
            }
            rows.Add(row);
        }

        var json = JsonSerializer.Serialize(new { rowCount = rows.Count, rows });

        // Truncado defensivo para no reventar el contexto del modelo.
        const int maxLen = 20000;
        if (json.Length > maxLen)
            json = json[..maxLen] + "\"…truncado…\"}";

        return json;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Resuelve los placeholders de baseUrl (<c>{api-contrato}</c>, etc.)
    /// con los valores de la configuración (sección <c>Saludsa:BaseUrls</c>).
    /// </summary>
    private string ResolveBaseUrl(string baseUrlTemplate)
    {
        // Mapeo: placeholder → clave de configuración
        // B1/T0a: las reglas de egress deben existir para cada URL en producción.
        var placeholders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{api-contrato}"]  = "Saludsa:BaseUrls:ApiContrato",
            ["{api-armonix}"]   = "Saludsa:BaseUrls:ApiArmonix",
            ["{api-prestador}"] = "Saludsa:BaseUrls:ApiPrestador",
            // REQ-019: validación de procedimientos + PVP (api-reembolso-automatico / CorrelacionController)
            ["{api-reembolso-automatico}"] = "Saludsa:BaseUrls:ApiReembolsoAutomatico",
            // REQ-021: el repositorio de comprobantes electrónicos. Es la fuente
            // de verdad de la factura —la trae del SRI y la guarda—, frente al
            // OCR, que es una lectura de una foto.
            ["{api-repositorio}"] = "Saludsa:BaseUrls:ApiRepositorio",
            // REQ-038: el servicio de liquidaciones. Es quien sabe de SOBRES:
            // ObtenerSobre da los del afiliado y ObtenerDetalleSobre trae en
            // una sola respuesta cabecera, estados, liquidacion y ficheros.
            // Se parte en varias tools con Pick/Omit para no volcarle al
            // modelo 1,69 MB de base64 cuando solo preguntaron "en que va".
            ["{api-liquidacion}"] = "Saludsa:BaseUrls:ApiLiquidacion"
        };

        foreach (var (placeholder, configKey) in placeholders)
        {
            if (!baseUrlTemplate.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
                continue;

            var resolved = _config[configKey];
            if (string.IsNullOrWhiteSpace(resolved))
                // B1/T0a: baseUrl no configurada — falla en runtime con mensaje claro
                throw new InvalidOperationException(
                    $"[T5 B1] BaseUrl '{placeholder}' no está configurada. " +
                    $"Configure '{configKey}' en appsettings / Key Vault (B1/T0a).");

            return resolved.TrimEnd('/');
        }

        // Si no era placeholder, usar tal cual (URL literal en la config)
        return baseUrlTemplate.TrimEnd('/');
    }


    /// <summary>
    /// Los identificadores que este caso tiene derecho a consultar.
    ///
    /// Sale de la nota de contexto del sobre, que es donde el portal y los
    /// importadores dejan quién es el afiliado, qué contrato es y —desde
    /// REQ-020c— a qué beneficiario va dirigido el reembolso. Se añaden todos
    /// porque todos son sujetos legítimos del mismo caso: pedir las
    /// preexistencias de un hijo por su cédula es correcto, no un IDOR.
    ///
    /// Si no se puede leer el contexto se cae a la cédula suelta que llegó por
    /// parámetro, que es el comportamiento anterior.
    /// </summary>
    private async Task<IdentidadCaso> ResolverIdentidadCasoAsync(
        long executionId, string? caseIdentity, CancellationToken ct)
    {
        var identidad = IdentidadCaso.DeCedula(caseIdentity);

        try
        {
            var caseCode = await _db.StepExecution.AsNoTracking()
                .Where(se => se.ExecutionId == executionId)
                .Select(se => (Guid?)se.CaseCode)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (caseCode is null || caseCode == Guid.Empty)
                return identidad;

            var contexto = await _db.Note.AsNoTracking()
                .Where(n => n.CaseCode == caseCode
                            && n.Title == app_tramites.Services.Ai.OcrPromptHelper.ContextoSobreNoteTitle)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => n.Detail)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(contexto))
                return identidad;

            using var doc = JsonDocument.Parse(contexto);
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object)
                return identidad;

            string? T(JsonElement e, string prop) =>
                e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v)
                    ? (v.ValueKind == JsonValueKind.String ? v.GetString()
                       : v.ValueKind == JsonValueKind.Number ? v.ToString() : null)
                    : null;

            // El contrato se registra con su llave COMPLETA: el número solo no
            // identifica nada (17% se repiten entre región y producto).
            identidad.ConCedula(T(raiz, "cedula"))
                     .ConContrato(T(raiz, "codigoRegion"),
                                  T(raiz, "codigoProducto"),
                                  T(raiz, "numeroContrato"))
                     .ConPersona(T(raiz, "numeroPersonaPaciente"));

            // El beneficiario al que va dirigido el reembolso: su cédula y su
            // número de persona son legítimos para este caso.
            if (raiz.TryGetProperty("beneficiario", out var b) && b.ValueKind == JsonValueKind.Object)
            {
                identidad.ConCedula(T(b, "cedula"))
                         .ConPersona(T(b, "numeroPersona"));
            }
        }
        catch (JsonException)
        {
            // Un contexto ilegible no debe abrir ni cerrar el guardián de más:
            // se queda con lo que llegó por parámetro.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[T5 D4] No se pudo resolver la identidad del caso para la ejecución {ExecutionId}; " +
                "se usa solo la cédula recibida.", executionId);
        }

        return identidad;
    }

    private async Task PersistInvocationAsync(
        long executionId,
        string toolCode,
        string? requestJson,
        string? responseJson,
        bool isError,
        DateTime start,
        CancellationToken ct)
    {
        try
        {
            var invocation = new ToolInvocation
            {
                ExecutionId  = executionId,
                ToolCode     = toolCode,
                RequestJson  = requestJson,
                ResponseJson = responseJson,
                IsError      = isError,
                StartDate    = start,
                EndDate      = DateTime.UtcNow
            };
            _db.ToolInvocation.Add(invocation);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // No propagar errores de auditoría — registrar y continuar
            _logger.LogWarning(ex,
                "[T5] No se pudo persistir ToolInvocation para tool '{ToolCode}' / execution {ExecutionId}.",
                toolCode, executionId);
        }
    }

    /// <summary>
    /// ¿El valor cabe entero en VARCHAR sin perder nada? Un código, un RUC, una
    /// clave de acceso o una fecha sí; «Muñoz» o «Hipófisis» no.
    ///
    /// Un null cuenta como ASCII: va a ser DBNull y el tipo da igual, pero VarChar
    /// mantiene el predicado utilizable por el índice cuando la consulta hace
    /// `(@p IS NULL OR col = @p)`, que es como están escritos los opcionales.
    /// </summary>
    private static bool EsAsciiPuro(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return true;
        foreach (var c in texto)
            if (c > 127) return false;
        return true;
    }
}
