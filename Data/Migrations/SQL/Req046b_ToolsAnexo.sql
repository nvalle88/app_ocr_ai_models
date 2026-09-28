-- ============================================================
-- REQ-046b — Tools del agente auditor que LEEN de la biblioteca de anexos
--            (tablas propias de Nexus, connection = DefaultConnection).
--   anexo_por_plan · anexo_coberturas · anexo_carencias ·
--   anexo_exclusiones · anexo_clausula
-- Se enlazan a AGENTE_AUDITOR_MEDICINA y AGENTE_CLAUDE.
-- Idempotente (upsert por Code + link condicional).
-- ============================================================
IF DB_NAME() <> N'db-nexus-test'
BEGIN
    RAISERROR('Script REQ-046b solo permitido en db-nexus-test. Abortado.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @code varchar(100), @desc nvarchar(500), @schema nvarchar(max), @binding nvarchar(max);

-- ── 1) anexo_por_plan ──────────────────────────────────────────────────────
SET @code = 'anexo_por_plan';
SET @desc = N'Devuelve el anexo estructurado del plan del cliente (nombre del plan, producto, versión, resumen de condiciones y tipo de contrato base). Úsala primero para ubicar el anexo del caso.';
SET @schema = N'{"type":"object","properties":{"codigoPlan":{"type":"string","description":"Código de plan del cliente (Cl04Contratos.CodigoPlan)."},"codigoProducto":{"type":"string","description":"Producto (IND, COR, ONC, XPR...). Opcional."}},"required":["codigoPlan"]}';
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

-- ── 2) anexo_coberturas ────────────────────────────────────────────────────
SET @code = 'anexo_coberturas';
SET @desc = N'Coberturas del anexo del plan: por beneficio devuelve porcentaje, tope, deducible, copago, periodo y ámbito. Es la fuente para el punto de cobertura contractual.';
SET @schema = N'{"type":"object","properties":{"codigoPlan":{"type":"string","description":"Código de plan del cliente."},"beneficio":{"type":"string","description":"Filtra por beneficio (Hospitalario, Ambulatorio, Medicina...). Opcional."}},"required":["codigoPlan"]}';
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

-- ── 3) anexo_carencias ─────────────────────────────────────────────────────
SET @code = 'anexo_carencias';
SET @desc = N'Períodos de carencia del anexo del plan por beneficio (días de carencia). Fuente para el punto de carencias.';
SET @schema = N'{"type":"object","properties":{"codigoPlan":{"type":"string","description":"Código de plan del cliente."}},"required":["codigoPlan"]}';
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

-- ── 4) anexo_exclusiones ───────────────────────────────────────────────────
SET @code = 'anexo_exclusiones';
SET @desc = N'Exclusiones del anexo del plan y del contrato base asociado (texto exacto + referencia de cláusula). Una exclusión no se interpreta por analogía: cítala textual.';
SET @schema = N'{"type":"object","properties":{"codigoPlan":{"type":"string","description":"Código de plan del cliente."}},"required":["codigoPlan"]}';
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

-- ── 5) anexo_clausula ──────────────────────────────────────────────────────
SET @code = 'anexo_clausula';
SET @desc = N'Cláusulas del contrato base (ordinal, numeral, literal, título, texto) para citar la cláusula exacta. Filtra por tipo de contrato o por texto.';
SET @schema = N'{"type":"object","properties":{"tipoContrato":{"type":"string","description":"Individual, Tradicional, OptimusPlus, Oncologico... Opcional."},"texto":{"type":"string","description":"Texto o tema a buscar dentro de las cláusulas. Opcional."}}}';
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

-- ── Verificación ───────────────────────────────────────────────────────────
SELECT t.Code, t.BindingType, t.IsActive,
       (SELECT COUNT(*) FROM dbo.OPAIModelTool mt WHERE mt.ToolCode = t.Code AND mt.IsEnabled = 1) AS Agentes
FROM dbo.OPAITool t
WHERE t.Code IN ('anexo_por_plan','anexo_coberturas','anexo_carencias','anexo_exclusiones','anexo_clausula')
ORDER BY t.Code;
