-- ============================================================
-- REQ-019: "Inteligencia" del agente — tools SQL a BD + skill de metodología
-- BD: db-nexus-test  |  IDEMPOTENTE  |  ADITIVO
-- Requisitos previos: Req019_ModeloClaude.sql, Req019c, Req019e, Req019f.
--
-- Qué hace:
--   1) 3 tools SQL (BindingType='Sql', read-only, ejecutadas por InternalApiToolExecutor):
--        consultar_sobre_bd          → cabecera real del sobre (estado, valor, contrato…)
--        consultar_detalle_sobre_bd  → detalle con VALORES (presentado/consultor/pendiente)
--        buscar_sobres_cliente_bd    → sobres del cliente por NOMBRE o contrato
--      Fuente: bdd_Salud_Consultas (ConnectionStrings:SaludConsultas).
--   2) Skill de metodología (OPAIPrompt en Markdown) apilada al agente vía OPAIModelPrompt.
--   3) Vincula las 3 tools a AGENTE_CLAUDE (Order 8-10).
-- ============================================================
IF DB_NAME() <> N'db-nexus-test'
BEGIN
    RAISERROR('Script REQ-019h solo permitido en db-nexus-test. Abortado.', 16, 1);
    RETURN;
END;

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM dbo.Agent WHERE Code = 'AGENTE_CLAUDE')
    THROW 51001, 'Falta Agent AGENTE_CLAUDE: correr Req019e primero.', 1;

-- ------------------------------------------------------------
-- 1a) Tool SQL: consultar_sobre_bd
-- ------------------------------------------------------------
MERGE dbo.OPAITool AS tgt
USING (SELECT N'consultar_sobre_bd' AS Code) AS src ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    Name = N'consultar_sobre_bd',
    Description = N'Consulta en la base de datos la cabecera REAL del sobre de reembolso: estado, valor presentado, fecha de recepción, contrato, producto, región y titular. Usar SIEMPRE al inicio para confirmar los datos del sobre.',
    InputSchema = N'{"type":"object","properties":{"numeroSobre":{"type":"string","description":"Número del sobre (p. ej. NA-2612551)."}},"required":["numeroSobre"]}',
    Strict = 1, BindingType = N'Sql',
    BindingConfig = N'{"connection":"SaludConsultas","maxRows":10,"query":"SELECT TOP (10) s.NumeroSobre, es.NombreEstado AS Estado, s.ValorPresentado, s.FechaRecepcion, s.NumeroContrato, s.CodigoProducto, s.CodigoRegion, s.PersonaContacto AS Titular, s.FechaDigitacion, s.UsuarioAsignado FROM dbo.Sobre s WITH (NOLOCK) LEFT JOIN dbo.EstadosSobre es WITH (NOLOCK) ON es.IdEstadoSobre = s.IdEstadoSobre WHERE s.NumeroSobre = @numeroSobre ORDER BY s.IdSobre DESC"}',
    IsActive = 1, VersionNumber = 1
WHEN NOT MATCHED THEN INSERT
    (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES (
    N'consultar_sobre_bd', N'consultar_sobre_bd',
    N'Consulta en la base de datos la cabecera REAL del sobre de reembolso: estado, valor presentado, fecha de recepción, contrato, producto, región y titular. Usar SIEMPRE al inicio para confirmar los datos del sobre.',
    N'{"type":"object","properties":{"numeroSobre":{"type":"string","description":"Número del sobre (p. ej. NA-2612551)."}},"required":["numeroSobre"]}',
    1, N'Sql',
    N'{"connection":"SaludConsultas","maxRows":10,"query":"SELECT TOP (10) s.NumeroSobre, es.NombreEstado AS Estado, s.ValorPresentado, s.FechaRecepcion, s.NumeroContrato, s.CodigoProducto, s.CodigoRegion, s.PersonaContacto AS Titular, s.FechaDigitacion, s.UsuarioAsignado FROM dbo.Sobre s WITH (NOLOCK) LEFT JOIN dbo.EstadosSobre es WITH (NOLOCK) ON es.IdEstadoSobre = s.IdEstadoSobre WHERE s.NumeroSobre = @numeroSobre ORDER BY s.IdSobre DESC"}',
    1, 1, SYSUTCDATETIME());

-- ------------------------------------------------------------
-- 1b) Tool SQL: consultar_detalle_sobre_bd (VALORES por detalle)
-- ------------------------------------------------------------
MERGE dbo.OPAITool AS tgt
USING (SELECT N'consultar_detalle_sobre_bd' AS Code) AS src ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    Name = N'consultar_detalle_sobre_bd',
    Description = N'Consulta en la base de datos el DETALLE del sobre con sus VALORES reales: valor presentado por detalle, valor liquidado por el consultor, valor pendiente, persona, tipo de cobertura, estado del detalle y observaciones. Es la fuente de verdad de los valores del sobre.',
    InputSchema = N'{"type":"object","properties":{"numeroSobre":{"type":"string","description":"Número del sobre (p. ej. NA-2612551)."}},"required":["numeroSobre"]}',
    Strict = 1, BindingType = N'Sql',
    BindingConfig = N'{"connection":"SaludConsultas","maxRows":50,"query":"SELECT TOP (50) d.IdDetalleSobre, d.NumeroPersona, d.IdTipoCobertura, d.ValorPresentadoDetalle, d.ValorConsultor, d.ValorPendiente, d.FechaIngresoDetalle, d.IdEstado AS IdEstadoDetalle, d.ObservacionesConsultor, d.NumeroSolicitudDetalle FROM dbo.DetalleSobre d WITH (NOLOCK) INNER JOIN dbo.Sobre s WITH (NOLOCK) ON s.IdSobre = d.IdSobre WHERE s.NumeroSobre = @numeroSobre ORDER BY d.IdDetalleSobre"}',
    IsActive = 1, VersionNumber = 1
WHEN NOT MATCHED THEN INSERT
    (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES (
    N'consultar_detalle_sobre_bd', N'consultar_detalle_sobre_bd',
    N'Consulta en la base de datos el DETALLE del sobre con sus VALORES reales: valor presentado por detalle, valor liquidado por el consultor, valor pendiente, persona, tipo de cobertura, estado del detalle y observaciones. Es la fuente de verdad de los valores del sobre.',
    N'{"type":"object","properties":{"numeroSobre":{"type":"string","description":"Número del sobre (p. ej. NA-2612551)."}},"required":["numeroSobre"]}',
    1, N'Sql',
    N'{"connection":"SaludConsultas","maxRows":50,"query":"SELECT TOP (50) d.IdDetalleSobre, d.NumeroPersona, d.IdTipoCobertura, d.ValorPresentadoDetalle, d.ValorConsultor, d.ValorPendiente, d.FechaIngresoDetalle, d.IdEstado AS IdEstadoDetalle, d.ObservacionesConsultor, d.NumeroSolicitudDetalle FROM dbo.DetalleSobre d WITH (NOLOCK) INNER JOIN dbo.Sobre s WITH (NOLOCK) ON s.IdSobre = d.IdSobre WHERE s.NumeroSobre = @numeroSobre ORDER BY d.IdDetalleSobre"}',
    1, 1, SYSUTCDATETIME());

-- ------------------------------------------------------------
-- 1c) Tool SQL: buscar_sobres_cliente_bd (por NOMBRE o contrato)
-- ------------------------------------------------------------
MERGE dbo.OPAITool AS tgt
USING (SELECT N'buscar_sobres_cliente_bd' AS Code) AS src ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    Name = N'buscar_sobres_cliente_bd',
    Description = N'Busca en la base de datos los sobres de un CLIENTE por su nombre (o parte, mín. 4 letras) o por número de contrato. Devuelve número de sobre, estado, valor, fecha y titular, del más reciente al más antiguo. Útil para "los sobres de Juan Pérez" o el historial de un contrato.',
    InputSchema = N'{"type":"object","properties":{"nombre":{"type":"string","description":"Nombre (o parte) del cliente/titular. Mínimo 4 letras."},"numeroContrato":{"type":"string","description":"Número de contrato (alternativa al nombre)."}},"required":[]}',
    Strict = 0, BindingType = N'Sql',
    BindingConfig = N'{"connection":"SaludConsultas","maxRows":30,"query":"SELECT TOP (30) s.NumeroSobre, es.NombreEstado AS Estado, s.ValorPresentado, s.FechaRecepcion, s.PersonaContacto AS Titular, s.NumeroContrato, s.CodigoProducto FROM dbo.Sobre s WITH (NOLOCK) LEFT JOIN dbo.EstadosSobre es WITH (NOLOCK) ON es.IdEstadoSobre = s.IdEstadoSobre WHERE (ISNULL(@nombre,'''') <> '''' AND LEN(@nombre) >= 4 AND s.PersonaContacto LIKE ''%'' + @nombre + ''%'') OR (ISNULL(@numeroContrato,'''') <> '''' AND s.NumeroContrato = TRY_CAST(@numeroContrato AS int)) ORDER BY s.FechaRecepcion DESC, s.IdSobre DESC"}',
    IsActive = 1, VersionNumber = 1
WHEN NOT MATCHED THEN INSERT
    (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES (
    N'buscar_sobres_cliente_bd', N'buscar_sobres_cliente_bd',
    N'Busca en la base de datos los sobres de un CLIENTE por su nombre (o parte, mín. 4 letras) o por número de contrato. Devuelve número de sobre, estado, valor, fecha y titular, del más reciente al más antiguo. Útil para "los sobres de Juan Pérez" o el historial de un contrato.',
    N'{"type":"object","properties":{"nombre":{"type":"string","description":"Nombre (o parte) del cliente/titular. Mínimo 4 letras."},"numeroContrato":{"type":"string","description":"Número de contrato (alternativa al nombre)."}},"required":[]}',
    0, N'Sql',
    N'{"connection":"SaludConsultas","maxRows":30,"query":"SELECT TOP (30) s.NumeroSobre, es.NombreEstado AS Estado, s.ValorPresentado, s.FechaRecepcion, s.PersonaContacto AS Titular, s.NumeroContrato, s.CodigoProducto FROM dbo.Sobre s WITH (NOLOCK) LEFT JOIN dbo.EstadosSobre es WITH (NOLOCK) ON es.IdEstadoSobre = s.IdEstadoSobre WHERE (ISNULL(@nombre,'''') <> '''' AND LEN(@nombre) >= 4 AND s.PersonaContacto LIKE ''%'' + @nombre + ''%'') OR (ISNULL(@numeroContrato,'''') <> '''' AND s.NumeroContrato = TRY_CAST(@numeroContrato AS int)) ORDER BY s.FechaRecepcion DESC, s.IdSobre DESC"}',
    1, 1, SYSUTCDATETIME());

-- ------------------------------------------------------------
-- 2) Vincular las 3 tools SQL a AGENTE_CLAUDE (Order 8-10)
-- ------------------------------------------------------------
;WITH toolset([ToolCode],[Order]) AS (
    SELECT 'consultar_sobre_bd',         8
    UNION ALL SELECT 'consultar_detalle_sobre_bd', 9
    UNION ALL SELECT 'buscar_sobres_cliente_bd',  10
)
MERGE dbo.OPAIModelTool AS tgt
USING (
    SELECT 'AGENTE_CLAUDE' AS ModelCode, ts.[ToolCode], ts.[Order]
    FROM toolset ts
    JOIN dbo.OPAITool t ON t.Code = ts.[ToolCode] AND t.IsActive = 1
) AS src
   ON tgt.ModelCode = src.ModelCode AND tgt.ToolCode = src.ToolCode
WHEN MATCHED THEN UPDATE SET tgt.[Order] = src.[Order], tgt.IsEnabled = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (ModelCode, ToolCode, [Order], IsEnabled)
    VALUES (src.ModelCode, src.ToolCode, src.[Order], 1);

-- ------------------------------------------------------------
-- 3) Skill de metodología (Markdown) como OPAIPrompt apilado al agente
-- ------------------------------------------------------------
DECLARE @skill NVARCHAR(MAX) = N'## Skill: Metodología de análisis de sobres Saludsa

### Fuentes y cuándo usarlas
- Los "Datos estructurados del sobre" del mensaje traen numeroSobre, contrato, producto, región y persona: úsalos DIRECTAMENTE como argumentos de las herramientas (no los pidas al usuario, no los inventes).
- BD directa (rápidas, priorízalas): consultar_sobre_bd (cabecera/estado/valor), consultar_detalle_sobre_bd (VALORES por detalle), buscar_sobres_cliente_bd (historial del cliente por nombre/contrato).
- APIs de negocio: consultar_coberturas_plan y consultar_deducible_contrato (requieren región/producto/plan/contrato/persona — del contexto o de resolver_contrato_por_cedula), consultar_preexistencias_por_cedula / consultar_diagnosticos_preexistentes (por cédula), obtener_documentos_sobre_armonix.
- resolver_contrato_por_cedula requiere cédula Y año de nacimiento; si no tienes año, dilo y sigue con lo que sí puedas responder.

### Flujo recomendado
1. consultar_sobre_bd → confirma sobre, estado, valor y titular.
2. consultar_detalle_sobre_bd → valores por detalle (presentado / liquidado / pendiente).
3. Del OCR: extrae ítems de la factura (procedimiento, cantidad, valor) y diagnóstico.
4. Si el contexto trae contrato/producto/región/persona → coberturas y deducible directo; si solo hay cédula (+año) → resolver_contrato primero.
5. Preexistencias cuando haya diagnóstico o la pregunta lo amerite.
6. Consolida: presenta valores REALES de la BD y, si estimas cobertura, rotúlala "estimación pre-liquidación (no oficial)".

### Reglas de respuesta
- Cita la fuente de cada dato: (BD sobre), (BD detalle), (API coberturas), (OCR).
- Si una herramienta falla, dilo explícitamente y continúa con las demás.
- Sé conciso: tabla o lista con los valores clave primero, explicación después.
- Nunca inventes montos ni estados: si no hay dato, di "sin dato".';

MERGE dbo.OPAIPrompt AS tgt
USING (SELECT N'SKILL_METODOLOGIA_SOBRE' AS Code) AS src ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET Content = @skill, ModifiedDate = SYSUTCDATETIME(), IsActive = 1
WHEN NOT MATCHED THEN INSERT (Code, Content, VersionNumber, CreatedDate, ModifiedDate, IsActive)
VALUES (N'SKILL_METODOLOGIA_SOBRE', @skill, 1, SYSUTCDATETIME(), SYSUTCDATETIME(), 1);

-- Vínculo OPAIModelPrompt (TypeAgent es FK a Catalog: usar uno existente)
DECLARE @typeAgent INT = COALESCE(
    (SELECT TOP 1 TypeAgent FROM dbo.OPAIModelPrompt),
    (SELECT TOP 1 Id FROM dbo.Catalog ORDER BY Id));

IF @typeAgent IS NULL
    PRINT 'AVISO: sin Catalog para TypeAgent — no se vinculó la skill (crear vínculo manual).';
ELSE IF NOT EXISTS (
    SELECT 1 FROM dbo.OPAIModelPrompt
    WHERE ModelCode = 'AGENTE_CLAUDE' AND PromptCode = 'SKILL_METODOLOGIA_SOBRE')
    INSERT INTO dbo.OPAIModelPrompt (ModelCode, PromptCode, [Order], IsDefault, TypeAgent)
    VALUES ('AGENTE_CLAUDE', 'SKILL_METODOLOGIA_SOBRE', 1, 0, @typeAgent);

-- ------------------------------------------------------------
-- 4) Prompt base del agente: más corto (la metodología vive en la skill)
-- ------------------------------------------------------------
UPDATE dbo.Agent
   SET SystemPrompt = N'Eres el analista médico-administrativo de Saludsa dentro de Nexus IA. Analizas sobres de reembolso con los documentos OCR del caso, el contexto estructurado del sobre y tus HERRAMIENTAS (base de datos y APIs de negocio). Respondes en español, con precisión operativa, citando la fuente de cada dato. Usa las herramientas siempre que aporten datos reales; no inventes nada.'
 WHERE Code = 'AGENTE_CLAUDE';

COMMIT TRANSACTION;

-- Verificación
SELECT 'tools' AS q, Code, BindingType, IsActive FROM dbo.OPAITool WHERE BindingType = 'Sql' ORDER BY Code;
SELECT 'links' AS q, ModelCode, ToolCode, [Order], IsEnabled FROM dbo.OPAIModelTool WHERE ModelCode = 'AGENTE_CLAUDE' ORDER BY [Order];
SELECT 'skill' AS q, Code, IsActive, LEN(Content) AS chars FROM dbo.OPAIPrompt WHERE Code = 'SKILL_METODOLOGIA_SOBRE';
SELECT 'skill-link' AS q, ModelCode, PromptCode, [Order] FROM dbo.OPAIModelPrompt WHERE ModelCode = 'AGENTE_CLAUDE';
GO
