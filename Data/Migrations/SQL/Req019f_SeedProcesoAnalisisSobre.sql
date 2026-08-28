-- ============================================================
-- REQ-019 T-final: Proceso funcional Claude 'ANALISIS_SOBRE'
-- BD: db-nexus-test  |  IDEMPOTENTE  |  ADITIVO
-- Requisitos previos: Req019_ModeloClaude.sql, Req019c_SeedTools.sql, Req019e_SeedClaudeFoundry.sql
--
-- Qué hace (lo que faltaba para que el motor IA "haga algo"):
--   1) Vincula las tools activas al agente Claude (OPAIModelTool) — antes ninguna estaba ligada.
--   2) Afina el prompt del agente para que ejecute el flujo completo (contrato→preexistencias→
--      coberturas→deducible→procedimientos→valores estimados).
--   3) Crea un Process ACTIVO 'ANALISIS_SOBRE' con un ProcessStep que usa AGENTE_CLAUDE
--      (para que "Analizar con IA" tenga pasos que ejecutar).
--   4) Crea el AgentProcess DefinitionCode='ANALISIS_SOBRE' -> AGENTE_CLAUDE
--      (para que "Chat" y el clasificador resuelvan el agente).
-- Tras esto, al importar un sobre eligiendo el proceso "Analisis de sobre (Claude)",
-- Analizar/Chat sí consultan coberturas, deducibles y preexistencias vía tools.
-- ============================================================
IF DB_NAME() <> N'db-nexus-test'
BEGIN
    RAISERROR('Script REQ-019f solo permitido en db-nexus-test. Abortado.', 16, 1);
    RETURN;
END;

SET QUOTED_IDENTIFIER ON;   -- requerido por MERGE (índices filtrados en el esquema)
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Precondición: el agente y su config deben existir (Req019e)
IF NOT EXISTS (SELECT 1 FROM dbo.Agent WHERE Code = 'AGENTE_CLAUDE')
    THROW 51001, 'Falta Agent AGENTE_CLAUDE: correr Req019e_SeedClaudeFoundry.sql primero.', 1;

-- ------------------------------------------------------------
-- 0) Afinar el prompt del agente para el flujo completo
--    (usa las herramientas; consolida una ESTIMACIÓN de valores, no liquidación oficial)
-- ------------------------------------------------------------
UPDATE dbo.Agent
   SET SystemPrompt = N'Eres un analista médico-administrativo de Saludsa. Analizas el sobre de reembolso a partir del texto OCR de sus documentos y evalúas la solicitud SEGÚN EL PLAN DEL CLIENTE usando las herramientas disponibles. No inventes datos: si un dato no está, dilo. Sigue este flujo:
1) Del OCR, identifica la CÉDULA del beneficiario y los ÍTEMS de la factura (código/descripción del procedimiento, cantidad, valor presentado, diagnóstico si aparece).
2) resolver_contrato_por_cedula → obtén región, producto, plan, versión, contrato y número de persona (necesarios para las demás consultas).
3) consultar_preexistencias_por_cedula y consultar_diagnosticos_preexistentes → revisa si el diagnóstico de la factura es una preexistencia.
4) consultar_coberturas_plan y consultar_deducible_contrato → obtén cobertura/carencias y el deducible/saldo.
5) Por cada ítem de la factura, valida el procedimiento con validar_procedimiento_factura (homologación código Salud, si pasa edad/género/frecuencia y el PVP/valor de tarifario).
6) Consolida una ESTIMACIÓN de valores (rotúlala como estimación pre-liquidación, NO liquidación oficial): por ítem, valorEstimadoCubierto = min(valorPresentado, PVP) × %cobertura; aplica el deducible pendiente en cascada; el resto es copago. Suma totales.
Entrega: resumen del caso, resultado por ítem (cubierto/no cubierto y por qué, con valores), y observaciones (preexistencias, carencias, tope de deducible). Cita siempre la herramienta/fuente de cada dato.'
 WHERE Code = 'AGENTE_CLAUDE';

-- ------------------------------------------------------------
-- 1) Vincular tools activas a AGENTE_CLAUDE (N:M OPAIModelTool)
--    Solo tools productivas/activas; excluye la INACTIVA de prestador.
--    validar_procedimiento_factura se vincula en Req019g (tras crearla).
-- ------------------------------------------------------------
;WITH toolset([ToolCode],[Order]) AS (
    SELECT 'resolver_contrato_por_cedula',          1
    UNION ALL SELECT 'consultar_preexistencias_por_cedula',   2
    UNION ALL SELECT 'consultar_diagnosticos_preexistentes',  3
    UNION ALL SELECT 'consultar_coberturas_plan',             4
    UNION ALL SELECT 'consultar_deducible_contrato',          5
    UNION ALL SELECT 'consultar_deducibles_coberturas_plan',  6
    UNION ALL SELECT 'obtener_documentos_sobre_armonix',      7
)
MERGE dbo.OPAIModelTool AS tgt
USING (
    SELECT 'AGENTE_CLAUDE' AS ModelCode, ts.[ToolCode], ts.[Order]
    FROM toolset ts
    JOIN dbo.OPAITool t ON t.Code = ts.[ToolCode] AND t.IsActive = 1
) AS src
   ON tgt.ModelCode = src.ModelCode AND tgt.ToolCode = src.ToolCode
WHEN MATCHED THEN
    UPDATE SET tgt.[Order] = src.[Order], tgt.IsEnabled = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (ModelCode, ToolCode, [Order], IsEnabled)
    VALUES (src.ModelCode, src.ToolCode, src.[Order], 1);

-- ------------------------------------------------------------
-- 2a) Process activo 'ANALISIS_SOBRE'
-- ------------------------------------------------------------
MERGE dbo.Process AS tgt
USING (SELECT 'ANALISIS_SOBRE' AS Code) AS src
   ON tgt.Code = src.Code
WHEN MATCHED THEN
    UPDATE SET Name = 'Analisis de sobre (Claude)', IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (Code, Name, Description, ClonedFromCode, VersionNumber, IsActive)
    VALUES ('ANALISIS_SOBRE',
            'Analisis de sobre (Claude)',
            'Evalua el sobre/documentos con el agente Claude y sus herramientas (contrato, preexistencias, coberturas, deducibles, procedimientos y documentos del sobre).',
            NULL, 1, 1);

-- ------------------------------------------------------------
-- 2b) ProcessStep 1 -> ModelCode AGENTE_CLAUDE, SourceType Original(0)
--     PK (ProcessCode, StepOrder). StepsToInclude=0. AggregateExecution=0.
-- ------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM dbo.ProcessStep
    WHERE ProcessCode = 'ANALISIS_SOBRE' AND StepOrder = 1
)
    INSERT INTO dbo.ProcessStep
        (ProcessCode, StepOrder, ModelCode, StepName, StepsToInclude, SourceType, AggregateExecution)
    VALUES
        ('ANALISIS_SOBRE', 1, 'AGENTE_CLAUDE', 'Analisis del sobre', 0, 0, 0);
ELSE
    UPDATE dbo.ProcessStep
       SET ModelCode = 'AGENTE_CLAUDE', SourceType = 0
     WHERE ProcessCode = 'ANALISIS_SOBRE' AND StepOrder = 1;

-- ------------------------------------------------------------
-- 3) AgentProcess: DefinitionCode='ANALISIS_SOBRE' -> AgentCode='AGENTE_CLAUDE'
--    Lo usan Chat (ChatController) y el clasificador (ProcessOrchestrator).
-- ------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM dbo.AgentProcess
    WHERE DefinitionCode = 'ANALISIS_SOBRE' AND AgentCode = 'AGENTE_CLAUDE'
)
    INSERT INTO dbo.AgentProcess (DefinitionCode, AgentCode)
    VALUES ('ANALISIS_SOBRE', 'AGENTE_CLAUDE');

COMMIT TRANSACTION;

-- Verificación
SELECT 'OPAIModelTool' AS tabla, ModelCode, ToolCode, [Order], IsEnabled
  FROM dbo.OPAIModelTool WHERE ModelCode = 'AGENTE_CLAUDE' ORDER BY [Order];
SELECT 'Process' AS tabla, Code, Name, IsActive FROM dbo.Process WHERE Code = 'ANALISIS_SOBRE';
SELECT 'ProcessStep' AS tabla, ProcessCode, StepOrder, ModelCode FROM dbo.ProcessStep WHERE ProcessCode = 'ANALISIS_SOBRE' ORDER BY StepOrder;
SELECT 'AgentProcess' AS tabla, Id, DefinitionCode, AgentCode FROM dbo.AgentProcess WHERE DefinitionCode = 'ANALISIS_SOBRE';
GO
