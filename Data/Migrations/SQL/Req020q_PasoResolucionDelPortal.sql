/* =============================================================================
   REQ-020q — El portal no tenía paso de resolución: por eso «Calcular» no hacía
              nada
   -----------------------------------------------------------------------------
   Sintoma: el afiliado pulsa «Calcular mi reembolso» y la pagina se recarga
   igual. Ni error, ni resultado.

   La cadena completa, medida sobre el caso af90411d:

     1. Se creo el Process PORTAL_CLIENTE (REQ-020a) SIN definirle pasos.
        ProcessStep no tiene ninguna fila para el.
     2. ProcessOrchestrator.RunAsync recorre los pasos del proceso del caso.
        Sin pasos no ejecuta nada y devuelve texto vacio — no hay ni una fila en
        StepExecution para la resolucion, solo las del portal y el auditor.
     3. ResolucionController, al no encontrar JSON, guarda el marcador de
        control humano: {"_raw":true}, 13 caracteres.
     4. La pantalla del cliente daba el paso por HECHO porque la nota existe,
        pero la resolucion no tenia contenido, asi que volvia a «podemos
        calcular». Con todo marcado como hecho, el boton recorria una lista
        vacia y recargaba: «no hace nada».

   El proceso que SI resuelve es ANALISIS_SOBRE, con un unico paso sobre
   AGENTE_CLAUDE — el que lleva SKILL_RESOLUCION_REEMBOLSO y sus herramientas.
   Tiene 18 resoluciones con contenido real. El portal necesita exactamente ese
   mismo paso: es el mismo trabajo, cambia solo quien lo pide.

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
   El paso de resolución del portal: el mismo de ANALISIS_SOBRE.
   Se copia de él en vez de escribirlo a mano, para que no puedan divergir.
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.ProcessStep WHERE ProcessCode = 'PORTAL_CLIENTE')
BEGIN
    INSERT INTO dbo.ProcessStep
        (ProcessCode, StepOrder, ModelCode, StepName, StepsToInclude, AggregateExecution, SourceType)
    SELECT 'PORTAL_CLIENTE', ps.StepOrder, ps.ModelCode,
           'Resolución del reembolso', ps.StepsToInclude, ps.AggregateExecution, ps.SourceType
      FROM dbo.ProcessStep ps
     WHERE ps.ProcessCode = 'ANALISIS_SOBRE';
END
GO

/* ---------------------------------------------------------------------------
   Las resoluciones vacías que dejó el defecto: se borran para que el paso deje
   de contarse como hecho y se pueda recalcular. No se pierde nada — {"_raw":true}
   no contiene resolución alguna.
   --------------------------------------------------------------------------- */
DECLARE @vacias int =
    (SELECT COUNT(*) FROM dbo.Notes
      WHERE Title = 'ResolucionReembolso' AND LEN(Detail) < 40);

DELETE FROM dbo.Notes
 WHERE Title = 'ResolucionReembolso' AND LEN(Detail) < 40;

RAISERROR('Resoluciones vacias eliminadas: %d', 0, 1, @vacias) WITH NOWAIT;
GO

/* ---------------------------------------------------------------------------
   Verificación
   --------------------------------------------------------------------------- */
SELECT 'paso del portal' AS Pieza,
       CONVERT(varchar(10), (SELECT COUNT(*) FROM dbo.ProcessStep
                              WHERE ProcessCode = 'PORTAL_CLIENTE')) AS Valor
UNION ALL
SELECT 'modelo que ejecuta',
       ISNULL((SELECT TOP 1 ModelCode FROM dbo.ProcessStep
                WHERE ProcessCode = 'PORTAL_CLIENTE'), '(ninguno)')
UNION ALL
SELECT 'resoluciones vacias que quedan',
       CONVERT(varchar(10), (SELECT COUNT(*) FROM dbo.Notes
                              WHERE Title = 'ResolucionReembolso' AND LEN(Detail) < 40));
GO
