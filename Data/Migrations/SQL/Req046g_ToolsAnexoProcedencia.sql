-- ============================================================
-- REQ-046g — Las tools del anexo devuelven la PROCEDENCIA (Pagina + TextoOrigen),
-- para que el dictamen pueda citar y enlazar al lugar exacto del PDF.
-- Idempotente. Cambiar el nombre de BD para prod (db-nexus-aud).
-- ============================================================
IF DB_NAME() <> N'db-nexus-test'
BEGIN
    RAISERROR('Script REQ-046g solo permitido en db-nexus-test. Abortado.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON; SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON;

UPDATE dbo.OPAITool SET BindingConfig =
 N'{"connection":"DefaultConnection","maxRows":80,"query":"SELECT TOP 80 co.Beneficio, co.CodigoBeneficio, co.Porcentaje, co.Tope, co.MonedaTope, co.Deducible, co.Copago, co.Periodo, co.Ambito, co.Notas, co.Pagina, co.TextoOrigen FROM dbo.AnexoCobertura co JOIN dbo.Anexo a ON a.Id = co.AnexoId WHERE a.IsActive = 1 AND a.CodigoPlan = @codigoPlan AND (@beneficio IS NULL OR LEN(@beneficio) = 0 OR CHARINDEX(@beneficio, co.Beneficio) > 0) ORDER BY co.Beneficio"}'
 WHERE Code = 'anexo_coberturas';

UPDATE dbo.OPAITool SET BindingConfig =
 N'{"connection":"DefaultConnection","maxRows":80,"query":"SELECT TOP 80 ca.Beneficio, ca.DiasCarencia, ca.Notas, ca.Pagina, ca.TextoOrigen FROM dbo.AnexoCarencia ca JOIN dbo.Anexo a ON a.Id = ca.AnexoId WHERE a.IsActive = 1 AND a.CodigoPlan = @codigoPlan ORDER BY ca.Beneficio"}'
 WHERE Code = 'anexo_carencias';

UPDATE dbo.OPAITool SET BindingConfig =
 N'{"connection":"DefaultConnection","maxRows":100,"query":"SELECT TOP 100 ex.Texto, ex.ClausulaRef, ex.Pagina, ex.TextoOrigen FROM dbo.AnexoExclusion ex WHERE ex.AnexoId IN (SELECT Id FROM dbo.Anexo WHERE IsActive = 1 AND CodigoPlan = @codigoPlan) OR ex.ContratoId IN (SELECT ContratoId FROM dbo.Anexo WHERE IsActive = 1 AND CodigoPlan = @codigoPlan AND ContratoId IS NOT NULL) ORDER BY ex.Id"}'
 WHERE Code = 'anexo_exclusiones';

UPDATE dbo.OPAITool SET BindingConfig =
 N'{"connection":"DefaultConnection","maxRows":25,"query":"SELECT TOP 25 c.Tipo AS TipoContrato, cl.Ordinal, cl.Numeral, cl.Literal, cl.Titulo, cl.Texto, cl.Pagina, cl.TextoOrigen FROM dbo.AnexoClausula cl JOIN dbo.AnexoContrato c ON c.Id = cl.ContratoId WHERE (@tipoContrato IS NULL OR LEN(@tipoContrato) = 0 OR c.Tipo = @tipoContrato) AND (@texto IS NULL OR LEN(@texto) = 0 OR CHARINDEX(@texto, cl.Texto) > 0 OR CHARINDEX(@texto, cl.Titulo) > 0) ORDER BY cl.Id"}'
 WHERE Code = 'anexo_clausula';

SELECT Code, CASE WHEN CAST(BindingConfig AS nvarchar(max)) LIKE '%TextoOrigen%' THEN 'con procedencia' ELSE 'sin' END AS Estado
FROM dbo.OPAITool WHERE Code IN ('anexo_coberturas','anexo_carencias','anexo_exclusiones','anexo_clausula') ORDER BY Code;
