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

    public InternalApiToolExecutor(
        OCRDbContext db,
        IHttpClientFactory httpClientFactory,
        ISaludsaTokenProvider tokenProvider,
        IToolAuthorizationGuard authGuard,
        IConfiguration config,
        ILogger<InternalApiToolExecutor> logger)
    {
        _db              = db              ?? throw new ArgumentNullException(nameof(db));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _tokenProvider   = tokenProvider   ?? throw new ArgumentNullException(nameof(tokenProvider));
        _authGuard       = authGuard       ?? throw new ArgumentNullException(nameof(authGuard));
        _config          = config          ?? throw new ArgumentNullException(nameof(config));
        _logger          = logger          ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(
        string toolCode,
        string agentCode,
        IReadOnlyDictionary<string, object?> toolInput,
        long executionId,
        string? caseIdentity = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentCode);

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
            && !string.Equals(tool.BindingType, "Armonix", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"[T5] BindingType '{tool.BindingType}' no soportado por InternalApiToolExecutor. " +
                "Tipos soportados: InternalApi, Armonix, Sql.");
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
                respJsonSql = await ExecuteSqlToolAsync(binding, toolInput, ct).ConfigureAwait(false);
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

        // ── 9. Persistir ToolInvocation ──────────────────────────────────
        await PersistInvocationAsync(
            executionId, toolCode,
            requestJson, responseJson, isError,
            startDate, ct)
            .ConfigureAwait(false);

        if (isError)
            throw new InvalidOperationException(
                $"[T5] La tool '{toolCode}' respondió con error. Detalle: {responseJson}");

        return responseJson;
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

        await using var cmd = new SqlCommand(query, conn) { CommandTimeout = 30 };

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
            ["{api-repositorio}"] = "Saludsa:BaseUrls:ApiRepositorio"
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
