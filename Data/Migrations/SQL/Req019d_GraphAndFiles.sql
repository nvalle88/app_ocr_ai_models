-- ============================================================
-- REQ-019 T19/T21 — Delta de migración: ClaudeFileId en DataFile +
--                   siembra de la tool grafo_consultar en OPAITool.
-- BD: db-nexus-test
-- Versión: 0001
-- Fecha: 2026-06-18
-- Autor: DeveloperAI / REQ-019 T19+T21
-- IDEMPOTENTE — guarda con IF NOT EXISTS / MERGE.
-- ADITIVO  — no borra ni altera columnas/filas existentes.
-- ============================================================

-- SALVAGUARDA: solo permitido en la BD de pruebas
IF DB_NAME() <> N'db-nexus-test'
BEGIN
    RAISERROR('Script REQ-019d solo permitido en db-nexus-test. Abortado.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;

-- ============================================================
-- 1. Columna DataFile.ClaudeFileId (T19)
--    varchar(100) NULL — almacena el file_id de la Files API
--    de Claude para reusar el documento en consultas sucesivas.
-- ============================================================
IF NOT EXISTS (
    SELECT 1
    FROM   sys.columns
    WHERE  object_id = OBJECT_ID(N'dbo.DataFile')
    AND    name      = N'ClaudeFileId'
)
BEGIN
    ALTER TABLE dbo.DataFile
        ADD ClaudeFileId varchar(100) NULL;
    PRINT 'Columna DataFile.ClaudeFileId agregada.';
END
ELSE
BEGIN
    PRINT 'DataFile.ClaudeFileId ya existe — sin cambios.';
END;

-- ============================================================
-- 2. Tool grafo_consultar (T21)
--    BindingType = 'Graph' — ejecutada por GraphToolExecutor.
--    IsActive = 1; NO vincular a agentes productivos aquí.
-- ============================================================
BEGIN TRANSACTION;

MERGE dbo.OPAITool AS tgt
USING (SELECT N'grafo_consultar' AS Code) AS src
ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    Name         = N'grafo_consultar',
    Description  = N'Consulta el grafo de conocimiento (Neo4j) de forma read-only para responder preguntas relacionales: otros sobres del afiliado con un diagnóstico, documentos que sustentan un hallazgo, procedimientos cubiertos, preexistencias del afiliado, etc. El modelo elige una consulta predefinida de la allow-list; no se acepta Cypher libre.',
    InputSchema  = N'{
  "type": "object",
  "properties": {
    "queryName": {
      "type": "string",
      "description": "Nombre de la consulta predefinida a ejecutar. Valores válidos: ''sobres_por_afiliado_y_diagnostico'', ''documentos_por_caso'', ''documentos_sustentan_hallazgo'', ''procedimientos_cubiertos_por_caso'', ''preexistencias_afiliado'', ''diagnosticos_por_caso''.",
      "enum": [
        "sobres_por_afiliado_y_diagnostico",
        "documentos_por_caso",
        "documentos_sustentan_hallazgo",
        "procedimientos_cubiertos_por_caso",
        "preexistencias_afiliado",
        "diagnosticos_por_caso"
      ]
    },
    "params": {
      "type": "object",
      "description": "Parámetros de la consulta. Campos según queryName: ''cedula'' (string), ''codigoDiagnostico'' (string), ''caseCode'' (string), ''hallazgoTexto'' (string).",
      "properties": {
        "cedula":            { "type": "string", "description": "Cédula del afiliado." },
        "codigoDiagnostico": { "type": "string", "description": "Código CIE-10 del diagnóstico." },
        "caseCode":          { "type": "string", "description": "Identificador del caso (UUID)." },
        "hallazgoTexto":     { "type": "string", "description": "Texto parcial del hallazgo a buscar." }
      }
    }
  },
  "required": ["queryName", "params"]
}',
    Strict       = 1,
    BindingType  = N'Graph',
    BindingConfig = N'{
  "engine": "neo4j",
  "readOnly": true,
  "allowList": [
    "sobres_por_afiliado_y_diagnostico",
    "documentos_por_caso",
    "documentos_sustentan_hallazgo",
    "procedimientos_cubiertos_por_caso",
    "preexistencias_afiliado",
    "diagnosticos_por_caso"
  ]
}',
    IsActive     = 1,
    VersionNumber = 1
WHEN NOT MATCHED THEN INSERT
    (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES (
    N'grafo_consultar',
    N'grafo_consultar',
    N'Consulta el grafo de conocimiento (Neo4j) de forma read-only para responder preguntas relacionales: otros sobres del afiliado con un diagnóstico, documentos que sustentan un hallazgo, procedimientos cubiertos, preexistencias del afiliado, etc. El modelo elige una consulta predefinida de la allow-list; no se acepta Cypher libre.',
    N'{
  "type": "object",
  "properties": {
    "queryName": {
      "type": "string",
      "description": "Nombre de la consulta predefinida a ejecutar. Valores válidos: ''sobres_por_afiliado_y_diagnostico'', ''documentos_por_caso'', ''documentos_sustentan_hallazgo'', ''procedimientos_cubiertos_por_caso'', ''preexistencias_afiliado'', ''diagnosticos_por_caso''.",
      "enum": [
        "sobres_por_afiliado_y_diagnostico",
        "documentos_por_caso",
        "documentos_sustentan_hallazgo",
        "procedimientos_cubiertos_por_caso",
        "preexistencias_afiliado",
        "diagnosticos_por_caso"
      ]
    },
    "params": {
      "type": "object",
      "description": "Parámetros de la consulta. Campos según queryName: ''cedula'' (string), ''codigoDiagnostico'' (string), ''caseCode'' (string), ''hallazgoTexto'' (string).",
      "properties": {
        "cedula":            { "type": "string", "description": "Cédula del afiliado." },
        "codigoDiagnostico": { "type": "string", "description": "Código CIE-10 del diagnóstico." },
        "caseCode":          { "type": "string", "description": "Identificador del caso (UUID)." },
        "hallazgoTexto":     { "type": "string", "description": "Texto parcial del hallazgo a buscar." }
      }
    }
  },
  "required": ["queryName", "params"]
}',
    1,
    N'Graph',
    N'{
  "engine": "neo4j",
  "readOnly": true,
  "allowList": [
    "sobres_por_afiliado_y_diagnostico",
    "documentos_por_caso",
    "documentos_sustentan_hallazgo",
    "procedimientos_cubiertos_por_caso",
    "preexistencias_afiliado",
    "diagnosticos_por_caso"
  ]
}',
    1, 1, SYSUTCDATETIME()
);

COMMIT TRANSACTION;

-- ============================================================
-- Verificación post-migración
-- ============================================================
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH
FROM   INFORMATION_SCHEMA.COLUMNS
WHERE  TABLE_NAME = 'DataFile'
AND    COLUMN_NAME = 'ClaudeFileId';

SELECT Code, Name, BindingType, IsActive
FROM   dbo.OPAITool
WHERE  Code = N'grafo_consultar';
