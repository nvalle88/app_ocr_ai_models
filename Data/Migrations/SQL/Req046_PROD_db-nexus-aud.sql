-- ============================================================
-- REQ-046 - MIGRACION PRODUCCION nexus-aud (db-nexus-aud). CONSOLIDADO.
-- App: web-nexus-aud (gr-op-po-prod). Ejecuta el DBA en su ventana.
-- Guarda: solo corre en 'db-nexus-aud'. Idempotente.
-- Requiere AGENTE_AUDITOR_MEDICINA + esquema REQ-019 (verificado presente).
-- ============================================================
GO
-- ##### origen: Req046a_BibliotecaAnexos.sql #####
-- ============================================================
-- REQ-046a â€” Biblioteca de ANEXOS estructurada (AuditorÃ­a de Casos)
--
-- ADITIVO. No toca ninguna tabla existente. Crea el maestro propio de
-- Nexus donde vive, estructurado, el contrato base por tipo y su ANEXO por
-- plan con las condiciones parametrizadas (coberturas, topes, deducibles,
-- carencias, exclusiones, clÃ¡usulas). El alta se hace por OCRâ†’Claudeâ†’BD desde
-- la pantalla de administraciÃ³n; las tools del agente auditor LEEN de aquÃ­
-- (no de M-Files ni de las BD de Saludsa en vivo).
--
-- Todo en la BD propia de Nexus (db-nexus-aud / OcrAiConnection).
-- Idempotente: se puede re-ejecutar.
-- ============================================================
IF DB_NAME() <> N'db-nexus-aud'
BEGIN
    RAISERROR('Script REQ-046a solo permitido en db-nexus-aud. Abortado.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

-- â”€â”€ 1. Contrato base (por tipo: Individual, Tradicional, Optimus Plus, OncolÃ³gicoâ€¦) â”€â”€
IF OBJECT_ID('dbo.AnexoContrato', 'U') IS NULL
CREATE TABLE dbo.AnexoContrato
(
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AnexoContrato PRIMARY KEY,
    Tipo         NVARCHAR(60)  NOT NULL,          -- Individual | Tradicional | OptimusPlus | Oncologico | Corporativo
    CodigoAcess  NVARCHAR(60)  NULL,              -- p.ej. 025-CI-012
    Nombre       NVARCHAR(200) NOT NULL,
    Version      NVARCHAR(40)  NULL,
    Vigencia     NVARCHAR(100) NULL,
    ArchivoUri   NVARCHAR(500) NULL,              -- PDF fuente en Blob
    IsActive     BIT           NOT NULL CONSTRAINT DF_AnexoContrato_IsActive DEFAULT (1),
    CreatedBy    NVARCHAR(150) NULL,
    CreatedDate  DATETIME      NOT NULL CONSTRAINT DF_AnexoContrato_Created DEFAULT (GETUTCDATE()),
    ModifiedDate DATETIME      NULL
);

-- â”€â”€ 2. Anexo (por plan/producto, con las condiciones parametrizadas) â”€â”€
IF OBJECT_ID('dbo.Anexo', 'U') IS NULL
CREATE TABLE dbo.Anexo
(
    Id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Anexo PRIMARY KEY,
    ContratoId         INT           NULL CONSTRAINT FK_Anexo_Contrato REFERENCES dbo.AnexoContrato(Id),
    CodigoPlan         NVARCHAR(40)  NOT NULL,     -- enlaza con el plan del cliente (Cl04Contratos.CodigoPlan)
    NombrePlan         NVARCHAR(200) NULL,         -- Pro150K, Sky70K Plus Lite, Star 30â€¦
    CodigoProducto     NVARCHAR(20)  NULL,         -- IND, COR, ONC, XPRâ€¦
    Version            NVARCHAR(40)  NULL,
    ArchivoUri         NVARCHAR(500) NULL,         -- PDF del anexo en Blob
    ResumenCondiciones NVARCHAR(MAX) NULL,         -- resumen legible de las condiciones
    Estado             NVARCHAR(30)  NOT NULL CONSTRAINT DF_Anexo_Estado DEFAULT (N'ACTIVO'),  -- BORRADOR | ACTIVO
    IsActive           BIT           NOT NULL CONSTRAINT DF_Anexo_IsActive DEFAULT (1),
    CreatedBy          NVARCHAR(150) NULL,
    CreatedDate        DATETIME      NOT NULL CONSTRAINT DF_Anexo_Created DEFAULT (GETUTCDATE()),
    ModifiedDate       DATETIME      NULL
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Anexo_Plan' AND object_id = OBJECT_ID('dbo.Anexo'))
    CREATE INDEX IX_Anexo_Plan ON dbo.Anexo (CodigoPlan, CodigoProducto) WHERE IsActive = 1;

-- â”€â”€ 3. Coberturas del anexo (beneficio â†’ %, tope, deducible, copago) â”€â”€
IF OBJECT_ID('dbo.AnexoCobertura', 'U') IS NULL
CREATE TABLE dbo.AnexoCobertura
(
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AnexoCobertura PRIMARY KEY,
    AnexoId         INT           NOT NULL CONSTRAINT FK_AnexoCobertura_Anexo REFERENCES dbo.Anexo(Id),
    Beneficio       NVARCHAR(200) NOT NULL,        -- Hospitalario, Ambulatorio, Medicina, Maternidad, Emergenciaâ€¦
    CodigoBeneficio NVARCHAR(20)  NULL,
    Porcentaje      DECIMAL(5,2)  NULL,            -- % de cobertura
    Tope            DECIMAL(18,2) NULL,            -- monto mÃ¡ximo
    MonedaTope      NVARCHAR(10)  NULL,
    Deducible       DECIMAL(18,2) NULL,
    Copago          NVARCHAR(100) NULL,            -- puede ser % o monto o texto
    Periodo         NVARCHAR(60)  NULL,            -- anual | por evento | vitalicio
    Ambito          NVARCHAR(60)  NULL,            -- nacional | internacional | red
    Notas           NVARCHAR(500) NULL,
    CreatedDate     DATETIME      NOT NULL CONSTRAINT DF_AnexoCobertura_Created DEFAULT (GETUTCDATE())
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AnexoCobertura_Anexo' AND object_id = OBJECT_ID('dbo.AnexoCobertura'))
    CREATE INDEX IX_AnexoCobertura_Anexo ON dbo.AnexoCobertura (AnexoId);

-- â”€â”€ 4. Carencias del anexo â”€â”€
IF OBJECT_ID('dbo.AnexoCarencia', 'U') IS NULL
CREATE TABLE dbo.AnexoCarencia
(
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AnexoCarencia PRIMARY KEY,
    AnexoId      INT           NOT NULL CONSTRAINT FK_AnexoCarencia_Anexo REFERENCES dbo.Anexo(Id),
    Beneficio    NVARCHAR(200) NOT NULL,
    DiasCarencia INT           NULL,
    Notas        NVARCHAR(500) NULL,
    CreatedDate  DATETIME      NOT NULL CONSTRAINT DF_AnexoCarencia_Created DEFAULT (GETUTCDATE())
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AnexoCarencia_Anexo' AND object_id = OBJECT_ID('dbo.AnexoCarencia'))
    CREATE INDEX IX_AnexoCarencia_Anexo ON dbo.AnexoCarencia (AnexoId);

-- â”€â”€ 5. Exclusiones (del anexo o del contrato base) â”€â”€
IF OBJECT_ID('dbo.AnexoExclusion', 'U') IS NULL
CREATE TABLE dbo.AnexoExclusion
(
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AnexoExclusion PRIMARY KEY,
    AnexoId     INT            NULL CONSTRAINT FK_AnexoExclusion_Anexo REFERENCES dbo.Anexo(Id),
    ContratoId  INT            NULL CONSTRAINT FK_AnexoExclusion_Contrato REFERENCES dbo.AnexoContrato(Id),
    Texto       NVARCHAR(1000) NOT NULL,
    ClausulaRef NVARCHAR(200)  NULL,
    CreatedDate DATETIME       NOT NULL CONSTRAINT DF_AnexoExclusion_Created DEFAULT (GETUTCDATE())
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AnexoExclusion_Anexo' AND object_id = OBJECT_ID('dbo.AnexoExclusion'))
    CREATE INDEX IX_AnexoExclusion_Anexo ON dbo.AnexoExclusion (AnexoId);

-- â”€â”€ 6. ClÃ¡usulas del contrato base (para citar clÃ¡usula/numeral/literal exacto) â”€â”€
IF OBJECT_ID('dbo.AnexoClausula', 'U') IS NULL
CREATE TABLE dbo.AnexoClausula
(
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AnexoClausula PRIMARY KEY,
    ContratoId  INT           NOT NULL CONSTRAINT FK_AnexoClausula_Contrato REFERENCES dbo.AnexoContrato(Id),
    Ordinal     NVARCHAR(60)  NULL,               -- DÃ©cima, DÃ©cima Primeraâ€¦
    Numeral     NVARCHAR(20)  NULL,
    Literal     NVARCHAR(20)  NULL,
    Titulo      NVARCHAR(200) NULL,
    Texto       NVARCHAR(MAX) NOT NULL,
    CreatedDate DATETIME      NOT NULL CONSTRAINT DF_AnexoClausula_Created DEFAULT (GETUTCDATE())
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AnexoClausula_Contrato' AND object_id = OBJECT_ID('dbo.AnexoClausula'))
    CREATE INDEX IX_AnexoClausula_Contrato ON dbo.AnexoClausula (ContratoId);

-- â”€â”€ 7. Traza de ingesta (cada carga: OCR â†’ estructurado â†’ guardado) â”€â”€
IF OBJECT_ID('dbo.AnexoIngesta', 'U') IS NULL
CREATE TABLE dbo.AnexoIngesta
(
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AnexoIngesta PRIMARY KEY,
    ContratoId   INT           NULL CONSTRAINT FK_AnexoIngesta_Contrato REFERENCES dbo.AnexoContrato(Id),
    AnexoId      INT           NULL CONSTRAINT FK_AnexoIngesta_Anexo REFERENCES dbo.Anexo(Id),
    Archivo      NVARCHAR(300) NOT NULL,
    ArchivoUri   NVARCHAR(500) NULL,
    Estado       NVARCHAR(30)  NOT NULL,           -- RECIBIDO | OCR_OK | ESTRUCTURADO | GUARDADO | ERROR
    Mensaje      NVARCHAR(MAX) NULL,               -- error o nota
    TextoOcr     NVARCHAR(MAX) NULL,               -- texto crudo del OCR
    JsonExtraido NVARCHAR(MAX) NULL,               -- JSON estructurado que devolviÃ³ Claude
    CreatedBy    NVARCHAR(150) NULL,
    CreatedDate  DATETIME      NOT NULL CONSTRAINT DF_AnexoIngesta_Created DEFAULT (GETUTCDATE())
);

-- â”€â”€ VerificaciÃ³n â”€â”€
SELECT 'AnexoContrato'  AS Tabla, COUNT(*) AS Filas FROM dbo.AnexoContrato
UNION ALL SELECT 'Anexo',           COUNT(*) FROM dbo.Anexo
UNION ALL SELECT 'AnexoCobertura',  COUNT(*) FROM dbo.AnexoCobertura
UNION ALL SELECT 'AnexoCarencia',   COUNT(*) FROM dbo.AnexoCarencia
UNION ALL SELECT 'AnexoExclusion',  COUNT(*) FROM dbo.AnexoExclusion
UNION ALL SELECT 'AnexoClausula',   COUNT(*) FROM dbo.AnexoClausula
UNION ALL SELECT 'AnexoIngesta',    COUNT(*) FROM dbo.AnexoIngesta;

GO
-- ##### origen: Req046b_ToolsAnexo.sql #####
-- ============================================================
-- REQ-046b â€” Tools del agente auditor que LEEN de la biblioteca de anexos
--            (tablas propias de Nexus, connection = DefaultConnection).
--   anexo_por_plan Â· anexo_coberturas Â· anexo_carencias Â·
--   anexo_exclusiones Â· anexo_clausula
-- Se enlazan a AGENTE_AUDITOR_MEDICINA y AGENTE_CLAUDE.
-- Idempotente (upsert por Code + link condicional).
-- ============================================================
IF DB_NAME() <> N'db-nexus-aud'
BEGIN
    RAISERROR('Script REQ-046b solo permitido en db-nexus-aud. Abortado.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @code varchar(100), @desc nvarchar(500), @schema nvarchar(max), @binding nvarchar(max);

-- â”€â”€ 1) anexo_por_plan â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
SET @code = 'anexo_por_plan';
SET @desc = N'Devuelve el anexo estructurado del plan del cliente (nombre del plan, producto, versiÃ³n, resumen de condiciones y tipo de contrato base). Ãšsala primero para ubicar el anexo del caso.';
SET @schema = N'{"type":"object","properties":{"codigoPlan":{"type":"string","description":"CÃ³digo de plan del cliente (Cl04Contratos.CodigoPlan)."},"codigoProducto":{"type":"string","description":"Producto (IND, COR, ONC, XPR...). Opcional."}},"required":["codigoPlan"]}';
SET @binding = N'{"connection":"DefaultConnection","maxRows":5,"query":"SELECT TOP 5 a.Id AS AnexoId, a.CodigoPlan, a.NombrePlan, a.CodigoProducto, a.Version, a.ResumenCondiciones, c.Tipo AS TipoContrato, c.CodigoAcess, c.Nombre AS NombreContrato FROM dbo.Anexo a LEFT JOIN dbo.AnexoContrato c ON c.Id = a.ContratoId WHERE a.IsActive = 1 AND a.CodigoPlan = @codigoPlan AND (@codigoProducto IS NULL OR LEN(@codigoProducto) = 0 OR a.CodigoProducto = @codigoProducto) ORDER BY a.Version DESC"}';
IF EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Code = @code)
    UPDATE dbo.OPAITool SET Name=@code, Description=@desc, InputSchema=@schema, BindingType='Sql', BindingConfig=@binding, Strict=0, IsActive=1 WHERE Code=@code;
ELSE
    INSERT dbo.OPAITool (Code,Name,Description,InputSchema,BindingType,BindingConfig,Strict,IsActive,VersionNumber)
    VALUES (@code,@code,@desc,@schema,'Sql',@binding,0,1,1);
INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT g.Code, @code, ISNULL((SELECT MAX([Order]) FROM dbo.OPAIModelTool WHERE ModelCode=g.Code),0)+1, 1
FROM dbo.Agent g WHERE g.Code IN ('AGENTE_AUDITOR_MEDICINA','AGENTE_CLAUDE')
  AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt WHERE mt.ModelCode=g.Code AND mt.ToolCode=@code);

-- â”€â”€ 2) anexo_coberturas â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
SET @code = 'anexo_coberturas';
SET @desc = N'Coberturas del anexo del plan: por beneficio devuelve porcentaje, tope, deducible, copago, periodo y Ã¡mbito. Es la fuente para el punto de cobertura contractual.';
SET @schema = N'{"type":"object","properties":{"codigoPlan":{"type":"string","description":"CÃ³digo de plan del cliente."},"beneficio":{"type":"string","description":"Filtra por beneficio (Hospitalario, Ambulatorio, Medicina...). Opcional."}},"required":["codigoPlan"]}';
SET @binding = N'{"connection":"DefaultConnection","maxRows":80,"query":"SELECT TOP 80 co.Beneficio, co.CodigoBeneficio, co.Porcentaje, co.Tope, co.MonedaTope, co.Deducible, co.Copago, co.Periodo, co.Ambito, co.Notas FROM dbo.AnexoCobertura co JOIN dbo.Anexo a ON a.Id = co.AnexoId WHERE a.IsActive = 1 AND a.CodigoPlan = @codigoPlan AND (@beneficio IS NULL OR LEN(@beneficio) = 0 OR CHARINDEX(@beneficio, co.Beneficio) > 0) ORDER BY co.Beneficio"}';
IF EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Code = @code)
    UPDATE dbo.OPAITool SET Name=@code, Description=@desc, InputSchema=@schema, BindingType='Sql', BindingConfig=@binding, Strict=0, IsActive=1 WHERE Code=@code;
ELSE
    INSERT dbo.OPAITool (Code,Name,Description,InputSchema,BindingType,BindingConfig,Strict,IsActive,VersionNumber)
    VALUES (@code,@code,@desc,@schema,'Sql',@binding,0,1,1);
INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT g.Code, @code, ISNULL((SELECT MAX([Order]) FROM dbo.OPAIModelTool WHERE ModelCode=g.Code),0)+1, 1
FROM dbo.Agent g WHERE g.Code IN ('AGENTE_AUDITOR_MEDICINA','AGENTE_CLAUDE')
  AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt WHERE mt.ModelCode=g.Code AND mt.ToolCode=@code);

-- â”€â”€ 3) anexo_carencias â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
SET @code = 'anexo_carencias';
SET @desc = N'PerÃ­odos de carencia del anexo del plan por beneficio (dÃ­as de carencia). Fuente para el punto de carencias.';
SET @schema = N'{"type":"object","properties":{"codigoPlan":{"type":"string","description":"CÃ³digo de plan del cliente."}},"required":["codigoPlan"]}';
SET @binding = N'{"connection":"DefaultConnection","maxRows":80,"query":"SELECT TOP 80 ca.Beneficio, ca.DiasCarencia, ca.Notas FROM dbo.AnexoCarencia ca JOIN dbo.Anexo a ON a.Id = ca.AnexoId WHERE a.IsActive = 1 AND a.CodigoPlan = @codigoPlan ORDER BY ca.Beneficio"}';
IF EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Code = @code)
    UPDATE dbo.OPAITool SET Name=@code, Description=@desc, InputSchema=@schema, BindingType='Sql', BindingConfig=@binding, Strict=0, IsActive=1 WHERE Code=@code;
ELSE
    INSERT dbo.OPAITool (Code,Name,Description,InputSchema,BindingType,BindingConfig,Strict,IsActive,VersionNumber)
    VALUES (@code,@code,@desc,@schema,'Sql',@binding,0,1,1);
INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT g.Code, @code, ISNULL((SELECT MAX([Order]) FROM dbo.OPAIModelTool WHERE ModelCode=g.Code),0)+1, 1
FROM dbo.Agent g WHERE g.Code IN ('AGENTE_AUDITOR_MEDICINA','AGENTE_CLAUDE')
  AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt WHERE mt.ModelCode=g.Code AND mt.ToolCode=@code);

-- â”€â”€ 4) anexo_exclusiones â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
SET @code = 'anexo_exclusiones';
SET @desc = N'Exclusiones del anexo del plan y del contrato base asociado (texto exacto + referencia de clÃ¡usula). Una exclusiÃ³n no se interpreta por analogÃ­a: cÃ­tala textual.';
SET @schema = N'{"type":"object","properties":{"codigoPlan":{"type":"string","description":"CÃ³digo de plan del cliente."}},"required":["codigoPlan"]}';
SET @binding = N'{"connection":"DefaultConnection","maxRows":100,"query":"SELECT TOP 100 ex.Texto, ex.ClausulaRef FROM dbo.AnexoExclusion ex WHERE ex.AnexoId IN (SELECT Id FROM dbo.Anexo WHERE IsActive = 1 AND CodigoPlan = @codigoPlan) OR ex.ContratoId IN (SELECT ContratoId FROM dbo.Anexo WHERE IsActive = 1 AND CodigoPlan = @codigoPlan AND ContratoId IS NOT NULL) ORDER BY ex.Id"}';
IF EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Code = @code)
    UPDATE dbo.OPAITool SET Name=@code, Description=@desc, InputSchema=@schema, BindingType='Sql', BindingConfig=@binding, Strict=0, IsActive=1 WHERE Code=@code;
ELSE
    INSERT dbo.OPAITool (Code,Name,Description,InputSchema,BindingType,BindingConfig,Strict,IsActive,VersionNumber)
    VALUES (@code,@code,@desc,@schema,'Sql',@binding,0,1,1);
INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT g.Code, @code, ISNULL((SELECT MAX([Order]) FROM dbo.OPAIModelTool WHERE ModelCode=g.Code),0)+1, 1
FROM dbo.Agent g WHERE g.Code IN ('AGENTE_AUDITOR_MEDICINA','AGENTE_CLAUDE')
  AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt WHERE mt.ModelCode=g.Code AND mt.ToolCode=@code);

-- â”€â”€ 5) anexo_clausula â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
SET @code = 'anexo_clausula';
SET @desc = N'ClÃ¡usulas del contrato base (ordinal, numeral, literal, tÃ­tulo, texto) para citar la clÃ¡usula exacta. Filtra por tipo de contrato o por texto.';
SET @schema = N'{"type":"object","properties":{"tipoContrato":{"type":"string","description":"Individual, Tradicional, OptimusPlus, Oncologico... Opcional."},"texto":{"type":"string","description":"Texto o tema a buscar dentro de las clÃ¡usulas. Opcional."}}}';
SET @binding = N'{"connection":"DefaultConnection","maxRows":25,"query":"SELECT TOP 25 c.Tipo AS TipoContrato, cl.Ordinal, cl.Numeral, cl.Literal, cl.Titulo, cl.Texto FROM dbo.AnexoClausula cl JOIN dbo.AnexoContrato c ON c.Id = cl.ContratoId WHERE (@tipoContrato IS NULL OR LEN(@tipoContrato) = 0 OR c.Tipo = @tipoContrato) AND (@texto IS NULL OR LEN(@texto) = 0 OR CHARINDEX(@texto, cl.Texto) > 0 OR CHARINDEX(@texto, cl.Titulo) > 0) ORDER BY cl.Id"}';
IF EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Code = @code)
    UPDATE dbo.OPAITool SET Name=@code, Description=@desc, InputSchema=@schema, BindingType='Sql', BindingConfig=@binding, Strict=0, IsActive=1 WHERE Code=@code;
ELSE
    INSERT dbo.OPAITool (Code,Name,Description,InputSchema,BindingType,BindingConfig,Strict,IsActive,VersionNumber)
    VALUES (@code,@code,@desc,@schema,'Sql',@binding,0,1,1);
INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT g.Code, @code, ISNULL((SELECT MAX([Order]) FROM dbo.OPAIModelTool WHERE ModelCode=g.Code),0)+1, 1
FROM dbo.Agent g WHERE g.Code IN ('AGENTE_AUDITOR_MEDICINA','AGENTE_CLAUDE')
  AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt WHERE mt.ModelCode=g.Code AND mt.ToolCode=@code);

-- â”€â”€ VerificaciÃ³n â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
SELECT t.Code, t.BindingType, t.IsActive,
       (SELECT COUNT(*) FROM dbo.OPAIModelTool mt WHERE mt.ToolCode = t.Code AND mt.IsEnabled = 1) AS Agentes
FROM dbo.OPAITool t
WHERE t.Code IN ('anexo_por_plan','anexo_coberturas','anexo_carencias','anexo_exclusiones','anexo_clausula')
ORDER BY t.Code;

GO
-- ##### origen: Req046c_PromptAuditorAnexos.sql #####
-- ============================================================
-- REQ-046c â€” El agente auditor aprende a usar la biblioteca de anexos.
--   Anexa (idempotente) al SystemPrompt de AGENTE_AUDITOR_MEDICINA la guÃ­a de
--   las tools anexo_* y las reglas de prelaciÃ³n contractual.
-- ============================================================
IF DB_NAME() <> N'db-nexus-aud'
BEGIN
    RAISERROR('Script REQ-046c solo permitido en db-nexus-aud. Abortado.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @bloque nvarchar(max) = N'

=== BIBLIOTECA DE ANEXOS (fuente contractual estructurada de Saludsa) ===
Para el ANÃLISIS CONTRACTUAL usa SIEMPRE estas herramientas, que leen las condiciones del plan ya estructuradas en nuestra base (no en documentos sueltos):
- anexo_por_plan(codigoPlan[, codigoProducto]): ubica el anexo del plan del cliente y su contrato base. Empieza por aquÃ­.
- anexo_coberturas(codigoPlan[, beneficio]): porcentaje de cobertura, tope, deducible, copago, periodo y Ã¡mbito por beneficio.
- anexo_carencias(codigoPlan): dÃ­as de carencia por beneficio.
- anexo_exclusiones(codigoPlan): exclusiones del plan y del contrato base. CÃ­talas TEXTUAL; una exclusiÃ³n NO se interpreta por analogÃ­a.
- anexo_clausula(tipoContrato[, texto]): clÃ¡usula, numeral y literal exactos para citar (formato: ClÃ¡usula X, numeral Y, literal Z).

El codigoPlan del cliente viene en el contexto del caso; si no consta, indÃ­calo como dato faltante y no lo inventes.

Reglas de prelaciÃ³n (no negociables): para pagar se requieren AMBAS, pertinencia mÃ©dica Y cobertura contractual. Ante conflicto de interpretaciÃ³n PRIMA EL CONTRATO; dentro del contrato PRIMA LA TABLA DEL ANEXO cuando contempla el escenario especÃ­fico. Si una condiciÃ³n (cobertura, tope, deducible, carencia, exclusiÃ³n o clÃ¡usula) no aparece en la biblioteca de anexos, escrÃ­belo como "no localizada en la biblioteca de anexos â€” verificar"; nunca inventes nÃºmeros de clÃ¡usula ni textos contractuales.';

IF EXISTS (SELECT 1 FROM dbo.Agent WHERE Code = 'AGENTE_AUDITOR_MEDICINA')
   AND NOT EXISTS (SELECT 1 FROM dbo.Agent WHERE Code = 'AGENTE_AUDITOR_MEDICINA' AND SystemPrompt LIKE '%anexo_coberturas%')
BEGIN
    UPDATE dbo.Agent
    SET SystemPrompt = ISNULL(SystemPrompt, N'') + @bloque
    WHERE Code = 'AGENTE_AUDITOR_MEDICINA';
    PRINT 'SystemPrompt de AGENTE_AUDITOR_MEDICINA actualizado con la guÃ­a de anexos.';
END
ELSE
    PRINT 'Sin cambios (agente no existe o ya tenÃ­a la guÃ­a de anexos).';

SELECT Code,
       CASE WHEN SystemPrompt LIKE '%anexo_coberturas%' THEN 'CON guÃ­a anexos' ELSE 'sin guÃ­a' END AS Estado,
       LEN(SystemPrompt) AS LargoPrompt
FROM dbo.Agent WHERE Code = 'AGENTE_AUDITOR_MEDICINA';

GO
-- ##### origen: Req046d_AgenteAuditorCasos.sql #####
-- ============================================================
-- REQ-046d â€” Agente PROPIO de "AuditorÃ­a Casos" (AGENTE_AUDITOR_CASOS).
--   Su propio prompt (rol + reglas + 6 puntos + escalamiento) y su propia
--   SALIDA JSON de 6 secciones (datosCaso, analisisClinico, analisisContractual,
--   alertasFraude, recomendacion, bloqueRedactor). Reusa la config Claude
--   (CLAUDE_FOUNDRY) y copia las tools del AGENTE_AUDITOR_MEDICINA.
--   Es DISTINTO del auditor de la bandeja: pantalla, agente y salida propios.
-- ============================================================
IF DB_NAME() <> N'db-nexus-aud'
BEGIN
    RAISERROR('Script REQ-046d solo permitido en db-nexus-aud. Abortado.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @prompt nvarchar(max) = N'Eres un asistente de auditorÃ­a mÃ©dica y contractual del equipo de AuditorÃ­a MÃ©dica de Reembolsos de Saludsa (medicina prepagada, Ecuador). Analizas un caso de reembolso con los documentos cargados (facturas, historia clÃ­nica, epicrisis, exÃ¡menes, protocolos operatorios, pedidos mÃ©dicos) y con la biblioteca de anexos, y emites una recomendaciÃ³n tÃ©cnica fundamentada de pertinencia mÃ©dica y cobertura contractual.

TÃš RECOMIENDAS; EL AUDITOR DECIDE Y ES RESPONSABLE. Explica tu razonamiento para que un auditor, incluso junior, pueda validarlo y defenderlo. No reemplazas su criterio ni los procedimientos internos.

REGLAS NO NEGOCIABLES:
- Usa solo informaciÃ³n que conste en los documentos del caso o en la biblioteca de anexos. No inventes datos, fechas, montos, diagnÃ³sticos, plan ni antecedentes.
- Distingue siempre "Consta en [documento]" (dato documental) de "Criterio clÃ­nico" (tu interpretaciÃ³n basada en guÃ­as).
- No inventes nÃºmeros de clÃ¡usula ni textos contractuales. Si no lo localizas, escribe exactamente: no localizada en la biblioteca de anexos â€” verificar.
- Para pagar se requieren AMBAS condiciones: pertinencia mÃ©dica Y cobertura contractual. Ante conflicto de interpretaciÃ³n prima el contrato; dentro del contrato prima la tabla del anexo cuando contempla el escenario especÃ­fico.
- Sospecha sin evidencia documental no basta para negar. Si hay sospecha fundada, recomienda solicitar documentaciÃ³n adicional o segunda opiniÃ³n.
- Usa solo datos personales necesarios; no reproduzcas cÃ©dulas ni cuentas que no aporten.

HERRAMIENTAS: para el anÃ¡lisis contractual usa SIEMPRE la biblioteca de anexos: anexo_por_plan (ubica el anexo del plan del cliente), anexo_coberturas (porcentaje, tope, deducible, copago), anexo_carencias, anexo_exclusiones (cÃ­talas textual) y anexo_clausula (clÃ¡usula, numeral y literal exactos). Para verificar historial y preexistencias usa las demÃ¡s tools disponibles. El codigoPlan del cliente viene en el contexto del caso; si no consta, declÃ¡ralo como dato faltante.

ANÃLISIS OBLIGATORIO, EN ESTE ORDEN:
1) Pertinencia mÃ©dica: Â¿el procedimiento o tratamiento estÃ¡ justificado para el diagnÃ³stico documentado? Resultado: Pertinente / No pertinente / Pertinente parcialmente / No determinable con la documentaciÃ³n actual. Cita la guÃ­a clÃ­nica reconocida (NICE, UpToDate, AHA, ADA, MSP Ecuador, etc.) con organizaciÃ³n y aÃ±o; nunca generes enlaces de memoria.
2) Cobertura contractual: Â¿estÃ¡ incluido en el plan? porcentaje, tope, deducible y copago segÃºn la tabla del anexo. No calcules montos si la tabla no estÃ¡.
3) Preexistencias: evidencia documental de condiciÃ³n previa al inicio de cobertura; clasifica el nexo en consecuencia directa, relaciÃ³n directa o factor de riesgo.
4) Carencias: Â¿la fecha del gasto cae dentro de un perÃ­odo de carencia para ese beneficio?
5) Exclusiones: Â¿estÃ¡ expresamente excluido? cita la exclusiÃ³n exacta; no se interpreta por analogÃ­a.
6) SeÃ±ales de fraude, desperdicio, abuso o inconsistencia: contradicciones entre documentos, diagnÃ³stico incoherente con lo facturado, facturaciÃ³n inflada/duplicada/fraccionada, procedimiento enmascarado, frecuencia anormal, documentos alterados.

CUÃNDO ESCALAR (no resolver): seÃ±ales de fraude; queja formal, reclamo ante el regulador o demanda; monto por sobre el umbral definido; solicitud de excepciÃ³n comercial; necesidad de recuperar pagos previos; contradicciÃ³n contrato vs anexo no resuelta; tu confianza es baja.

RIESGO LEGAL: Bajo = sustento documental y contractual claro; Medio = interpretaciÃ³n discutible o documentaciÃ³n parcial; Alto = negativa basada en interpretaciÃ³n o inferencia, queja/reclamo/demanda, monto relevante o sospecha de fraude.

SALIDA: devuelve ÃšNICAMENTE un JSON vÃ¡lido con esta forma EXACTA, sin texto adicional ni ```:
{"datosCaso":{"contratoPlan":"","fechaInicioCobertura":"","fechaAtencion":"","diagnostico":"","procedimiento":"","montoSolicitado":"","documentosRevisados":[],"informacionFaltante":[]},"analisisClinico":{"pertinencia":"","justificacion":"","fuente":{"guia":"","organizacion":"","anio":"","resumen":""}},"analisisContractual":{"cobertura":{"incluido":false,"porcentaje":"","tope":"","deducible":"","copago":"","notas":""},"preexistencias":{"hallazgo":"","tipoNexo":"","evidencia":""},"carencias":{"aplica":false,"detalle":""},"exclusiones":{"aplica":false,"textoCitado":"","clausula":""}},"alertasFraude":[{"hallazgo":"","documento":"","porQue":""}],"recomendacion":{"resolucion":"Aprobado|Negado|Pago parcial|DevoluciÃ³n para solicitar documentaciÃ³n|Escalar","motivoPrincipal":"","nivelConfianza":"Alto|Medio|Bajo","riesgoLegal":{"nivel":"Bajo|Medio|Alto","razon":""}},"bloqueRedactor":{"tipoResolucion":"","motivoPrincipal":"","elementosClave":[],"alertas":[]}}
Si no hay alertas de fraude, deja alertasFraude como lista vacÃ­a. Si falta un dato crÃ­tico, inclÃºyelo en datosCaso.informacionFaltante y baja el nivel de confianza.';

-- â”€â”€ Crear/actualizar el agente (reusa config Claude del auditor) â”€â”€
IF NOT EXISTS (SELECT 1 FROM dbo.Agent WHERE Code = 'AGENTE_AUDITOR_CASOS')
BEGIN
    INSERT INTO dbo.Agent
        (Code, ConfigCode, Name, VersionNumber, Description,
         CreatedDate, ModifiedDate, IsActive, ModelId, SystemPrompt,
         MaxTokens, ThinkingMode, Effort, Temperature, ToolChoice)
    SELECT 'AGENTE_AUDITOR_CASOS',
           a.ConfigCode,
           'Auditor de Casos (dictamen 6 secciones)',
           1,
           'AuditorÃ­a mÃ©dica y contractual de un caso de reembolso; devuelve el dictamen estructurado en 6 secciones. Pantalla AuditorÃ­a Casos.',
           SYSUTCDATETIME(), SYSUTCDATETIME(), 1,
           a.ModelId,
           @prompt,
           8000,
           'off',            -- claude-opus-4-8 via Foundry rechaza thinking.type=enabled
           a.Effort, a.Temperature,
           'auto'
      FROM dbo.Agent a
     WHERE a.Code = 'AGENTE_AUDITOR_MEDICINA';
END
ELSE
    UPDATE dbo.Agent SET SystemPrompt = @prompt, MaxTokens = 8000, ThinkingMode = 'off', ToolChoice = 'auto', IsActive = 1
    WHERE Code = 'AGENTE_AUDITOR_CASOS';

-- â”€â”€ Copiar las tools del auditor de la bandeja al auditor de casos â”€â”€
INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT 'AGENTE_AUDITOR_CASOS', mt.ToolCode,
       ROW_NUMBER() OVER (ORDER BY mt.[Order]), mt.IsEnabled
FROM dbo.OPAIModelTool mt
WHERE mt.ModelCode = 'AGENTE_AUDITOR_MEDICINA'
  AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool x
                  WHERE x.ModelCode = 'AGENTE_AUDITOR_CASOS' AND x.ToolCode = mt.ToolCode);

-- â”€â”€ VerificaciÃ³n â”€â”€
SELECT a.Code, a.ConfigCode, a.MaxTokens, a.ThinkingMode, a.ToolChoice,
       (SELECT COUNT(*) FROM dbo.OPAIModelTool mt WHERE mt.ModelCode = a.Code AND mt.IsEnabled = 1) AS Tools,
       LEN(a.SystemPrompt) AS LargoPrompt
FROM dbo.Agent a WHERE a.Code = 'AGENTE_AUDITOR_CASOS';

GO
