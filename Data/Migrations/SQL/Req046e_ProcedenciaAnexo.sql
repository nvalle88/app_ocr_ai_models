-- ============================================================
-- REQ-046e — PROCEDENCIA: cada dato estructurado del anexo/contrato sabe DÓNDE
-- está en el PDF (página + segmento verbatim). Así el análisis corre sobre la BD
-- (rápido) pero al ejecutivo se le muestra el PDF abierto en ese lugar.
-- Aditivo e idempotente. Cambiar el nombre de BD para prod (db-nexus-aud).
-- ============================================================
IF DB_NAME() <> N'db-nexus-test'
BEGIN
    RAISERROR('Script REQ-046e solo permitido en db-nexus-test. Abortado.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON; SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;

IF COL_LENGTH('dbo.AnexoCobertura','Pagina')     IS NULL ALTER TABLE dbo.AnexoCobertura  ADD Pagina INT NULL;
IF COL_LENGTH('dbo.AnexoCobertura','TextoOrigen') IS NULL ALTER TABLE dbo.AnexoCobertura ADD TextoOrigen NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.AnexoCarencia','Pagina')      IS NULL ALTER TABLE dbo.AnexoCarencia   ADD Pagina INT NULL;
IF COL_LENGTH('dbo.AnexoCarencia','TextoOrigen')  IS NULL ALTER TABLE dbo.AnexoCarencia  ADD TextoOrigen NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.AnexoExclusion','Pagina')     IS NULL ALTER TABLE dbo.AnexoExclusion  ADD Pagina INT NULL;
IF COL_LENGTH('dbo.AnexoExclusion','TextoOrigen') IS NULL ALTER TABLE dbo.AnexoExclusion ADD TextoOrigen NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.AnexoClausula','Pagina')      IS NULL ALTER TABLE dbo.AnexoClausula   ADD Pagina INT NULL;
IF COL_LENGTH('dbo.AnexoClausula','TextoOrigen')  IS NULL ALTER TABLE dbo.AnexoClausula  ADD TextoOrigen NVARCHAR(MAX) NULL;

SELECT 'cobertura.Pagina='  + CAST(COL_LENGTH('dbo.AnexoCobertura','Pagina') AS varchar) + ' | '
     + 'clausula.TextoOrigen=' + CAST(COL_LENGTH('dbo.AnexoClausula','TextoOrigen') AS varchar) AS Verificacion;
