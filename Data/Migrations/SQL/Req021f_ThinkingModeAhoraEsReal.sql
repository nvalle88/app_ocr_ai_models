/* =============================================================================
   REQ-021f - ThinkingMode dejo de ser decorativo: hay que dejarlo donde estaba
   -----------------------------------------------------------------------------
   Hasta ahora dbo.Agent.ThinkingMode, .Temperature y .ToolChoice se escribian y
   NADIE las leia en ClaudeCompletionService, que es quien construye la peticion.
   Prueba: los tres agentes lentos estaban en 'adaptive' y dbo.Usage marcaba
   ThinkingTokens = 0 en todas las ejecuciones. El pensamiento extendido no se
   estaba usando.

   Ya estan cableadas. Y eso crea un problema inmediato: si se dejan como estan,
   AGENTE_AUDITOR_MEDICINA, AGENTE_CLAUDE y AGENTE_PORTAL_CLIENTE encenderian
   pensamiento extendido de golpe, en todas sus llamadas, sin que nadie lo haya
   pedido. Multiplicaria latencia y coste justo despues de haber bajado ambos con
   la cache de prompt: un caso ya tarda ~3 minutos.

   Asi que se normalizan a 'off'. NO es "apagar una mejora": es dejar el
   comportamiento EXACTAMENTE como esta hoy, porque hoy el pensamiento no
   ocurre. Lo que cambia es que a partir de ahora encenderlo funciona de verdad,
   y por tanto es una decision que se toma a proposito y se mide.

   Para encenderlo en un agente concreto:

       UPDATE dbo.Agent SET ThinkingMode = 'adaptive' WHERE Code = '...';

   y luego comparar en dbo.Usage los ThinkingTokens y los segundos de
   dbo.StepExecution contra la linea base de hoy:

       resolucion 65 s | clasificacion 60 s | auditoria 55 s | expediente 24 s

   OJO: con pensamiento extendido la API exige temperature = 1. Si se enciende un
   agente que tenga Temperature fijada a 0, hay que quitarla.

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

DECLARE @tocados int;

UPDATE dbo.Agent
   SET ThinkingMode = 'off',
       ModifiedDate = SYSUTCDATETIME()
 WHERE ThinkingMode IS NOT NULL
   AND LOWER(LTRIM(RTRIM(ThinkingMode))) IN ('adaptive', 'enabled', 'on', 'extended');

SET @tocados = @@ROWCOUNT;
RAISERROR('Agentes normalizados a ThinkingMode = off: %d', 0, 1, @tocados) WITH NOWAIT;
GO

/* ---------------------------------------------------------------------------
   Verificacion: ninguno debe quedar encendido sin haberlo decidido.
   --------------------------------------------------------------------------- */
SELECT Code, ThinkingMode, Temperature, ToolChoice, MaxTokens
  FROM dbo.Agent
 WHERE IsActive = 1 AND ModelId IS NOT NULL
 ORDER BY Code;
GO
