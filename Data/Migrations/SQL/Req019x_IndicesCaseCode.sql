/* =============================================================================
   REQ-019x — Los índices por CaseCode que el modelo declara y la base no tiene
   -----------------------------------------------------------------------------
   EL DEFECTO
     OCRDbContext.cs:191 declara HasIndex(e => e.CaseCode, "IX_DataFile_CaseCode")
     y OCRDbContext.cs:540 lo mismo para FinalResponseResult. En la base NO
     existen: solo está la PK. Causa raíz: el esquema se aplica con scripts
     sueltos de esta carpeta, no con migraciones EF, y ese CREATE INDEX nunca se
     escribió (grep sobre los scripts: cero coincidencias).

   IMPACTO MEDIDO (db-nexus-test, 2026-08-25)
     DataFile = 10.376 filas, 5.858 casos distintos, 2.707 páginas in-row (21 MB).
     Media de 1,77 archivos por caso, así que CUALQUIER caso dispara el escaneo
     completo de la tabla.

       consulta de proyección  : 2.707 → 2 lecturas lógicas
       carga de entidad completa: 2.707 → 122 lecturas lógicas
       reloj                    : 216,6 ms → 2,045 ms   (~106x)

     El LOB no se toca en ninguno de los dos casos (lob logical reads = 0), así
     que esto es puramente el clustered scan.

     Afecta a 8 puntos bajo Areas/Studio/Controllers: ClasificacionController:55
     y :210, más los .Include(pc => pc.DataFile) de Analizar, Auditoria, Chat,
     Expediente, Sobres y Nexus. Además FK_DataFile_ProcessCase es NO_ACTION sin
     índice, así que un DELETE de caso también escanearía.

   POR QUÉ SIN INCLUDE — medido lado a lado, no supuesto
     La clave pelada ocupa 288 KB y da 122 lecturas.
     Con INCLUDE(FileUri, OriginalName, ClaudeFileId, IsFileUri) ocupa 2.632 KB
     (9,1x más) y da 124: sale PEOR. Ninguna consulta se cubre con él — las de
     proyección piden solo Id (gratis por el clustered) y la dominante pide la
     entidad entera, incluidos Text y CreatedDate, que el INCLUDE no cubre.
     Encima ClaudeFileId está NULL en el 100% de las filas y OriginalName en el 40%.

   StepExecution
     Sin más índice que el PK clustered. La consulta de AuditoriaController:255 y
     ResolucionController:149 le sale al optimizador al revés: recorre
     ToolInvocation y busca en StepExecution por cada invocación →
     708 lecturas lógicas para devolver CERO filas. Simulado a 57.000 filas sube
     a 8.162, que el índice deja en 3: a escala empeora, no se diluye.
     Su FK a ProcessCase es CASCADE sin índice.
     También pelado: medido, con INCLUDE(StepOrder, Status, DataFileId) da 3
     lecturas contra 2 del pelado, porque es más ancho y no evita ningún key
     lookup (la consulta que lee la entidad arrastra RequestContent).

   Aditivo, idempotente y con guarda de base. Cero cambio de código.
   ============================================================================= */

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    RAISERROR('Este script solo debe correr en db-nexus-test. Base actual: %s', 16, 1, @@SERVERNAME);
    RETURN;
END
GO

/* ---------------------------------------------------------------------------
   1) DataFile.CaseCode — el de mayor impacto
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_DataFile_CaseCode' AND object_id = OBJECT_ID('dbo.DataFile'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_DataFile_CaseCode ON dbo.DataFile ([CaseCode]);
    PRINT 'IX_DataFile_CaseCode creado';
END
ELSE PRINT 'IX_DataFile_CaseCode ya existia';
GO

/* ---------------------------------------------------------------------------
   2) FinalResponseResult.CaseCode
      CreatedDate DESC porque AnalizarController:102 filtra por CaseCode y ordena
      descendente con Take(10): así el índice sirve el filtro Y el orden.
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_FinalResponseResult_CaseCode' AND object_id = OBJECT_ID('dbo.FinalResponseResult'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_FinalResponseResult_CaseCode
        ON dbo.FinalResponseResult ([CaseCode], [CreatedDate] DESC);
    PRINT 'IX_FinalResponseResult_CaseCode creado';
END
ELSE PRINT 'IX_FinalResponseResult_CaseCode ya existia';
GO

/* ---------------------------------------------------------------------------
   3) StepExecution.CaseCode
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_StepExecution_CaseCode' AND object_id = OBJECT_ID('dbo.StepExecution'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_StepExecution_CaseCode ON dbo.StepExecution ([CaseCode]);
    PRINT 'IX_StepExecution_CaseCode creado';
END
ELSE PRINT 'IX_StepExecution_CaseCode ya existia';
GO

/* ---------------------------------------------------------------------------
   Verificación: existen, y cuánto ocupan
   --------------------------------------------------------------------------- */
SELECT i.name                                   AS Indice,
       OBJECT_NAME(i.object_id)                 AS Tabla,
       SUM(a.total_pages) * 8                   AS KB,
       (SELECT SUM(p2.rows) FROM sys.partitions p2
        WHERE p2.object_id = i.object_id AND p2.index_id IN (0,1)) AS FilasTabla
FROM sys.indexes i
JOIN sys.partitions p ON p.object_id = i.object_id AND p.index_id = i.index_id
JOIN sys.allocation_units a ON a.container_id = p.partition_id
WHERE i.name IN ('IX_DataFile_CaseCode', 'IX_FinalResponseResult_CaseCode', 'IX_StepExecution_CaseCode')
GROUP BY i.name, i.object_id
ORDER BY i.name;
GO
