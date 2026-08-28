-- ============================================================
-- REQ-019: Tool 'validar_procedimiento_factura' (procedimientos + PVP/tarifario)
-- BD: db-nexus-test  |  IDEMPOTENTE  |  ADITIVO
-- Requisitos previos: Req019c_SeedTools.sql, Req019e_SeedClaudeFoundry.sql, Req019f_SeedProcesoAnalisisSobre.sql
--
-- Mapea a un endpoint REAL ya existente:
--   POST {api-reembolso-automatico}/api/Correlacion/ValidarCorrelacionProcedimiento
--   (api-reembolso-automatico / CorrelacionController.ValidarCorrelacionProcedimiento)
--   Respuesta RespuestaCorrelacion: TieneCorrelacion, ProcedimientoSalud, NombreProcedimientoSalud,
--   Homologada, PasaEdad/PasaGenero/PasaFrecuencia, Pvp (valor de tarifario), EsConvenio, ControlValorTarifario.
--
-- ⚠️ DRAFT — REQUIERE PRUEBA DE HUMO ANTES DE PRODUCCIÓN:
--   1) Verificar en Pruebas (1 sobre real, p.ej. NA-2612551) los campos MÍNIMOS que exige
--      ValidarCorrelacionFiltro { TicketUsuario, DetalleSobreItem, CodigoProcedimiento }.
--      El TicketUsuario puede requerir más campos que los aquí mapeados.
--   2) InternalApiToolExecutor.SetNestedJsonValue serializa TODO valor como STRING; si el endpoint
--      exige numéricos (PrecioUnitario/Cantidad) puede requerir ajuste en el executor o el binding.
--   3) Requiere: placeholder {api-reembolso-automatico} en InternalApiToolExecutor.ResolveBaseUrl (YA añadido)
--      + config Saludsa:BaseUrls:ApiReembolsoAutomatico + regla de egress (B1/T0a).
-- Por eso este script está SEPARADO de Req019f: aplícalo sólo tras validar el contrato.
-- ============================================================
IF DB_NAME() <> N'db-nexus-test'
BEGIN
    RAISERROR('Script REQ-019g solo permitido en db-nexus-test. Abortado.', 16, 1);
    RETURN;
END;

SET QUOTED_IDENTIFIER ON;   -- requerido por MERGE (índices filtrados en el esquema)
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @inputSchema NVARCHAR(MAX) = N'{
  "type": "object",
  "properties": {
    "numeroSobre":        { "type": "string", "description": "Número del sobre en análisis." },
    "cedulaBeneficiario": { "type": "string", "description": "Cédula del beneficiario (del OCR / resolver_contrato_por_cedula)." },
    "codigoProducto":     { "type": "string", "description": "Código de producto del contrato (de resolver_contrato_por_cedula)." },
    "codigoPrincipal":    { "type": "string", "description": "Código principal del ítem/procedimiento en la factura (del OCR)." },
    "codigoAuxiliar":     { "type": "string", "description": "Código auxiliar del ítem, si aplica." },
    "descripcion":        { "type": "string", "description": "Descripción del procedimiento/ítem tal como aparece en la factura." },
    "codigoDiagnostico":  { "type": "string", "description": "Código de diagnóstico (CIE-10) asociado, si aparece." },
    "precioUnitario":     { "type": "string", "description": "Valor unitario presentado en la factura." },
    "cantidad":           { "type": "string", "description": "Cantidad del ítem." },
    "codigoProcedimiento":{ "type": "string", "description": "Código de procedimiento a validar/homologar." }
  },
  "required": ["numeroSobre", "codigoProducto", "descripcion"]
}';

DECLARE @bindingConfig NVARCHAR(MAX) = N'{
  "baseUrl": "{api-reembolso-automatico}",
  "method": "POST",
  "path": "/api/Correlacion/ValidarCorrelacionProcedimiento",
  "paramMap": {},
  "bodyMap": [
    "ticketUsuario.numeroSobre",
    "ticketUsuario.cedulaBeneficiario",
    "ticketUsuario.codigoProducto",
    "detalleSobreItem.codigoPrincipal",
    "detalleSobreItem.codigoAuxiliar",
    "detalleSobreItem.descripcion",
    "detalleSobreItem.codigoDiagnostico",
    "detalleSobreItem.precioUnitario",
    "detalleSobreItem.cantidad",
    "codigoProcedimiento"
  ],
  "authMode": "saludsa-oauth"
}';

MERGE dbo.OPAITool AS tgt
USING (SELECT N'validar_procedimiento_factura' AS Code) AS src
   ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    Name          = N'validar_procedimiento_factura',
    Description   = N'Valida un procedimiento/ítem de la factura (código del prestador o descripción del OCR) contra el catálogo y tarifario de Saludsa. Devuelve si tiene correlación, el procedimiento Salud homologado, si pasa edad/género/frecuencia y el PVP (valor de tarifario). Ejecutar por cada ítem detectado en el OCR.',
    InputSchema   = @inputSchema,
    Strict        = 0,
    BindingType   = N'InternalApi',
    BindingConfig = @bindingConfig,
    IsActive      = 1,
    VersionNumber = 1
WHEN NOT MATCHED THEN INSERT
    (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES (
    N'validar_procedimiento_factura',
    N'validar_procedimiento_factura',
    N'Valida un procedimiento/ítem de la factura (código del prestador o descripción del OCR) contra el catálogo y tarifario de Saludsa. Devuelve si tiene correlación, el procedimiento Salud homologado, si pasa edad/género/frecuencia y el PVP (valor de tarifario). Ejecutar por cada ítem detectado en el OCR.',
    @inputSchema,
    0,
    N'InternalApi',
    @bindingConfig,
    1, 1, SYSUTCDATETIME()
);

-- Vincular la tool al agente Claude (Order 8, después de la cadena B5)
IF EXISTS (SELECT 1 FROM dbo.Agent WHERE Code = 'AGENTE_CLAUDE')
MERGE dbo.OPAIModelTool AS tgt
USING (SELECT 'AGENTE_CLAUDE' AS ModelCode, 'validar_procedimiento_factura' AS ToolCode, 8 AS [Order]) AS src
   ON tgt.ModelCode = src.ModelCode AND tgt.ToolCode = src.ToolCode
WHEN MATCHED THEN
    UPDATE SET tgt.[Order] = src.[Order], tgt.IsEnabled = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (ModelCode, ToolCode, [Order], IsEnabled)
    VALUES (src.ModelCode, src.ToolCode, src.[Order], 1);

COMMIT TRANSACTION;

SELECT 'OPAITool' AS tabla, Code, BindingType, IsActive FROM dbo.OPAITool WHERE Code = 'validar_procedimiento_factura';
SELECT 'OPAIModelTool' AS tabla, ModelCode, ToolCode, [Order], IsEnabled FROM dbo.OPAIModelTool WHERE ToolCode = 'validar_procedimiento_factura';
GO
