/* =============================================================================
   REQ-020b — Una ejecucion puede no operar sobre ningun documento
   -----------------------------------------------------------------------------
   Sintoma:
       The INSERT statement conflicted with the FOREIGN KEY constraint
       "FK_StepExecution_DataFile" ... table "dbo.DataFile", column 'Id'.

   Causa: StepExecution.DataFileId es NOT NULL con FK a DataFile, y el codigo
   escribia 0 cuando el caso no tenia documentos:

       DataFileId = caso.DataFile.FirstOrDefault()?.Id ?? 0

   Ese "?? 0" nunca funciono: de las 742 ejecuciones guardadas, CERO tienen
   DataFileId = 0. Lo que pasaba es que hasta ahora todos los casos llegaban ya
   con documentos (venian de Zendesk o de Armonix), asi que el ramal del ?? 0
   jamas se ejecutaba. El portal del afiliado lo destapo: alli el caso se abre
   ANTES de que la persona suba nada, porque lo primero que se hace es resolver
   sus contratos por cedula.

   Arreglo: la columna pasa a admitir NULL. Una ejecucion que no opera sobre un
   documento concreto -- "resuelve los contratos de esta cedula" -- legitimamente
   no tiene DataFileId. Las alternativas eran peores:
     · seguir metiendo 0        -> viola la FK (es lo que fallaba)
     · apuntar a un documento cualquiera -> corrompe la traza de auditoria
     · no crear la ejecucion    -> imposible: ToolInvocation.ExecutionId es
                                   NOT NULL, sin ejecucion no hay auditoria de
                                   la llamada, que es justo lo que no se quiere
                                   perder

   El cambio es ensanchante (NOT NULL -> NULL): las 742 filas existentes
   conservan su valor y la FK sigue vigente, simplemente no aplica a los NULL.

   Idempotente y con guarda de base.
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
   Antes de tocar nada: que no quede ninguna fila con el 0 invalido.
   Si alguna se colo, se deja constancia y se convierte en NULL, que es lo que
   deberia haber sido.
   --------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM dbo.StepExecution WHERE DataFileId = 0)
BEGIN
    DECLARE @huerfanas int = (SELECT COUNT(*) FROM dbo.StepExecution WHERE DataFileId = 0);
    RAISERROR('Hay %d ejecuciones con DataFileId = 0; se pasan a NULL.', 0, 1, @huerfanas) WITH NOWAIT;
END
GO

/* ---------------------------------------------------------------------------
   1) La FK estorba al cambiar la nulabilidad: se quita y se repone igual.
   --------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_StepExecution_DataFile')
BEGIN
    ALTER TABLE dbo.StepExecution DROP CONSTRAINT FK_StepExecution_DataFile;
END
GO

/* ---------------------------------------------------------------------------
   2) La columna admite NULL
   --------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
           WHERE TABLE_NAME = 'StepExecution' AND COLUMN_NAME = 'DataFileId'
             AND IS_NULLABLE = 'NO')
BEGIN
    ALTER TABLE dbo.StepExecution ALTER COLUMN DataFileId int NULL;
END
GO

/* Los ceros que hubiera se convierten en lo que significaban: "ninguno". */
UPDATE dbo.StepExecution SET DataFileId = NULL WHERE DataFileId = 0;
GO

/* ---------------------------------------------------------------------------
   3) La FK vuelve, con el mismo comportamiento de borrado que tenia
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_StepExecution_DataFile')
BEGIN
    ALTER TABLE dbo.StepExecution WITH CHECK
        ADD CONSTRAINT FK_StepExecution_DataFile
        FOREIGN KEY (DataFileId) REFERENCES dbo.DataFile (Id);
END
GO

/* ---------------------------------------------------------------------------
   Verificacion
   --------------------------------------------------------------------------- */
SELECT 'DataFileId admite NULL' AS Comprobacion,
       (SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS
         WHERE TABLE_NAME = 'StepExecution' AND COLUMN_NAME = 'DataFileId') AS Valor
UNION ALL
SELECT 'FK repuesta',
       CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_StepExecution_DataFile')
            THEN 'si' ELSE 'NO' END
UNION ALL
SELECT 'FK de confianza (no untrusted)',
       CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys
                          WHERE name = 'FK_StepExecution_DataFile' AND is_not_trusted = 0)
            THEN 'si' ELSE 'NO' END
UNION ALL
SELECT 'filas con DataFileId = 0',
       CONVERT(varchar(10), (SELECT COUNT(*) FROM dbo.StepExecution WHERE DataFileId = 0))
UNION ALL
SELECT 'ejecuciones totales',
       CONVERT(varchar(10), (SELECT COUNT(*) FROM dbo.StepExecution));
GO
