/* =============================================================================
   REQ-020r — El portal no tenia agente registrado para su proceso
   -----------------------------------------------------------------------------
   Ultimo eslabon de por que «Calcular mi reembolso» no hacia nada.

   ProcessOrchestrator.RunClassifierAsync busca el agente asi:

       ap.DefinitionCode == processCase.DefinitionCode
       && ap.Agent.IsActive
       && (allowedAgentCodes.Count == 0 || allowedAgentCodes.Contains(ap.AgentCode))

   La ultima clausula es la que explica que ANALISIS_SOBRE funcione sin tener
   ninguna politica: sin politicas, se permite todo. Lo que NO se puede saltar
   es la primera: hace falta una fila en AgentProcess para el proceso del caso.

   PORTAL_CLIENTE no tenia ninguna, asi que el clasificador devolvia null, el
   orquestador cortaba con «No se encontro un agente clasificador activo» y
   ResolucionController guardaba el marcador de control humano {"_raw":true}.

   Se registra el mismo agente que usa ANALISIS_SOBRE: AGENTE_CLAUDE, el que
   lleva SKILL_RESOLUCION_REEMBOLSO. Es el mismo trabajo; cambia quien lo pide.

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

IF NOT EXISTS (SELECT 1 FROM dbo.AgentProcess WHERE DefinitionCode = 'PORTAL_CLIENTE')
BEGIN
    INSERT INTO dbo.AgentProcess (DefinitionCode, AgentCode)
    SELECT 'PORTAL_CLIENTE', ap.AgentCode
      FROM dbo.AgentProcess ap
     WHERE ap.DefinitionCode = 'ANALISIS_SOBRE';
END
GO

SELECT 'agentes del portal' AS Pieza,
       CONVERT(varchar(10), (SELECT COUNT(*) FROM dbo.AgentProcess
                              WHERE DefinitionCode='PORTAL_CLIENTE')) AS Valor
UNION ALL
SELECT 'cual',
       ISNULL((SELECT TOP 1 AgentCode FROM dbo.AgentProcess
                WHERE DefinitionCode='PORTAL_CLIENTE'), '(ninguno)');
GO
