using Neo4j.Driver;
using System.Security.Cryptography;
using System.Text;

namespace app_tramites.Services.Graph;

// ============================================================
// REQ-019 T19 — Implementación de IGraphService sobre Neo4j AuraDB.
// Bloqueo B6: no conecta en startup; falla en runtime si las
// credenciales (Neo4j:Uri/User/Password) no están configuradas.
// ============================================================

/// <summary>
/// Implementación de <see cref="IGraphService"/> sobre Neo4j AuraDB Free.
/// </summary>
/// <remarks>
/// <para>
/// Las credenciales se leen de la configuración segura:
/// <list type="bullet">
///   <item><c>Neo4j:Uri</c>      — URI bolt+TLS, ej: <c>neo4j+s://xxxxxxxx.databases.neo4j.io</c>.</item>
///   <item><c>Neo4j:User</c>     — usuario de la instancia AuraDB.</item>
///   <item><c>Neo4j:Password</c> — contraseña (secreto Key Vault vía SecretRef, no en claro).</item>
/// </list>
/// </para>
/// <para>
/// La conexión se establece de forma perezosa al primer uso; no en startup.
/// Si alguna credencial falta, lanza <see cref="InvalidOperationException"/> con
/// un mensaje descriptivo del bloqueo B6.
/// </para>
/// </remarks>
public sealed class Neo4jGraphService : IGraphService, IAsyncDisposable
{
    private readonly IConfiguration _config;
    private readonly ILogger<Neo4jGraphService> _logger;
    private IDriver? _driver;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    // Allow-list de consultas Cypher predefinidas (read-only, parametrizadas).
    // NADA de Cypher libre: el modelo elige por nombre; los parámetros van
    // escapados vía el driver ($param — Neo4j los escapa automáticamente).
    private static readonly IReadOnlyDictionary<string, string> AllowedQueries =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sobres_por_afiliado_y_diagnostico"] =
                "MATCH (a:Afiliado {cedula: $cedula})<-[:DE]-(s:Sobre)-[:GENERA]->(:Caso)-[:CONTIENE]->(d:Documento)-[:MENCIONA]->(diag:Diagnostico {codigo: $codigoDiagnostico}) " +
                "RETURN s.numeroSobre AS numeroSobre, s.cuentaZendesk AS cuentaZendesk, s.ticketId AS ticketId " +
                "ORDER BY s.numeroSobre",

            ["documentos_por_caso"] =
                "MATCH (:Caso {caseCode: $caseCode})-[:CONTIENE]->(d:Documento) " +
                "RETURN d.dataFileId AS dataFileId, d.claudeFileId AS claudeFileId, d.fileUri AS fileUri, d.tipo AS tipo",

            ["documentos_sustentan_hallazgo"] =
                "MATCH (h:Hallazgo)-[:SUSTENTADO_POR]->(d:Documento)<-[:CONTIENE]-(:Caso {caseCode: $caseCode}) " +
                "WHERE h.texto CONTAINS $hallazgoTexto " +
                "RETURN h.texto AS hallazgo, h.origen AS origen, d.dataFileId AS dataFileId, d.fileUri AS fileUri",

            ["procedimientos_cubiertos_por_caso"] =
                "MATCH (:Caso {caseCode: $caseCode})-[:CONTIENE]->(d:Documento)-[:MENCIONA]->(p:Procedimiento)-[:CUBIERTO_POR]->(c:Cobertura) " +
                "RETURN p.codigo AS codigo, p.descripcion AS descripcion, c.convenio AS convenio, c.producto AS producto, c.plan AS plan",

            ["preexistencias_afiliado"] =
                "MATCH (a:Afiliado {cedula: $cedula})-[:TIENE]->(px:Preexistencia) " +
                "RETURN px.codigo AS codigo, px.descripcion AS descripcion",

            ["diagnosticos_por_caso"] =
                "MATCH (:Caso {caseCode: $caseCode})-[:CONTIENE]->(d:Documento)-[:MENCIONA]->(diag:Diagnostico) " +
                "RETURN DISTINCT diag.codigo AS codigo, diag.descripcion AS descripcion " +
                "ORDER BY diag.codigo"
        };

    /// <summary>
    /// Crea el servicio con sus dependencias.
    /// </summary>
    /// <param name="config">Configuración de la aplicación (sección <c>Neo4j</c>).</param>
    /// <param name="logger">Logger.</param>
    public Neo4jGraphService(IConfiguration config, ILogger<Neo4jGraphService> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── Bootstrap ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        var driver = await GetDriverAsync(ct).ConfigureAwait(false);
        await using var session = driver.AsyncSession();

        // Constraints de unicidad (CREATE CONSTRAINT IF NOT EXISTS — Aura / Neo4j 4.4+)
        var statements = new[]
        {
            "CREATE CONSTRAINT afiliado_cedula_unique IF NOT EXISTS FOR (a:Afiliado) REQUIRE a.cedula IS UNIQUE",
            "CREATE CONSTRAINT sobre_numero_unique IF NOT EXISTS FOR (s:Sobre) REQUIRE s.numeroSobre IS UNIQUE",
            "CREATE CONSTRAINT caso_code_unique IF NOT EXISTS FOR (c:Caso) REQUIRE c.caseCode IS UNIQUE",
            "CREATE CONSTRAINT documento_fileid_unique IF NOT EXISTS FOR (d:Documento) REQUIRE d.dataFileId IS UNIQUE",
            "CREATE INDEX diagnostico_codigo_idx IF NOT EXISTS FOR (d:Diagnostico) ON (d.codigo)",
            "CREATE INDEX procedimiento_codigo_idx IF NOT EXISTS FOR (p:Procedimiento) ON (p.codigo)",
            "CREATE INDEX preexistencia_codigo_idx IF NOT EXISTS FOR (px:Preexistencia) ON (px.codigo)"
        };

        foreach (var stmt in statements)
        {
            try
            {
                var cursor = await session.RunAsync(stmt).ConfigureAwait(false);
                await cursor.ConsumeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[T19] Error aplicando esquema Neo4j: {Statement}", stmt);
            }
        }

        _logger.LogInformation("[T19] Esquema Neo4j verificado/inicializado.");
    }

    // ── MERGE de nodos ────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task MergeAfiliadoAsync(string cedula, string? nombre, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cedula);

        const string cypher =
            "MERGE (a:Afiliado {cedula: $cedula}) " +
            "ON CREATE SET a.nombre = $nombre " +
            "ON MATCH  SET a.nombre = COALESCE($nombre, a.nombre)";

        await RunWriteAsync(cypher, Params("cedula", cedula, "nombre", nombre), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MergeSobreAsync(
        string numeroSobre,
        string? cuentaZendesk,
        string? ticketId,
        string? cedula,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(numeroSobre);

        var p = new Dictionary<string, object?>
        {
            ["numeroSobre"]    = numeroSobre,
            ["cuentaZendesk"]  = cuentaZendesk,
            ["ticketId"]       = ticketId,
            ["cedula"]         = cedula
        };

        var cypher = string.IsNullOrWhiteSpace(cedula)
            ? "MERGE (s:Sobre {numeroSobre: $numeroSobre}) " +
              "ON CREATE SET s.cuentaZendesk = $cuentaZendesk, s.ticketId = $ticketId " +
              "ON MATCH  SET s.cuentaZendesk = COALESCE($cuentaZendesk, s.cuentaZendesk), " +
              "              s.ticketId      = COALESCE($ticketId,      s.ticketId)"
            : "MERGE (s:Sobre {numeroSobre: $numeroSobre}) " +
              "ON CREATE SET s.cuentaZendesk = $cuentaZendesk, s.ticketId = $ticketId " +
              "ON MATCH  SET s.cuentaZendesk = COALESCE($cuentaZendesk, s.cuentaZendesk), " +
              "              s.ticketId      = COALESCE($ticketId,      s.ticketId) " +
              "WITH s " +
              "MERGE (a:Afiliado {cedula: $cedula}) " +
              "MERGE (s)-[:DE]->(a)";

        await RunWriteAsync(cypher, p, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MergeCasoAsync(
        string caseCode,
        string? numeroSobre,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseCode);

        var p = new Dictionary<string, object?>
        {
            ["caseCode"]    = caseCode,
            ["numeroSobre"] = numeroSobre
        };

        var cypher = string.IsNullOrWhiteSpace(numeroSobre)
            ? "MERGE (:Caso {caseCode: $caseCode})"
            : "MERGE (c:Caso {caseCode: $caseCode}) " +
              "WITH c " +
              "MERGE (s:Sobre {numeroSobre: $numeroSobre}) " +
              "MERGE (s)-[:GENERA]->(c)";

        await RunWriteAsync(cypher, p, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MergeDocumentoAsync(
        string dataFileId,
        string? claudeFileId,
        string? fileUri,
        string? tipo,
        string caseCode,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(caseCode);

        const string cypher =
            "MERGE (d:Documento {dataFileId: $dataFileId}) " +
            "ON CREATE SET d.claudeFileId = $claudeFileId, d.fileUri = $fileUri, d.tipo = $tipo " +
            "ON MATCH  SET d.claudeFileId = COALESCE($claudeFileId, d.claudeFileId), " +
            "              d.fileUri      = COALESCE($fileUri,      d.fileUri), " +
            "              d.tipo         = COALESCE($tipo,         d.tipo) " +
            "WITH d " +
            "MERGE (c:Caso {caseCode: $caseCode}) " +
            "MERGE (c)-[:CONTIENE]->(d)";

        var p = new Dictionary<string, object?>
        {
            ["dataFileId"]   = dataFileId,
            ["claudeFileId"] = claudeFileId,
            ["fileUri"]      = fileUri,
            ["tipo"]         = tipo,
            ["caseCode"]     = caseCode
        };

        await RunWriteAsync(cypher, p, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MergeDiagnosticoAsync(
        string codigo,
        string? descripcion,
        string dataFileId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFileId);

        const string cypher =
            "MERGE (diag:Diagnostico {codigo: $codigo}) " +
            "ON CREATE SET diag.descripcion = $descripcion " +
            "ON MATCH  SET diag.descripcion = COALESCE($descripcion, diag.descripcion) " +
            "WITH diag " +
            "MERGE (d:Documento {dataFileId: $dataFileId}) " +
            "MERGE (d)-[:MENCIONA]->(diag)";

        await RunWriteAsync(cypher, Params("codigo", codigo, "descripcion", descripcion, "dataFileId", dataFileId), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MergeProcedimientoAsync(
        string codigo,
        string? descripcion,
        string dataFileId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFileId);

        const string cypher =
            "MERGE (p:Procedimiento {codigo: $codigo}) " +
            "ON CREATE SET p.descripcion = $descripcion " +
            "ON MATCH  SET p.descripcion = COALESCE($descripcion, p.descripcion) " +
            "WITH p " +
            "MERGE (d:Documento {dataFileId: $dataFileId}) " +
            "MERGE (d)-[:MENCIONA]->(p)";

        await RunWriteAsync(cypher, Params("codigo", codigo, "descripcion", descripcion, "dataFileId", dataFileId), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MergeCoberturaAsync(
        string convenio,
        string? producto,
        string? plan,
        string? codigoProcedimiento,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(convenio);

        var p = new Dictionary<string, object?>
        {
            ["convenio"]              = convenio,
            ["producto"]              = producto,
            ["plan"]                  = plan,
            ["codigoProcedimiento"]   = codigoProcedimiento
        };

        var cypher = string.IsNullOrWhiteSpace(codigoProcedimiento)
            ? "MERGE (:Cobertura {convenio: $convenio, producto: $producto, plan: $plan})"
            : "MERGE (cob:Cobertura {convenio: $convenio, producto: $producto, plan: $plan}) " +
              "WITH cob " +
              "MERGE (proc:Procedimiento {codigo: $codigoProcedimiento}) " +
              "MERGE (proc)-[:CUBIERTO_POR]->(cob)";

        await RunWriteAsync(cypher, p, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MergePreexistenciaAsync(
        string codigo,
        string? descripcion,
        string? dataFileId,
        string? cedula,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo);

        // Ejecutar en varios pasos simples para evitar FOREACH condicional
        // que puede ser complejo de parametrizar en Neo4j 5.x sin extensiones.
        var pBase = Params("codigo", codigo, "descripcion", descripcion);
        const string cypherBase =
            "MERGE (px:Preexistencia {codigo: $codigo}) " +
            "ON CREATE SET px.descripcion = $descripcion " +
            "ON MATCH  SET px.descripcion = COALESCE($descripcion, px.descripcion)";
        await RunWriteAsync(cypherBase, pBase, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(dataFileId))
        {
            const string cypherDoc =
                "MATCH (px:Preexistencia {codigo: $codigo}) " +
                "MERGE (d:Documento {dataFileId: $dataFileId}) " +
                "MERGE (d)-[:MENCIONA]->(px)";
            await RunWriteAsync(cypherDoc, Params("codigo", codigo, "dataFileId", dataFileId), ct).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(cedula))
        {
            const string cypherAfil =
                "MATCH (px:Preexistencia {codigo: $codigo}) " +
                "MERGE (a:Afiliado {cedula: $cedula}) " +
                "MERGE (a)-[:TIENE]->(px)";
            await RunWriteAsync(cypherAfil, Params("codigo", codigo, "cedula", cedula), ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task MergeHallazgoAsync(
        string texto,
        string? origen,
        string? dataFileId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texto);

        // Clave del hallazgo: hash SHA-256 truncado del texto para evitar duplicados exactos
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(texto)))[..16];

        var pBase = new Dictionary<string, object?> { ["textoHash"] = hash, ["texto"] = texto, ["origen"] = origen };
        const string cypherBase =
            "MERGE (h:Hallazgo {textoHash: $textoHash}) " +
            "ON CREATE SET h.texto = $texto, h.origen = $origen " +
            "ON MATCH  SET h.texto  = $texto, h.origen = COALESCE($origen, h.origen)";
        await RunWriteAsync(cypherBase, pBase, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(dataFileId))
        {
            const string cypherRel =
                "MATCH (h:Hallazgo {textoHash: $textoHash}) " +
                "MERGE (d:Documento {dataFileId: $dataFileId}) " +
                "MERGE (h)-[:SUSTENTADO_POR]->(d)";
            await RunWriteAsync(cypherRel, Params("textoHash", hash, "dataFileId", dataFileId), ct).ConfigureAwait(false);
        }
    }

    // ── Consultas read-only (allow-list) ──────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string queryName,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryName);
        ArgumentNullException.ThrowIfNull(parameters);

        if (!AllowedQueries.TryGetValue(queryName, out var cypher))
            throw new ArgumentException(
                $"[T21] Consulta '{queryName}' no está en la allow-list del grafo. " +
                $"Consultas válidas: {string.Join(", ", AllowedQueries.Keys)}.",
                nameof(queryName));

        var driver = await GetDriverAsync(ct).ConfigureAwait(false);
        await using var session = driver.AsyncSession(
            o => o.WithDefaultAccessMode(AccessMode.Read));

        // Neo4j.Driver 5.x: RunAsync(string, IDictionary<string,object>)
        // Convertir IReadOnlyDictionary<string,object?> a Dictionary<string,object>
        var neoParams = ToNeoDictionary(parameters);
        var cursor    = await session.RunAsync(cypher, neoParams).ConfigureAwait(false);
        var records   = await cursor.ToListAsync().ConfigureAwait(false);

        var results = new List<IReadOnlyDictionary<string, object?>>(records.Count);
        foreach (var record in records)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in record.Keys)
                row[key] = record[key];
            results.Add(row);
        }

        return results;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Ejecuta una sentencia de escritura en una sesión con modo Write.
    /// CancellationToken se usa solo en la inicialización del driver;
    /// el driver Neo4j 5.x no acepta CT directamente en RunAsync.
    /// </summary>
    private async Task RunWriteAsync(
        string cypher,
        IDictionary<string, object?> parameters,
        CancellationToken ct)
    {
        var driver = await GetDriverAsync(ct).ConfigureAwait(false);
        await using var session = driver.AsyncSession(
            o => o.WithDefaultAccessMode(AccessMode.Write));

        var cursor = await session.RunAsync(cypher, ToNeoDictionary(parameters)).ConfigureAwait(false);
        await cursor.ConsumeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Construye un <see cref="Dictionary{TKey,TValue}"/> de parámetros a partir de
    /// pares nombre/valor (args alternados).
    /// </summary>
    private static Dictionary<string, object?> Params(params object?[] namesAndValues)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i + 1 < namesAndValues.Length; i += 2)
            dict[namesAndValues[i]!.ToString()!] = namesAndValues[i + 1];
        return dict;
    }

    /// <summary>
    /// Convierte <c>IDictionary&lt;string,object?&gt;</c> al tipo que espera Neo4j.Driver 5.x:
    /// <c>IDictionary&lt;string,object&gt;</c> (sin nullables en el value).
    /// Los valores null se pasan como <c>null</c> boxeado — el driver los convierte a
    /// <c>null</c> en Cypher.
    /// </summary>
    private static Dictionary<string, object> ToNeoDictionary(IDictionary<string, object?> source)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in source)
            result[k] = v!;   // el driver acepta null boxeado como object
        return result;
    }

    /// <summary>
    /// Sobrecarga que acepta <c>IReadOnlyDictionary</c>.
    /// </summary>
    private static Dictionary<string, object> ToNeoDictionary(IReadOnlyDictionary<string, object?> source)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in source)
            result[k] = v!;   // el driver acepta null boxeado como object
        return result;
    }

    /// <summary>
    /// Obtiene (o inicializa) el driver Neo4j de forma perezosa y thread-safe.
    /// Falla con <see cref="InvalidOperationException"/> si las credenciales no están
    /// configuradas (bloqueo B6 — no en startup).
    /// </summary>
    private async Task<IDriver> GetDriverAsync(CancellationToken ct)
    {
        if (_driver != null) return _driver;

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_driver != null) return _driver;

            var uri      = _config["Neo4j:Uri"];
            var user     = _config["Neo4j:User"];
            var password = _config["Neo4j:Password"];

            if (string.IsNullOrWhiteSpace(uri))
                throw new InvalidOperationException(
                    "[T19 B6] Neo4j:Uri no está configurado. " +
                    "Provisione AuraDB Free y configure las credenciales en Key Vault (bloqueo B6).");

            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException(
                    "[T19 B6] Neo4j:User o Neo4j:Password no están configurados. " +
                    "Configure las credenciales de AuraDB en Key Vault (bloqueo B6).");

            _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));

            _logger.LogInformation("[T19] Driver Neo4j inicializado para {Uri}.", uri);
            return _driver;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_driver != null)
            await _driver.DisposeAsync().ConfigureAwait(false);

        _initLock.Dispose();
    }
}
