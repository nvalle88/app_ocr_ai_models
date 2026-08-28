-- ============================================================
-- REQ-019 — Clasificación y tipificación de documentos de reembolso
--           (spec_clasificacion_documentos_reembolso.md, §9 "Ampliación requerida")
-- BD: db-nexus-test
-- Versión: 0001
-- Fecha: 2026-08-21
-- Autor: DBA DeveloperAI
-- ADITIVO — solo CREATE TABLE / CREATE INDEX / CREATE VIEW nuevos.
--           NO altera ni una columna de las tablas existentes.
-- IDEMPOTENTE — cada objeto va protegido por IF OBJECT_ID / IF NOT EXISTS.
-- NOTA: script de UN SOLO batch (sin GO intermedios) a propósito, para que la
--       SALVAGUARDA de DB_NAME() cubra TODO el script. En Req019_ModeloClaude.sql
--       el GO de la línea 22 deja los batches siguientes SIN guarda; aquí no.
-- ============================================================

-- SALVAGUARDA: solo permitido en la BD de pruebas
IF DB_NAME() <> N'db-nexus-test'
BEGIN
    RAISERROR('Script REQ-019j solo permitido en db-nexus-test. Abortado.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;   -- requerido por los índices FILTRADOS de este script
SET ANSI_NULLS ON;

-- PRE-REQUISITO: dbo.DataFile(Id) debe existir (es el padre de las 5 tablas)
IF OBJECT_ID('dbo.DataFile', 'U') IS NULL
BEGIN
    RAISERROR('dbo.DataFile no existe. Abortado.', 16, 1);
    RETURN;
END;

BEGIN TRANSACTION;

-- ============================================================
-- BLOQUE 1: dbo.DataFilePage — OCR POR PÁGINA
-- Hoy AnalyzeResult.Pages se descarta (OcrIngestService.cs:64 solo guarda
-- operation.Value.Content). Esta tabla es el destino de esa estructura.
-- ============================================================
IF OBJECT_ID('dbo.DataFilePage', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DataFilePage (
        [Id]          int            IDENTITY(1,1) NOT NULL,
        [DataFileId]  int            NOT NULL,
        [PageNumber]  int            NOT NULL,   -- 1-based (DocumentPage.PageNumber)
        [Text]        varchar(max)   NULL,       -- varchar como DataFile.Text (IsUnicode(false))
        [Width]       real           NULL,       -- DocumentPage.Width  (float? en el SDK)
        [Height]      real           NULL,       -- DocumentPage.Height (float?)
        [Unit]        varchar(20)    NULL,       -- 'pixel' (imagen) | 'inch' (PDF)
        [Angle]       real           NULL,       -- DocumentPage.Angle (-180, 180]
        [LineCount]   int            NULL,       -- page.Lines.Count  (diagnóstico de calidad OCR)
        [WordCount]   int            NULL,       -- page.Words.Count
        [CharCount]   int            NULL,       -- LEN(Text) al momento de la ingesta
        [CreatedDate] datetime       NOT NULL
            CONSTRAINT DF_DataFilePage_CreatedDate DEFAULT (sysutcdatetime()),
        CONSTRAINT PK_DataFilePage PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT FK_DataFilePage_DataFile FOREIGN KEY ([DataFileId])
            REFERENCES dbo.DataFile ([Id]) ON DELETE CASCADE,
        CONSTRAINT CK_DataFilePage_PageNumber CHECK ([PageNumber] >= 1)
    );

    -- Una sola fila por (archivo, página): blinda contra doble ingesta.
    CREATE UNIQUE INDEX UQ_DataFilePage_File_Page
        ON dbo.DataFilePage ([DataFileId], [PageNumber]);

    PRINT 'Table DataFilePage created.';
END
ELSE PRINT 'DataFilePage ya existe — sin cambios.';

-- ============================================================
-- BLOQUE 2: dbo.DocumentoClasificacion — 1 fila VIGENTE por DataFile
-- spec §4 (TipoArchivo / ListaTipoArchivo), §1.1 (factura válida), §3 (ValorTotal)
-- Se versiona (VersionNumber + IsCurrent) para poder RE-clasificar sin perder
-- el histórico: la fila anterior queda con IsCurrent = 0.
-- ============================================================
IF OBJECT_ID('dbo.DocumentoClasificacion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentoClasificacion (
        [Id]               int            IDENTITY(1,1) NOT NULL,
        [DataFileId]       int            NOT NULL,
        -- UNO de: LAB_IMA-FACTURA | BEN_ADI-FACTURA | GENERAL-SOPORTE | CON_MED-FACTURA
        --         MED-FACTURA | LAB_CLI-FACTURA | ATE_HOS-FACTURA | GENERAL-FACTURA
        --         PRO-FACTURA | TER-FACTURA        (spec §4.1)
        [TipoArchivo]      varchar(30)    NOT NULL,
        -- CSV sin duplicados; aquí SÍ se admiten los *-SOPORTE (spec §4.2)
        [ListaTipoArchivo] varchar(400)   NULL,
        [ValorTotal]       decimal(18,2)  NOT NULL
            CONSTRAINT DF_DocumentoClasificacion_ValorTotal DEFAULT (0),
        [EsFacturaValida]  bit            NOT NULL
            CONSTRAINT DF_DocumentoClasificacion_EsFacturaValida DEFAULT (0),
        [NumeroFactura]    varchar(50)    NULL,   -- ej. '001-028-000000509'
        [ClaveAcceso]      varchar(60)    NULL,   -- clave de acceso SRI (49 díg.) o nº autorización
        [Confianza]        decimal(5,4)   NULL,   -- 0.0000 .. 1.0000
        [ModelCode]        varchar(50)    NULL,   -- ref. blanda a Agent.Code que clasificó
        [ExecutionId]      bigint         NULL,   -- ref. blanda a StepExecution.ExecutionId
        [RawJson]          nvarchar(max)  NULL,   -- respuesta cruda del clasificador (auditoría)
        [VersionNumber]    int            NOT NULL
            CONSTRAINT DF_DocumentoClasificacion_Version DEFAULT (1),
        [IsCurrent]        bit            NOT NULL
            CONSTRAINT DF_DocumentoClasificacion_IsCurrent DEFAULT (1),
        [CreatedDate]      datetime       NOT NULL
            CONSTRAINT DF_DocumentoClasificacion_CreatedDate DEFAULT (sysutcdatetime()),
        CONSTRAINT PK_DocumentoClasificacion PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT FK_DocumentoClasificacion_DataFile FOREIGN KEY ([DataFileId])
            REFERENCES dbo.DataFile ([Id]) ON DELETE CASCADE,
        CONSTRAINT CK_DocumentoClasificacion_ValorTotal CHECK ([ValorTotal] >= 0),
        CONSTRAINT CK_DocumentoClasificacion_Confianza
            CHECK ([Confianza] IS NULL OR ([Confianza] >= 0 AND [Confianza] <= 1))
    );

    -- Solo UNA clasificación vigente por archivo (índice FILTRADO → exige QUOTED_IDENTIFIER ON)
    CREATE UNIQUE INDEX UQ_DocumentoClasificacion_Current
        ON dbo.DocumentoClasificacion ([DataFileId])
        WHERE [IsCurrent] = 1;

    CREATE INDEX IX_DocumentoClasificacion_TipoArchivo
        ON dbo.DocumentoClasificacion ([TipoArchivo])
        INCLUDE ([DataFileId], [ValorTotal]);

    CREATE INDEX IX_DocumentoClasificacion_DataFileId
        ON dbo.DocumentoClasificacion ([DataFileId], [VersionNumber]);

    PRINT 'Table DocumentoClasificacion created.';
END
ELSE PRINT 'DocumentoClasificacion ya existe — sin cambios.';

-- ============================================================
-- BLOQUE 3: dbo.DocumentoItem — DESGLOSE ÍTEM POR ÍTEM
-- spec §9.2: una misma factura puede mezclar MED + ATE_HOS + PRO…
-- ============================================================
IF OBJECT_ID('dbo.DocumentoItem', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentoItem (
        [Id]            bigint         IDENTITY(1,1) NOT NULL,
        [DataFileId]    int            NOT NULL,
        [PageNumber]    int            NULL,   -- opcional: página donde aparece el rubro
        [Orden]         int            NULL,   -- orden de lectura dentro del documento
        [NumeroFactura] varchar(50)    NULL,   -- separa rubros cuando hay VARIAS facturas (spec §3.3)
        [Descripcion]   varchar(500)   NOT NULL,
        -- Taxonomía de rubro (spec §5)
        [TipoRubro]     varchar(10)    NOT NULL,
        [Cantidad]      decimal(18,4)  NULL,
        [ValorUnitario] decimal(18,4)  NULL,
        [ValorTotal]    decimal(18,2)  NULL,
        [Confianza]     decimal(5,4)   NULL,
        [CreatedDate]   datetime       NOT NULL
            CONSTRAINT DF_DocumentoItem_CreatedDate DEFAULT (sysutcdatetime()),
        CONSTRAINT PK_DocumentoItem PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT FK_DocumentoItem_DataFile FOREIGN KEY ([DataFileId])
            REFERENCES dbo.DataFile ([Id]) ON DELETE CASCADE,
        CONSTRAINT CK_DocumentoItem_TipoRubro CHECK ([TipoRubro] IN
            ('MED','ATE_HOS','PRO','CON_MED','LAB_CLI','LAB_IMA','TER','BEN_ADI','GENERAL')),
        CONSTRAINT CK_DocumentoItem_PageNumber
            CHECK ([PageNumber] IS NULL OR [PageNumber] >= 1),
        CONSTRAINT CK_DocumentoItem_Confianza
            CHECK ([Confianza] IS NULL OR ([Confianza] >= 0 AND [Confianza] <= 1))
    );

    CREATE INDEX IX_DocumentoItem_DataFileId
        ON dbo.DocumentoItem ([DataFileId], [PageNumber])
        INCLUDE ([TipoRubro], [ValorTotal]);

    -- Soporta el "split del reembolso por tipo" (spec §9.3) sin table scan
    CREATE INDEX IX_DocumentoItem_TipoRubro
        ON dbo.DocumentoItem ([TipoRubro])
        INCLUDE ([DataFileId], [ValorTotal]);

    PRINT 'Table DocumentoItem created.';
END
ELSE PRINT 'DocumentoItem ya existe — sin cambios.';

-- ============================================================
-- BLOQUE 4: dbo.DocumentoTag — TAGS por DOCUMENTO y por PÁGINA
-- spec §9.1. PageNumber NULL = tag a nivel de documento completo.
-- ============================================================
IF OBJECT_ID('dbo.DocumentoTag', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentoTag (
        [Id]          bigint        IDENTITY(1,1) NOT NULL,
        [DataFileId]  int           NOT NULL,
        [PageNumber]  int           NULL,        -- NULL ⇒ tag del documento, no de una página
        [Tag]         varchar(60)   NOT NULL,    -- 'MED-FACTURA', 'TIENE_CLAVE_ACCESO', 'TOTAL_EN_ULTIMA_HOJA'…
        [Categoria]   varchar(30)   NULL,        -- 'TIPO' | 'MARCA' | 'RUBRO' | 'DIAGNOSTICO' | 'TOTAL'
        [Origen]      varchar(10)   NOT NULL,    -- 'IA' (clasificador) | 'Regla' (keywords spec §5)
        [Confianza]   decimal(5,4)  NULL,
        [Valor]       varchar(200)  NULL,        -- valor asociado al tag, si lo tiene
        [CreatedDate] datetime      NOT NULL
            CONSTRAINT DF_DocumentoTag_CreatedDate DEFAULT (sysutcdatetime()),
        CONSTRAINT PK_DocumentoTag PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT FK_DocumentoTag_DataFile FOREIGN KEY ([DataFileId])
            REFERENCES dbo.DataFile ([Id]) ON DELETE CASCADE,
        CONSTRAINT CK_DocumentoTag_Origen CHECK ([Origen] IN ('IA','Regla')),
        CONSTRAINT CK_DocumentoTag_PageNumber
            CHECK ([PageNumber] IS NULL OR [PageNumber] >= 1),
        CONSTRAINT CK_DocumentoTag_Confianza
            CHECK ([Confianza] IS NULL OR ([Confianza] >= 0 AND [Confianza] <= 1))
    );

    -- Anti-duplicado (spec §1.3). OJO: SQL Server trata los NULL como IGUALES en
    -- un índice único ⇒ (DataFileId, NULL, Tag, Origen) admite UNA sola fila,
    -- que es justo la semántica deseada para el tag a nivel de documento.
    CREATE UNIQUE INDEX UQ_DocumentoTag_Unico
        ON dbo.DocumentoTag ([DataFileId], [PageNumber], [Tag], [Origen]);

    CREATE INDEX IX_DocumentoTag_Tag
        ON dbo.DocumentoTag ([Tag])
        INCLUDE ([DataFileId], [PageNumber]);

    PRINT 'Table DocumentoTag created.';
END
ELSE PRINT 'DocumentoTag ya existe — sin cambios.';

-- ============================================================
-- BLOQUE 5: dbo.DocumentoDiagnostico — CIE10 normalizado por DataFile
-- spec §2 (normalizar sin puntos: M51.9 ⇒ M519) y §7 (ValorTotal completo a
-- CADA diagnóstico del archivo).
-- ============================================================
IF OBJECT_ID('dbo.DocumentoDiagnostico', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentoDiagnostico (
        [Id]              bigint         IDENTITY(1,1) NOT NULL,
        [DataFileId]      int            NOT NULL,
        [PageNumber]      int            NULL,   -- primera página donde se detectó (trazabilidad)
        [Codigo]          varchar(10)    NOT NULL,  -- CIE10 NORMALIZADO, sin puntos
        [CodigoOriginal]  varchar(15)    NULL,      -- tal como salió del OCR ('M51.9')
        [Descripcion]     varchar(300)   NULL,
        [ValorAsignado]   decimal(18,2)  NULL,      -- ValorTotal del archivo (spec §7)
        [CreatedDate]     datetime       NOT NULL
            CONSTRAINT DF_DocumentoDiagnostico_CreatedDate DEFAULT (sysutcdatetime()),
        CONSTRAINT PK_DocumentoDiagnostico PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT FK_DocumentoDiagnostico_DataFile FOREIGN KEY ([DataFileId])
            REFERENCES dbo.DataFile ([Id]) ON DELETE CASCADE,
        CONSTRAINT CK_DocumentoDiagnostico_PageNumber
            CHECK ([PageNumber] IS NULL OR [PageNumber] >= 1)
    );

    -- Un CIE10 aparece UNA vez por archivo (spec §2 "extraer TODOS", §7 agrupa por Codigo)
    CREATE UNIQUE INDEX UQ_DocumentoDiagnostico_File_Codigo
        ON dbo.DocumentoDiagnostico ([DataFileId], [Codigo]);

    -- Alimenta el ResumenDiagnosticos agrupado (spec §7)
    CREATE INDEX IX_DocumentoDiagnostico_Codigo
        ON dbo.DocumentoDiagnostico ([Codigo])
        INCLUDE ([DataFileId], [ValorAsignado]);

    PRINT 'Table DocumentoDiagnostico created.';
END
ELSE PRINT 'DocumentoDiagnostico ya existe — sin cambios.';

COMMIT TRANSACTION;

-- ============================================================
-- BLOQUE 6: VISTA de apoyo — split del reembolso por tipo (spec §9.3)
-- Derivada de DocumentoItem: NO agrega tabla ni riesgo de desincronización.
-- ============================================================
IF OBJECT_ID('dbo.vw_DocumentoSplitPorTipo', 'V') IS NULL
    EXEC(N'
    CREATE VIEW dbo.vw_DocumentoSplitPorTipo AS
    SELECT  df.CaseCode,
            i.DataFileId,
            i.TipoRubro,
            COUNT(*)                    AS ItemCount,
            SUM(ISNULL(i.ValorTotal,0)) AS ValorTipo
    FROM    dbo.DocumentoItem i
    JOIN    dbo.DataFile      df ON df.Id = i.DataFileId
    GROUP BY df.CaseCode, i.DataFileId, i.TipoRubro;');

-- ============================================================
-- Verificación post-migración
-- ============================================================
SELECT t.name AS Tabla, p.rows AS Filas
FROM   sys.tables t
JOIN   sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
WHERE  t.name IN ('DataFilePage','DocumentoClasificacion','DocumentoItem',
                  'DocumentoTag','DocumentoDiagnostico')
ORDER BY t.name;

SELECT i.name AS Indice, OBJECT_NAME(i.object_id) AS Tabla, i.is_unique, i.has_filter
FROM   sys.indexes i
WHERE  OBJECT_NAME(i.object_id) IN ('DataFilePage','DocumentoClasificacion',
       'DocumentoItem','DocumentoTag','DocumentoDiagnostico')
   AND i.name IS NOT NULL
ORDER BY Tabla, Indice;