-- ============================================================
-- MIGRACIÓN: tabla ZendeskConf (configuración multi-cuenta)
-- BD: db-nexus-test
-- Versión: 0002 (delta de REQ-019 T3)
-- Fecha: 2026-06-17
-- Autor: DBA DeveloperAI
-- ADITIVO — no modifica ni elimina nada existente
-- Depende de: Req019_ModeloClaude.sql (versión 0001)
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
-- BLOQUE 1: NUEVA TABLA ZendeskConf
-- Almacena la configuración de cada cuenta Zendesk (Auxiliar,
-- Digital, Experience).  SecretRef referencia el nombre del
-- secret en Azure Key Vault; NO almacena tokens en claro.
-- ============================================================

IF OBJECT_ID('dbo.ZendeskConf', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ZendeskConf (
        [Code]      varchar(50)     NOT NULL,
        -- Valores esperados: 'Auxiliar' | 'Digital' | 'Experience'
        [Cuenta]    varchar(30)     NOT NULL,
        [Subdomain] varchar(100)    NOT NULL,
        -- Referencia al secret de Key Vault (ej. 'zendesk-token-auxiliar')
        -- En entorno local puede contener el token como placeholder.
        [SecretRef] varchar(250)    NULL,
        [IsActive]  bit             NOT NULL
            CONSTRAINT DF_ZendeskConf_IsActive DEFAULT 1,
        CONSTRAINT PK_ZendeskConf PRIMARY KEY CLUSTERED ([Code])
    );

    CREATE INDEX IX_ZendeskConf_IsActive ON dbo.ZendeskConf ([IsActive]);

    PRINT 'Table ZendeskConf created.';
END;

-- ============================================================
-- BLOQUE 2: SEED inicial de cuentas (idempotente con MERGE)
-- Los tokens en SecretRef son PLACEHOLDER; deben actualizarse
-- con los nombres de secret reales en Key Vault antes del
-- primer uso en producción.
-- ============================================================

MERGE dbo.ZendeskConf AS target
USING (VALUES
    ('Auxiliar',   'Auxiliar',   'saludsa',  NULL, 1),
    ('Digital',    'Digital',    'saludsa',  NULL, 1),
    ('Experience', 'Experience', 'saludsa',  NULL, 0)
) AS src ([Code], [Cuenta], [Subdomain], [SecretRef], [IsActive])
ON target.[Code] = src.[Code]
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Code], [Cuenta], [Subdomain], [SecretRef], [IsActive])
    VALUES (src.[Code], src.[Cuenta], src.[Subdomain], src.[SecretRef], src.[IsActive]);

PRINT 'ZendeskConf seed applied (MERGE — no overwrite of existing rows).';

COMMIT TRANSACTION;
PRINT 'Req019b_ZendeskConf.sql (0002) completed successfully.';
GO
