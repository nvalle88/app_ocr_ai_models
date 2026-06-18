-- ============================================================
-- MIGRACIÓN: soporte Claude AI + clonado de procesos
-- BD: db-nexus-test
-- Versión: 0001
-- Fecha: 2026-06-17
-- Autor: DBA DeveloperAI / REQ-019 T1 re-baseline
-- ADITIVO — no modifica ni elimina columnas existentes
-- ============================================================

-- SALVAGUARDA: solo permitido en la BD de pruebas
IF DB_NAME() <> N'db-nexus-test'
BEGIN
    RAISERROR('Script REQ-019 solo permitido en db-nexus-test. Abortado.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
BEGIN TRANSACTION;

-- ============================================================
-- BLOQUE 1: EXTENDER OPAIConfiguration
-- ============================================================

-- + Provider varchar(30) NOT NULL DEFAULT 'AzureOpenAI'
IF COL_LENGTH('dbo.OPAIConfiguration', 'Provider') IS NULL
BEGIN
    ALTER TABLE dbo.OPAIConfiguration
        ADD [Provider] varchar(30) NOT NULL
            CONSTRAINT DF_OPAIConfiguration_Provider DEFAULT 'AzureOpenAI';
    PRINT 'OPAIConfiguration.Provider added.';
END;

-- + SecretRef varchar(250) NULL (referencia Key Vault)
IF COL_LENGTH('dbo.OPAIConfiguration', 'SecretRef') IS NULL
BEGIN
    ALTER TABLE dbo.OPAIConfiguration
        ADD [SecretRef] varchar(250) NULL;
    PRINT 'OPAIConfiguration.SecretRef added.';
END;

-- ============================================================
-- BLOQUE 2: EXTENDER Agent
-- ============================================================

IF COL_LENGTH('dbo.Agent', 'ModelId') IS NULL
BEGIN
    ALTER TABLE dbo.Agent
        ADD [ModelId] varchar(100) NULL;
    PRINT 'Agent.ModelId added.';
END;

IF COL_LENGTH('dbo.Agent', 'SystemPrompt') IS NULL
BEGIN
    ALTER TABLE dbo.Agent
        ADD [SystemPrompt] nvarchar(max) NULL;
    PRINT 'Agent.SystemPrompt added.';
END;

IF COL_LENGTH('dbo.Agent', 'MaxTokens') IS NULL
BEGIN
    ALTER TABLE dbo.Agent
        ADD [MaxTokens] int NULL;
    PRINT 'Agent.MaxTokens added.';
END;

IF COL_LENGTH('dbo.Agent', 'ThinkingMode') IS NULL
BEGIN
    ALTER TABLE dbo.Agent
        ADD [ThinkingMode] varchar(20) NULL;
    -- Valores válidos: 'adaptive' | 'disabled' | 'enabled'
    PRINT 'Agent.ThinkingMode added.';
END;

IF COL_LENGTH('dbo.Agent', 'Effort') IS NULL
BEGIN
    ALTER TABLE dbo.Agent
        ADD [Effort] varchar(10) NULL;
    -- Valores válidos: 'low' | 'medium' | 'high' | 'xhigh' | 'max'
    PRINT 'Agent.Effort added.';
END;

-- NOTA D3: decimal(4,2) en lugar de float para evitar imprecisión binaria
IF COL_LENGTH('dbo.Agent', 'Temperature') IS NULL
BEGIN
    ALTER TABLE dbo.Agent
        ADD [Temperature] decimal(4,2) NULL;
    PRINT 'Agent.Temperature added (decimal(4,2)).';
END;

IF COL_LENGTH('dbo.Agent', 'ToolChoice') IS NULL
BEGIN
    ALTER TABLE dbo.Agent
        ADD [ToolChoice] varchar(20) NOT NULL
            CONSTRAINT DF_Agent_ToolChoice DEFAULT 'auto';
    -- Valores válidos: 'auto' | 'any' | 'none' | 'tool'
    PRINT 'Agent.ToolChoice added.';
END;

-- ============================================================
-- BLOQUE 3: EXTENDER Usage
-- RÉGIMEN DOBLE (D1/D5): se agrega ExecutionId nullable
-- y se CONSERVA FinalResponseResultId para el flujo A-HOSP legacy
-- ============================================================

IF COL_LENGTH('dbo.Usage', 'ExecutionId') IS NULL
BEGIN
    ALTER TABLE dbo.Usage
        ADD [ExecutionId] bigint NULL;
    PRINT 'Usage.ExecutionId added (FK nullable a StepExecution).';
END;

-- FK a StepExecution (solo si la tabla existe y la FK no existe aún)
IF OBJECT_ID('dbo.StepExecution', 'U') IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = 'FK_Usage_StepExecution'
          AND parent_object_id = OBJECT_ID('dbo.Usage')
    )
BEGIN
    ALTER TABLE dbo.Usage
        ADD CONSTRAINT FK_Usage_StepExecution
        FOREIGN KEY ([ExecutionId]) REFERENCES dbo.StepExecution ([ExecutionId]);
    PRINT 'FK_Usage_StepExecution created.';
END;

IF COL_LENGTH('dbo.Usage', 'ThinkingTokens') IS NULL
BEGIN
    ALTER TABLE dbo.Usage
        ADD [ThinkingTokens] int NULL;
    PRINT 'Usage.ThinkingTokens added.';
END;

IF COL_LENGTH('dbo.Usage', 'CacheReadTokens') IS NULL
BEGIN
    ALTER TABLE dbo.Usage
        ADD [CacheReadTokens] int NULL;
    PRINT 'Usage.CacheReadTokens added.';
END;

IF COL_LENGTH('dbo.Usage', 'CacheCreationTokens') IS NULL
BEGIN
    ALTER TABLE dbo.Usage
        ADD [CacheCreationTokens] int NULL;
    PRINT 'Usage.CacheCreationTokens added.';
END;

-- ============================================================
-- BLOQUE 4: EXTENDER Process
-- (Code varchar(30), Name varchar(200), Description nvarchar(max))
-- ============================================================

-- NOTA D5: FK auto-referencial nullable
IF COL_LENGTH('dbo.Process', 'ClonedFromCode') IS NULL
BEGIN
    ALTER TABLE dbo.Process
        ADD [ClonedFromCode] varchar(30) NULL;
    PRINT 'Process.ClonedFromCode added.';
END;

-- Añadir FK solo si no existe
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = 'FK_Process_ClonedFrom' AND parent_object_id = OBJECT_ID('dbo.Process')
)
BEGIN
    ALTER TABLE dbo.Process
        ADD CONSTRAINT FK_Process_ClonedFrom
        FOREIGN KEY ([ClonedFromCode]) REFERENCES dbo.Process ([Code]);
    PRINT 'FK_Process_ClonedFrom created.';
END;

IF COL_LENGTH('dbo.Process', 'VersionNumber') IS NULL
BEGIN
    ALTER TABLE dbo.Process
        ADD [VersionNumber] int NOT NULL
            CONSTRAINT DF_Process_VersionNumber DEFAULT 1;
    PRINT 'Process.VersionNumber added.';
END;

IF COL_LENGTH('dbo.Process', 'IsActive') IS NULL
BEGIN
    ALTER TABLE dbo.Process
        ADD [IsActive] bit NOT NULL
            CONSTRAINT DF_Process_IsActive DEFAULT 1;
    PRINT 'Process.IsActive added.';
END;

-- Índice en Process.IsActive
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_Process_IsActive' AND object_id = OBJECT_ID('dbo.Process')
)
BEGIN
    CREATE INDEX IX_Process_IsActive ON dbo.Process ([IsActive]);
    PRINT 'IX_Process_IsActive created.';
END;

-- Índice filtrado en Process.ClonedFromCode
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_Process_ClonedFromCode' AND object_id = OBJECT_ID('dbo.Process')
)
BEGIN
    CREATE INDEX IX_Process_ClonedFromCode
        ON dbo.Process ([ClonedFromCode])
        WHERE [ClonedFromCode] IS NOT NULL;
    PRINT 'IX_Process_ClonedFromCode created.';
END;

-- ============================================================
-- BLOQUE 5: EXTENDER DataFile (Files API de Claude)
-- ============================================================

IF COL_LENGTH('dbo.DataFile', 'ClaudeFileId') IS NULL
BEGIN
    ALTER TABLE dbo.DataFile
        ADD [ClaudeFileId] varchar(100) NULL;
    PRINT 'DataFile.ClaudeFileId added.';
END;

-- ============================================================
-- BLOQUE 6: NUEVA TABLA OPAITool
-- (catálogo de herramientas; Code varchar(50) PK convención repo)
-- ============================================================

IF OBJECT_ID('dbo.OPAITool', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OPAITool (
        [Code]          varchar(50)     NOT NULL,
        [Name]          varchar(250)    NOT NULL,
        [Description]   nvarchar(500)   NULL,
        [InputSchema]   nvarchar(max)   NULL,   -- JSON Schema de los parámetros
        [Strict]        bit             NOT NULL CONSTRAINT DF_OPAITool_Strict    DEFAULT 1,
        [BindingType]   varchar(20)     NOT NULL,
        -- Valores: 'InternalApi' | 'Sql' | 'Zendesk' | 'Static'
        [BindingConfig] nvarchar(max)   NULL,   -- JSON de configuración del binding
        [IsActive]      bit             NOT NULL CONSTRAINT DF_OPAITool_IsActive  DEFAULT 1,
        [VersionNumber] int             NOT NULL CONSTRAINT DF_OPAITool_Version   DEFAULT 1,
        [CreatedDate]   datetime        NOT NULL CONSTRAINT DF_OPAITool_Created   DEFAULT (sysutcdatetime()),
        CONSTRAINT PK_OPAITool PRIMARY KEY CLUSTERED ([Code])
    );

    CREATE INDEX IX_OPAITool_IsActive ON dbo.OPAITool ([IsActive]);

    PRINT 'Table OPAITool created.';
END;

-- ============================================================
-- BLOQUE 7: NUEVA TABLA OPAIModelTool
-- (N:M Agent ↔ OPAITool)
-- NOTA D6: ModelCode varchar(50) — Agent.Code es varchar(50)
-- NOTA D11: columna de orden se llama [Order] (T-SQL reservado → corchetes);
--           en C# la propiedad se llama SortOrder y se mapea via HasColumnName("Order")
-- ============================================================

IF OBJECT_ID('dbo.OPAIModelTool', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OPAIModelTool (
        [ModelCode]  varchar(50) NOT NULL,
        [ToolCode]   varchar(50) NOT NULL,
        [Order]      int         NOT NULL CONSTRAINT DF_OPAIModelTool_Order     DEFAULT 0,
        [IsEnabled]  bit         NOT NULL CONSTRAINT DF_OPAIModelTool_IsEnabled DEFAULT 1,
        CONSTRAINT PK_OPAIModelTool PRIMARY KEY CLUSTERED ([ModelCode], [ToolCode]),
        CONSTRAINT FK_OPAIModelTool_Agent
            FOREIGN KEY ([ModelCode]) REFERENCES dbo.Agent ([Code]),
        CONSTRAINT FK_OPAIModelTool_Tool
            FOREIGN KEY ([ToolCode])  REFERENCES dbo.OPAITool ([Code])
    );

    -- Índice para consultas por herramienta (buscar qué agentes usan una tool)
    CREATE INDEX IX_OPAIModelTool_ToolCode ON dbo.OPAIModelTool ([ToolCode]);

    PRINT 'Table OPAIModelTool created.';
END;

-- ============================================================
-- BLOQUE 8: NUEVA TABLA OPAISkill
-- NOTA D12: se añade VersionNumber para consistencia con catálogos
-- ============================================================

IF OBJECT_ID('dbo.OPAISkill', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OPAISkill (
        [Code]          varchar(50)  NOT NULL,
        [SkillType]     varchar(20)  NOT NULL,
        -- Valores: 'anthropic' | 'custom'
        [SkillId]       varchar(100) NOT NULL,   -- ID externo de la skill (p.ej. en Anthropic)
        [SkillVersion]  varchar(20)  NULL,
        [Name]          varchar(250) NOT NULL,
        [Description]   nvarchar(500) NULL,
        [IsActive]      bit          NOT NULL CONSTRAINT DF_OPAISkill_IsActive  DEFAULT 1,
        [VersionNumber] int          NOT NULL CONSTRAINT DF_OPAISkill_Version   DEFAULT 1,
        [CreatedDate]   datetime     NOT NULL CONSTRAINT DF_OPAISkill_Created   DEFAULT (sysutcdatetime()),
        CONSTRAINT PK_OPAISkill PRIMARY KEY CLUSTERED ([Code])
    );

    PRINT 'Table OPAISkill created.';
END;

-- ============================================================
-- BLOQUE 9: NUEVA TABLA OPAIModelSkill
-- (N:M Agent ↔ OPAISkill)
-- NOTA D7: ModelCode varchar(50) — Agent.Code es varchar(50)
-- ============================================================

IF OBJECT_ID('dbo.OPAIModelSkill', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OPAIModelSkill (
        [ModelCode]  varchar(50) NOT NULL,
        [SkillCode]  varchar(50) NOT NULL,
        [Order]      int         NOT NULL CONSTRAINT DF_OPAIModelSkill_Order     DEFAULT 0,
        [IsEnabled]  bit         NOT NULL CONSTRAINT DF_OPAIModelSkill_IsEnabled DEFAULT 1,
        CONSTRAINT PK_OPAIModelSkill PRIMARY KEY CLUSTERED ([ModelCode], [SkillCode]),
        CONSTRAINT FK_OPAIModelSkill_Agent
            FOREIGN KEY ([ModelCode]) REFERENCES dbo.Agent ([Code]),
        CONSTRAINT FK_OPAIModelSkill_Skill
            FOREIGN KEY ([SkillCode]) REFERENCES dbo.OPAISkill ([Code])
    );

    CREATE INDEX IX_OPAIModelSkill_SkillCode ON dbo.OPAIModelSkill ([SkillCode]);

    PRINT 'Table OPAIModelSkill created.';
END;

-- ============================================================
-- BLOQUE 10: NUEVA TABLA ToolInvocation
-- NOTA D8: ExecutionId es bigint — StepExecution.ExecutionId es bigint (C# long)
-- ============================================================

IF OBJECT_ID('dbo.ToolInvocation', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ToolInvocation (
        [InvocationId]  bigint          NOT NULL IDENTITY(1,1),
        [ExecutionId]   bigint          NOT NULL,           -- FK → StepExecution.ExecutionId
        [ToolCode]      varchar(50)     NOT NULL,           -- FK → OPAITool.Code (soft ref: tool puede eliminarse lógicamente)
        [RequestJson]   nvarchar(max)   NULL,
        [ResponseJson]  nvarchar(max)   NULL,
        [IsError]       bit             NOT NULL CONSTRAINT DF_ToolInvocation_IsError DEFAULT 0,
        [StartDate]     datetime        NOT NULL CONSTRAINT DF_ToolInvocation_Start   DEFAULT (sysutcdatetime()),
        [EndDate]       datetime        NULL,
        CONSTRAINT PK_ToolInvocation PRIMARY KEY CLUSTERED ([InvocationId]),
        CONSTRAINT FK_ToolInvocation_Execution
            FOREIGN KEY ([ExecutionId]) REFERENCES dbo.StepExecution ([ExecutionId])
        -- ToolCode: referencia blanda a OPAITool (sin FK formal)
        -- para permitir auditoría aunque la tool se desactive/renombre
    );

    -- Índice cubriente para join StepExecution → invocaciones
    CREATE INDEX IX_ToolInvocation_ExecutionId
        ON dbo.ToolInvocation ([ExecutionId])
        INCLUDE ([ToolCode], [IsError]);

    -- Índice para análisis de rendimiento por herramienta
    CREATE INDEX IX_ToolInvocation_ToolCode_StartDate
        ON dbo.ToolInvocation ([ToolCode], [StartDate] DESC);

    PRINT 'Table ToolInvocation created.';
END;

COMMIT TRANSACTION;
PRINT 'REQ-019 Req019_ModeloClaude.sql (0001) completed successfully.';
GO

-- ============================================================
-- sp_ClonarProceso
-- Clon superficial: duplica Process + ProcessStep + FinalResponseConfig
-- Clon profundo (@deep=1): también duplica Agent con sus links
--   OPAIModelPrompt, OPAIModelTool, OPAIModelSkill.
-- Los catálogos OPAITool, OPAISkill, OPAIPrompt NO se duplican.
-- El nuevo Code de agentes clonados = original + '_' + @nuevoCode
-- para evitar colisiones y mantener trazabilidad.
-- ============================================================

CREATE OR ALTER PROCEDURE dbo.sp_ClonarProceso
    @origen    varchar(30),
    @nuevoCode varchar(30),
    @deep      bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Validaciones previas
    IF NOT EXISTS (SELECT 1 FROM dbo.Process WHERE Code = @origen)
        THROW 50001, 'El proceso origen no existe.', 1;

    IF EXISTS (SELECT 1 FROM dbo.Process WHERE Code = @nuevoCode)
        THROW 50002, 'Ya existe un proceso con el código destino.', 1;

    IF LEN(@nuevoCode) > 30
        THROW 50003, 'El nuevo código no puede superar 30 caracteres.', 1;

    BEGIN TRANSACTION;

    -- --------------------------------------------------------
    -- 1. Clonar Process
    -- --------------------------------------------------------
    INSERT INTO dbo.Process (Code, Name, Description, ClonedFromCode, VersionNumber, IsActive)
    SELECT
        @nuevoCode,
        Name + ' (clon)',
        Description,
        @origen,
        1,
        1
    FROM dbo.Process
    WHERE Code = @origen;

    -- --------------------------------------------------------
    -- 2. Clonar ProcessStep
    --    Si @deep=0: reutiliza los mismos ModelCode (Agent)
    --    Si @deep=1: los ModelCode se reemplazarán después
    -- --------------------------------------------------------
    INSERT INTO dbo.ProcessStep
        (ProcessCode, StepOrder, ModelCode, StepName, StepsToInclude, SourceType, AggregateExecution)
    SELECT
        @nuevoCode,
        StepOrder,
        ModelCode,   -- se actualizará a continuación si @deep=1
        StepName,
        StepsToInclude,
        SourceType,
        AggregateExecution
    FROM dbo.ProcessStep
    WHERE ProcessCode = @origen;

    -- --------------------------------------------------------
    -- 3. Clonar FinalResponseConfig
    --    ConfigCode = original + '_' + @nuevoCode (truncado a 50)
    -- --------------------------------------------------------
    INSERT INTO dbo.FinalResponseConfig
        (ConfigCode, ProcessCode, AgentCode, PromptTemplate,
         IncludedStepOrders, UseOriginalText, IncludeStepNames,
         IncludeFileCount, IsEnabled, MetadataJson)
    SELECT
        LEFT(ConfigCode + '_' + @nuevoCode, 50),
        @nuevoCode,
        AgentCode,   -- se actualizará si @deep=1
        PromptTemplate,
        IncludedStepOrders,
        UseOriginalText,
        IncludeStepNames,
        IncludeFileCount,
        IsEnabled,
        MetadataJson
    FROM dbo.FinalResponseConfig
    WHERE ProcessCode = @origen;

    -- --------------------------------------------------------
    -- 4. Clon profundo: duplicar Agents + sus links
    -- --------------------------------------------------------
    IF @deep = 1
    BEGIN
        -- Tabla temporal para mapear Agent original → Agent clon
        CREATE TABLE #AgentMap (
            OriginalCode varchar(50) NOT NULL,
            ClonedCode   varchar(50) NOT NULL
        );

        -- Obtener los agentes distintos usados en los pasos del proceso origen
        -- + el agente de FinalResponseConfig
        INSERT INTO #AgentMap (OriginalCode, ClonedCode)
        SELECT DISTINCT
            a.Code,
            LEFT(a.Code + '_' + @nuevoCode, 50) AS ClonedCode
        FROM dbo.Agent a
        WHERE a.Code IN (
            SELECT ModelCode FROM dbo.ProcessStep WHERE ProcessCode = @origen
            UNION
            SELECT AgentCode FROM dbo.FinalResponseConfig WHERE ProcessCode = @origen
        );

        -- Validar que los nuevos códigos no colisionen
        IF EXISTS (
            SELECT 1 FROM #AgentMap m
            JOIN dbo.Agent a ON a.Code = m.ClonedCode
        )
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50004, 'Colisión de código en Agent al clonar. Elija un @nuevoCode más corto o único.', 1;
        END;

        -- Clonar Agents
        INSERT INTO dbo.Agent
            (Code, ConfigCode, Name, VersionNumber, Description,
             CreatedDate, ModifiedDate, IsActive,
             ModelId, SystemPrompt, MaxTokens, ThinkingMode,
             Effort, Temperature, ToolChoice)
        SELECT
            m.ClonedCode,
            a.ConfigCode,
            a.Name + ' (' + @nuevoCode + ')',
            1,
            a.Description,
            sysutcdatetime(),
            sysutcdatetime(),
            a.IsActive,
            a.ModelId,
            a.SystemPrompt,
            a.MaxTokens,
            a.ThinkingMode,
            a.Effort,
            a.Temperature,
            a.ToolChoice
        FROM dbo.Agent a
        JOIN #AgentMap m ON m.OriginalCode = a.Code;

        -- Clonar OPAIModelPrompt (reutiliza OPAIPrompt — catálogo compartido)
        INSERT INTO dbo.OPAIModelPrompt (ModelCode, PromptCode, [Order])
        SELECT m.ClonedCode, mp.PromptCode, mp.[Order]
        FROM dbo.OPAIModelPrompt mp
        JOIN #AgentMap m ON m.OriginalCode = mp.ModelCode;

        -- Clonar OPAIModelTool (reutiliza OPAITool — catálogo compartido)
        INSERT INTO dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
        SELECT m.ClonedCode, mt.ToolCode, mt.[Order], mt.IsEnabled
        FROM dbo.OPAIModelTool mt
        JOIN #AgentMap m ON m.OriginalCode = mt.ModelCode;

        -- Clonar OPAIModelSkill (reutiliza OPAISkill — catálogo compartido)
        INSERT INTO dbo.OPAIModelSkill (ModelCode, SkillCode, [Order], IsEnabled)
        SELECT m.ClonedCode, ms.SkillCode, ms.[Order], ms.IsEnabled
        FROM dbo.OPAIModelSkill ms
        JOIN #AgentMap m ON m.OriginalCode = ms.ModelCode;

        -- Redirigir ProcessStep del nuevo proceso a los Agents clonados
        UPDATE ps
        SET ps.ModelCode = m.ClonedCode
        FROM dbo.ProcessStep ps
        JOIN #AgentMap m ON m.OriginalCode = ps.ModelCode
        WHERE ps.ProcessCode = @nuevoCode;

        -- Redirigir FinalResponseConfig del nuevo proceso a los Agents clonados
        UPDATE frc
        SET frc.AgentCode = m.ClonedCode
        FROM dbo.FinalResponseConfig frc
        JOIN #AgentMap m ON m.OriginalCode = frc.AgentCode
        WHERE frc.ProcessCode = @nuevoCode;

        DROP TABLE #AgentMap;
    END;

    COMMIT TRANSACTION;

    -- Devolver resumen
    SELECT
        p.Code,
        p.Name,
        p.ClonedFromCode,
        p.VersionNumber,
        (SELECT COUNT(*) FROM dbo.ProcessStep  WHERE ProcessCode = p.Code) AS StepCount,
        (SELECT COUNT(*) FROM dbo.FinalResponseConfig WHERE ProcessCode = p.Code) AS FinalConfigCount
    FROM dbo.Process p
    WHERE p.Code = @nuevoCode;
END;
GO
