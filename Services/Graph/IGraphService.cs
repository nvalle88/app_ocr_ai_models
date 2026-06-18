namespace app_tramites.Services.Graph;

// ============================================================
// REQ-019 T19 — Interfaz del servicio de grafo de conocimiento.
// Implementación: Neo4jGraphService (AuraDB Free).
// Sin conexión real hasta B6 (credenciales + egress).
// ============================================================

/// <summary>
/// Servicio de grafo de conocimiento sobre Neo4j AuraDB.
/// Provee operaciones MERGE idempotentes para nodos y relaciones,
/// bootstrap del esquema (constraints + índices), y consultas read-only
/// parametrizadas para la tool <c>grafo_consultar</c>.
/// </summary>
/// <remarks>
/// Dependencia: bloqueo B6 (Neo4j AuraDB Free + egress + Key Vault).
/// La implementación <see cref="Neo4jGraphService"/> falla en runtime si
/// <c>Neo4j:Uri</c> / <c>Neo4j:User</c> / <c>Neo4j:Password</c> no están
/// configurados; no falla en startup.
/// </remarks>
public interface IGraphService
{
    // ── Bootstrap ────────────────────────────────────────────────────────

    /// <summary>
    /// Inicializa el esquema del grafo: constraints de unicidad e índices.
    /// Idempotente; seguro de ejecutar más de una vez.
    /// </summary>
    /// <param name="ct">Token de cancelación.</param>
    Task EnsureSchemaAsync(CancellationToken ct = default);

    // ── MERGE de nodos ───────────────────────────────────────────────────

    /// <summary>
    /// MERGE de nodo <c>Afiliado</c> (clave: <paramref name="cedula"/>).
    /// Actualiza <paramref name="nombre"/> si el nodo ya existe.
    /// </summary>
    Task MergeAfiliadoAsync(string cedula, string? nombre, CancellationToken ct = default);

    /// <summary>
    /// MERGE de nodo <c>Sobre</c> (clave: <paramref name="numeroSobre"/>).
    /// Crea o actualiza la relación <c>(:Sobre)-[:DE]->(:Afiliado)</c> si se provee la cédula.
    /// </summary>
    Task MergeSobreAsync(
        string numeroSobre,
        string? cuentaZendesk,
        string? ticketId,
        string? cedula,
        CancellationToken ct = default);

    /// <summary>
    /// MERGE de nodo <c>Caso</c> (clave: <paramref name="caseCode"/>).
    /// Crea o actualiza la relación <c>(:Sobre)-[:GENERA]->(:Caso)</c> si se provee el número de sobre.
    /// </summary>
    Task MergeCasoAsync(
        string caseCode,
        string? numeroSobre,
        CancellationToken ct = default);

    /// <summary>
    /// MERGE de nodo <c>Documento</c> (clave: <paramref name="dataFileId"/>).
    /// Crea o actualiza la relación <c>(:Caso)-[:CONTIENE]->(:Documento)</c>.
    /// </summary>
    Task MergeDocumentoAsync(
        string dataFileId,
        string? claudeFileId,
        string? fileUri,
        string? tipo,
        string caseCode,
        CancellationToken ct = default);

    /// <summary>
    /// MERGE de nodo <c>Diagnostico</c> (clave: <paramref name="codigo"/>).
    /// Crea la relación <c>(:Documento)-[:MENCIONA]->(:Diagnostico)</c>.
    /// </summary>
    Task MergeDiagnosticoAsync(
        string codigo,
        string? descripcion,
        string dataFileId,
        CancellationToken ct = default);

    /// <summary>
    /// MERGE de nodo <c>Procedimiento</c> (clave: <paramref name="codigo"/>).
    /// Crea la relación <c>(:Documento)-[:MENCIONA]->(:Procedimiento)</c>.
    /// </summary>
    Task MergeProcedimientoAsync(
        string codigo,
        string? descripcion,
        string dataFileId,
        CancellationToken ct = default);

    /// <summary>
    /// MERGE de nodo <c>Cobertura</c> (clave: <paramref name="convenio"/>+<paramref name="producto"/>+<paramref name="plan"/>).
    /// Crea la relación <c>(:Procedimiento)-[:CUBIERTO_POR]->(:Cobertura)</c> si se provee el código de procedimiento.
    /// </summary>
    Task MergeCoberturaAsync(
        string convenio,
        string? producto,
        string? plan,
        string? codigoProcedimiento,
        CancellationToken ct = default);

    /// <summary>
    /// MERGE de nodo <c>Preexistencia</c> (clave: <paramref name="codigo"/>).
    /// Crea las relaciones <c>(:Documento)-[:MENCIONA]->(:Preexistencia)</c>
    /// y <c>(:Afiliado)-[:TIENE]->(:Preexistencia)</c> si se proveen las claves.
    /// </summary>
    Task MergePreexistenciaAsync(
        string codigo,
        string? descripcion,
        string? dataFileId,
        string? cedula,
        CancellationToken ct = default);

    /// <summary>
    /// MERGE de nodo <c>Hallazgo</c> (clave: hash del <paramref name="texto"/>).
    /// Crea la relación <c>(:Hallazgo)-[:SUSTENTADO_POR]->(:Documento)</c>.
    /// </summary>
    Task MergeHallazgoAsync(
        string texto,
        string? origen,
        string? dataFileId,
        CancellationToken ct = default);

    // ── Consultas read-only (allow-list) ─────────────────────────────────

    /// <summary>
    /// Ejecuta una de las consultas Cypher predefinidas de la allow-list.
    /// El <paramref name="queryName"/> debe estar en la allow-list; de lo contrario
    /// lanza <see cref="ArgumentException"/>.
    /// </summary>
    /// <param name="queryName">
    /// Nombre de la consulta predefinida. Valores válidos:
    /// <c>sobres_por_afiliado_y_diagnostico</c>, <c>documentos_por_caso</c>,
    /// <c>documentos_sustentan_hallazgo</c>, <c>procedimientos_cubiertos_por_caso</c>,
    /// <c>preexistencias_afiliado</c>, <c>diagnosticos_por_caso</c>.
    /// </param>
    /// <param name="parameters">
    /// Parámetros de la consulta: <c>cedula</c>, <c>codigoDiagnostico</c>,
    /// <c>caseCode</c>, <c>hallazgoTexto</c> según la consulta.
    /// </param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Lista de registros como diccionarios clave/valor.</returns>
    /// <exception cref="ArgumentException">Si <paramref name="queryName"/> no está en la allow-list.</exception>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string queryName,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default);
}
