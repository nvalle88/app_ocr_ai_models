-- ============================================================
-- REQ-046a — Biblioteca de ANEXOS estructurada (Auditoría de Casos)
--
-- ADITIVO. No toca ninguna tabla existente. Crea el maestro propio de
-- Nexus donde vive, estructurado, el contrato base por tipo y su ANEXO por
-- plan con las condiciones parametrizadas (coberturas, topes, deducibles,
-- carencias, exclusiones, cláusulas). El alta se hace por OCR→Claude→BD desde
-- la pantalla de administración; las tools del agente auditor LEEN de aquí
-- (no de M-Files ni de las BD de Saludsa en vivo).
--
-- Todo en la BD propia de Nexus (db-nexus-test / OcrAiConnection).
-- Idempotente: se puede re-ejecutar.
-- ============================================================
IF DB_NAME() <> N'db-nexus-test'
BEGIN
    RAISERROR('Script REQ-046a solo permitido en db-nexus-test. Abortado.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

-- ── 1. Contrato base (por tipo: Individual, Tradicional, Optimus Plus, Oncológico…) ──
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

-- ── 2. Anexo (por plan/producto, con las condiciones parametrizadas) ──
IF OBJECT_ID('dbo.Anexo', 'U') IS NULL
CREATE TABLE dbo.Anexo
(
    Id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Anexo PRIMARY KEY,
    ContratoId         INT           NULL CONSTRAINT FK_Anexo_Contrato REFERENCES dbo.AnexoContrato(Id),
    CodigoPlan         NVARCHAR(40)  NOT NULL,     -- enlaza con el plan del cliente (Cl04Contratos.CodigoPlan)
    NombrePlan         NVARCHAR(200) NULL,         -- Pro150K, Sky70K Plus Lite, Star 30…
    CodigoProducto     NVARCHAR(20)  NULL,         -- IND, COR, ONC, XPR…
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

-- ── 3. Coberturas del anexo (beneficio → %, tope, deducible, copago) ──
IF OBJECT_ID('dbo.AnexoCobertura', 'U') IS NULL
CREATE TABLE dbo.AnexoCobertura
(
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AnexoCobertura PRIMARY KEY,
    AnexoId         INT           NOT NULL CONSTRAINT FK_AnexoCobertura_Anexo REFERENCES dbo.Anexo(Id),
    Beneficio       NVARCHAR(200) NOT NULL,        -- Hospitalario, Ambulatorio, Medicina, Maternidad, Emergencia…
    CodigoBeneficio NVARCHAR(20)  NULL,
    Porcentaje      DECIMAL(5,2)  NULL,            -- % de cobertura
    Tope            DECIMAL(18,2) NULL,            -- monto máximo
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

-- ── 4. Carencias del anexo ──
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

-- ── 5. Exclusiones (del anexo o del contrato base) ──
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

-- ── 6. Cláusulas del contrato base (para citar cláusula/numeral/literal exacto) ──
IF OBJECT_ID('dbo.AnexoClausula', 'U') IS NULL
CREATE TABLE dbo.AnexoClausula
(
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AnexoClausula PRIMARY KEY,
    ContratoId  INT           NOT NULL CONSTRAINT FK_AnexoClausula_Contrato REFERENCES dbo.AnexoContrato(Id),
    Ordinal     NVARCHAR(60)  NULL,               -- Décima, Décima Primera…
    Numeral     NVARCHAR(20)  NULL,
    Literal     NVARCHAR(20)  NULL,
    Titulo      NVARCHAR(200) NULL,
    Texto       NVARCHAR(MAX) NOT NULL,
    CreatedDate DATETIME      NOT NULL CONSTRAINT DF_AnexoClausula_Created DEFAULT (GETUTCDATE())
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AnexoClausula_Contrato' AND object_id = OBJECT_ID('dbo.AnexoClausula'))
    CREATE INDEX IX_AnexoClausula_Contrato ON dbo.AnexoClausula (ContratoId);

-- ── 7. Traza de ingesta (cada carga: OCR → estructurado → guardado) ──
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
    JsonExtraido NVARCHAR(MAX) NULL,               -- JSON estructurado que devolvió Claude
    CreatedBy    NVARCHAR(150) NULL,
    CreatedDate  DATETIME      NOT NULL CONSTRAINT DF_AnexoIngesta_Created DEFAULT (GETUTCDATE())
);

-- ── Verificación ──
SELECT 'AnexoContrato'  AS Tabla, COUNT(*) AS Filas FROM dbo.AnexoContrato
UNION ALL SELECT 'Anexo',           COUNT(*) FROM dbo.Anexo
UNION ALL SELECT 'AnexoCobertura',  COUNT(*) FROM dbo.AnexoCobertura
UNION ALL SELECT 'AnexoCarencia',   COUNT(*) FROM dbo.AnexoCarencia
UNION ALL SELECT 'AnexoExclusion',  COUNT(*) FROM dbo.AnexoExclusion
UNION ALL SELECT 'AnexoClausula',   COUNT(*) FROM dbo.AnexoClausula
UNION ALL SELECT 'AnexoIngesta',    COUNT(*) FROM dbo.AnexoIngesta;
