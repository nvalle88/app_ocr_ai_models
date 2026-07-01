-- REQ-019 T23/config — Seed de OPAIConfiguration + Agent apuntando a Claude en Azure AI Foundry.
-- Correr en db-nexus-test DESPUÉS de Req019_ModeloClaude.sql (crea las columnas Provider/SecretRef/ModelId/etc.).
-- La key NO va aquí: usa SecretRef -> Key Vault. El ApiKey queda NULL (placeholder para prueba manual).

IF DB_NAME() <> N'db-nexus-test'
BEGIN
    RAISERROR('Script REQ-019e solo permitido en db-nexus-test. Abortado.', 16, 1);
    RETURN;
END;

SET XACT_ABORT ON;
BEGIN TRAN;

-- 1) Conexión al endpoint Anthropic-compatible de Azure AI Foundry
MERGE dbo.OPAIConfiguration AS t
USING (SELECT 'CLAUDE_FOUNDRY' AS Code) AS s ON t.Code = s.Code
WHEN NOT MATCHED THEN
    INSERT (Code, Name, Provider, EndpointUrl, ApiKey, SecretRef, ConfigType, IsActive)
    VALUES ('CLAUDE_FOUNDRY',
            'Claude Opus 4.8 (Azure AI Foundry)',
            'Anthropic',
            'https://yvall-mr1pokp6-eastus2.services.ai.azure.com/anthropic',  -- base SIN /v1/messages (el SDK lo agrega)
            '',                         -- ApiKey: placeholder vacio (columna NOT NULL). La key real se pone por UPDATE (test) o via env ANTHROPIC_API_KEY / SecretRef->Key Vault (prod)
            'anthropic-foundry-key',    -- SecretRef: nombre del secret en Key Vault con la key ROTADA del recurso
            'chat',
            1)
WHEN MATCHED THEN
    UPDATE SET Name = 'Claude Opus 4.8 (Azure AI Foundry)',
               Provider = 'Anthropic',
               EndpointUrl = 'https://yvall-mr1pokp6-eastus2.services.ai.azure.com/anthropic',
               IsActive = 1;

-- 2) Agente configurable que usa esa conexión (prompt/modelo/tools/skills se ajustan luego por pantalla)
MERGE dbo.Agent AS t
USING (SELECT 'AGENTE_CLAUDE' AS Code) AS s ON t.Code = s.Code
WHEN NOT MATCHED THEN
    INSERT (Code, ConfigCode, Name, Description, ModelId, SystemPrompt, ToolChoice, MaxTokens, ThinkingMode, IsActive, VersionNumber)
    VALUES ('AGENTE_CLAUDE',
            'CLAUDE_FOUNDRY',
            'Analista de sobres (Claude)',
            'Agente especialista que evalua el sobre segun el plan del cliente.',
            'claude-opus-4-8',
            'Eres un analista medico-administrativo de Saludsa. Evalua el sobre/documentos segun el plan del cliente: revisa preexistencias, coberturas del plan y deducibles usando las herramientas disponibles. Se preciso y cita la fuente.',
            'auto',
            8000,
            'adaptive',
            1,
            1)
WHEN MATCHED THEN
    UPDATE SET ConfigCode = 'CLAUDE_FOUNDRY', ModelId = 'claude-opus-4-8', ToolChoice = 'auto', IsActive = 1;

COMMIT TRAN;

-- Para exponer tools/skills al agente: vincular filas en OPAIModelTool (ToolCode del seed Req019c) /
-- OPAIModelSkill al ModelCode 'AGENTE_CLAUDE'. Ej. (descomentar y ajustar):
-- INSERT INTO dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled) VALUES ('AGENTE_CLAUDE','resolver_contrato_por_cedula',1,1);
-- INSERT INTO dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled) VALUES ('AGENTE_CLAUDE','consultar_preexistencias_por_cedula',2,1);
