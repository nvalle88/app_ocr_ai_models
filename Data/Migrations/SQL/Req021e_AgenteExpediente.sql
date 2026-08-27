/* =============================================================================
   REQ-021e - El expediente no tenia agente, y por eso no se podia medir
   -----------------------------------------------------------------------------
   Dos de los cuatro pasos del pipeline no dejaban ni una fila en StepExecution:
   la clasificacion y el expediente. El paso mas lento del flujo -medido a mano
   en el navegador, ~50 s con un solo PDF- era invisible en cualquier consulta.

   La causa no era un olvido: StepExecution.ModelCode tiene FK contra dbo.Agent
   (FK_StepExecution_Agent). La clasificacion SI tiene su agente
   (AGENTE_CLASIFICADOR_DOC, lo usa como constante), pero el expediente no tenia
   ninguno: el controlador coge la primera OPAIConfiguration activa de Anthropic
   y llama al modelo directamente. Sin fila en Agent no se puede registrar el
   paso, y sin registrar el paso no hay duracion ni tokens.

   Se le da su agente. Mismos parametros con los que ya corre hoy
   (MaxTokens 6000, el modelo de CLAUDE_FOUNDRY), asi que el comportamiento no
   cambia: lo que cambia es que ahora se ve.

   OJO, deuda que NO se toca aqui: el prompt del expediente sigue siendo una
   constante en ExpedienteController (ExpedientePrompt), no la columna
   SystemPrompt de este agente. Moverlo es un cambio aparte y con su propia
   verificacion; dejarlo a medias seria peor. Queda escrito para que se sepa
   que la fila existe por telemetria, no como fuente del prompt todavia.

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

IF NOT EXISTS (SELECT 1 FROM dbo.Agent WHERE Code = 'AGENTE_EXPEDIENTE')
BEGIN
    INSERT INTO dbo.Agent
        (Code, ConfigCode, Name, VersionNumber, Description,
         CreatedDate, ModifiedDate, IsActive, ModelId, SystemPrompt,
         MaxTokens, ThinkingMode, Effort, Temperature, ToolChoice)
    SELECT 'AGENTE_EXPEDIENTE',
           a.ConfigCode,
           'Expediente documental (ordena que respalda a que)',
           1,
           'Empareja cada respaldo con la factura que justifica. Existe tambien para que el paso quede registrado en StepExecution, que tiene FK contra esta tabla.',
           SYSUTCDATETIME(), SYSUTCDATETIME(), 1,
           a.ModelId,
           NULL,          -- el prompt sigue viviendo en ExpedienteController
           6000,          -- lo que ya usa el controlador hoy
           a.ThinkingMode, a.Effort, a.Temperature,
           'none'         -- el expediente no usa herramientas
      FROM dbo.Agent a
     WHERE a.Code = 'AGENTE_CLAUDE';
END
GO

/* ---------------------------------------------------------------------------
   Verificacion
   --------------------------------------------------------------------------- */
SELECT Code, ConfigCode, ModelId, MaxTokens, ToolChoice, IsActive
  FROM dbo.Agent
 WHERE Code IN ('AGENTE_EXPEDIENTE', 'AGENTE_CLASIFICADOR_DOC')
 ORDER BY Code;
GO
