-- ============================================================
-- SEED: Catálogo de tools REQ-019 T5 (OPAITool)
-- BD: db-nexus-test
-- Versión: 0001
-- Fecha: 2026-06-18
-- Autor: DeveloperAI / REQ-019 T5
-- IDEMPOTENTE — usa MERGE; seguro de re-ejecutar.
-- ADITIVO — no borra ni modifica tools existentes fuera del catálogo.
-- ============================================================

-- SALVAGUARDA: solo permitido en la BD de pruebas
IF DB_NAME() <> N'db-nexus-test'
BEGIN
    RAISERROR('Script REQ-019c solo permitido en db-nexus-test. Abortado.', 16, 1);
    RETURN;
END;

SET NOCOUNT ON;
BEGIN TRANSACTION;

-- ============================================================
-- 1. resolver_contrato_por_cedula
--    Cadena B5: FUENTE de contrato/convenio/producto/plan/región
--    Endpoint: GET api-contrato /api/contrato/ObtenerContratosPorDocumentoChatBot
-- ============================================================
MERGE dbo.OPAITool AS tgt
USING (SELECT N'resolver_contrato_por_cedula' AS Code) AS src
ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    Name         = N'resolver_contrato_por_cedula',
    Description  = N'Resuelve los contratos vigentes de un afiliado a partir de su cédula y año de nacimiento. Devuelve la lista de contratos (número de contrato, convenio, producto, plan, versión, región, número de persona). Es el prerrequisito para tools que requieren numeroContrato y numeroPersona (cadena B5).',
    InputSchema  = N'{
  "type": "object",
  "properties": {
    "numeroDocumento": {
      "type": "string",
      "description": "Número de cédula o documento de identidad del afiliado."
    },
    "anioNacimiento": {
      "type": "integer",
      "description": "Año de nacimiento del afiliado (4 dígitos). Requerido para desambiguación."
    },
    "incluirContratos": {
      "type": "boolean",
      "description": "Si es true, incluye contratos individuales además de convenios. Por defecto true.",
      "default": true
    }
  },
  "required": ["numeroDocumento", "anioNacimiento"]
}',
    Strict       = 1,
    BindingType  = N'InternalApi',
    BindingConfig = N'{
  "baseUrl": "{api-contrato}",
  "method": "GET",
  "path": "/api/contrato/ObtenerContratosPorDocumentoChatBot",
  "paramMap": {
    "numeroDocumento": "numeroDocumento",
    "anioNacimiento": "anioNacimiento",
    "incluirContratos": "incluirContratos"
  },
  "bodyMap": [],
  "authMode": "saludsa-oauth"
}',
    IsActive     = 1,
    VersionNumber = 1
WHEN NOT MATCHED THEN INSERT
    (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES (
    N'resolver_contrato_por_cedula',
    N'resolver_contrato_por_cedula',
    N'Resuelve los contratos vigentes de un afiliado a partir de su cédula y año de nacimiento. Devuelve la lista de contratos (número de contrato, convenio, producto, plan, versión, región, número de persona). Es el prerrequisito para tools que requieren numeroContrato y numeroPersona (cadena B5).',
    N'{
  "type": "object",
  "properties": {
    "numeroDocumento": {
      "type": "string",
      "description": "Número de cédula o documento de identidad del afiliado."
    },
    "anioNacimiento": {
      "type": "integer",
      "description": "Año de nacimiento del afiliado (4 dígitos). Requerido para desambiguación."
    },
    "incluirContratos": {
      "type": "boolean",
      "description": "Si es true, incluye contratos individuales además de convenios. Por defecto true.",
      "default": true
    }
  },
  "required": ["numeroDocumento", "anioNacimiento"]
}',
    1,
    N'InternalApi',
    N'{
  "baseUrl": "{api-contrato}",
  "method": "GET",
  "path": "/api/contrato/ObtenerContratosPorDocumentoChatBot",
  "paramMap": {
    "numeroDocumento": "numeroDocumento",
    "anioNacimiento": "anioNacimiento",
    "incluirContratos": "incluirContratos"
  },
  "bodyMap": [],
  "authMode": "saludsa-oauth"
}',
    1, 1, SYSUTCDATETIME()
);

-- ============================================================
-- 2. consultar_preexistencias_por_cedula
--    Sin cadena B5; recibe cédula directamente.
--    Endpoint: GET api-contrato /api/Preexistencias/ConsultaPreexistenciasRegistradasPorIdBeneficiario
-- ============================================================
MERGE dbo.OPAITool AS tgt
USING (SELECT N'consultar_preexistencias_por_cedula' AS Code) AS src
ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    Name         = N'consultar_preexistencias_por_cedula',
    Description  = N'Consulta las preexistencias médicas registradas de un afiliado a partir de su cédula. Devuelve el listado de condiciones preexistentes con estado, fecha de inicio y fin del proceso.',
    InputSchema  = N'{
  "type": "object",
  "properties": {
    "identificacion": {
      "type": "string",
      "description": "Número de cédula del afiliado."
    },
    "tipoDocumento": {
      "type": "string",
      "description": "Tipo de documento (ej: CED para cédula).",
      "default": "CED"
    },
    "estadoProceso": {
      "type": "string",
      "description": "Filtro de estado del proceso (ej: ACTIVO, INACTIVO). Opcional."
    },
    "fechaInicio": {
      "type": "string",
      "format": "date",
      "description": "Fecha de inicio del filtro (YYYY-MM-DD). Opcional."
    },
    "fechaFin": {
      "type": "string",
      "format": "date",
      "description": "Fecha fin del filtro (YYYY-MM-DD). Opcional."
    },
    "numeroPagina": {
      "type": "integer",
      "description": "Número de página para paginación.",
      "default": 1
    },
    "registrosPagina": {
      "type": "integer",
      "description": "Registros por página.",
      "default": 50
    }
  },
  "required": ["identificacion"]
}',
    Strict       = 1,
    BindingType  = N'InternalApi',
    BindingConfig = N'{
  "baseUrl": "{api-contrato}",
  "method": "GET",
  "path": "/api/Preexistencias/ConsultaPreexistenciasRegistradasPorIdBeneficiario",
  "paramMap": {
    "identificacion": "identificacion",
    "tipoDocumento": "tipoDocumento",
    "estadoProceso": "estadoProceso",
    "fechaInicio": "fechaInicio",
    "fechaFin": "fechaFin",
    "numeroPagina": "numeroPagina",
    "registrosPagina": "registrosPagina"
  },
  "bodyMap": [],
  "authMode": "saludsa-oauth"
}',
    IsActive     = 1,
    VersionNumber = 1
WHEN NOT MATCHED THEN INSERT
    (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES (
    N'consultar_preexistencias_por_cedula',
    N'consultar_preexistencias_por_cedula',
    N'Consulta las preexistencias médicas registradas de un afiliado a partir de su cédula. Devuelve el listado de condiciones preexistentes con estado, fecha de inicio y fin del proceso.',
    N'{
  "type": "object",
  "properties": {
    "identificacion": {
      "type": "string",
      "description": "Número de cédula del afiliado."
    },
    "tipoDocumento": {
      "type": "string",
      "description": "Tipo de documento (ej: CED para cédula).",
      "default": "CED"
    },
    "estadoProceso": {
      "type": "string",
      "description": "Filtro de estado del proceso (ej: ACTIVO, INACTIVO). Opcional."
    },
    "fechaInicio": {
      "type": "string",
      "format": "date",
      "description": "Fecha de inicio del filtro (YYYY-MM-DD). Opcional."
    },
    "fechaFin": {
      "type": "string",
      "format": "date",
      "description": "Fecha fin del filtro (YYYY-MM-DD). Opcional."
    },
    "numeroPagina": {
      "type": "integer",
      "description": "Número de página para paginación.",
      "default": 1
    },
    "registrosPagina": {
      "type": "integer",
      "description": "Registros por página.",
      "default": 50
    }
  },
  "required": ["identificacion"]
}',
    1,
    N'InternalApi',
    N'{
  "baseUrl": "{api-contrato}",
  "method": "GET",
  "path": "/api/Preexistencias/ConsultaPreexistenciasRegistradasPorIdBeneficiario",
  "paramMap": {
    "identificacion": "identificacion",
    "tipoDocumento": "tipoDocumento",
    "estadoProceso": "estadoProceso",
    "fechaInicio": "fechaInicio",
    "fechaFin": "fechaFin",
    "numeroPagina": "numeroPagina",
    "registrosPagina": "registrosPagina"
  },
  "bodyMap": [],
  "authMode": "saludsa-oauth"
}',
    1, 1, SYSUTCDATETIME()
);

-- ============================================================
-- 3. consultar_diagnosticos_preexistentes
--    Sin cadena B5; recibe lista de cédulas.
--    Endpoint: POST api-contrato /api/Preexistencias/ObtenerDiagnosticosPreexistentes
-- ============================================================
MERGE dbo.OPAITool AS tgt
USING (SELECT N'consultar_diagnosticos_preexistentes' AS Code) AS src
ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    Name         = N'consultar_diagnosticos_preexistentes',
    Description  = N'Obtiene los diagnósticos preexistentes para una o varias cédulas. Devuelve los diagnósticos CIE-10 registrados como preexistencias para cada beneficiario.',
    InputSchema  = N'{
  "type": "object",
  "properties": {
    "identificaciones": {
      "type": "array",
      "items": { "type": "string" },
      "description": "Lista de números de cédula de los beneficiarios a consultar."
    }
  },
  "required": ["identificaciones"]
}',
    Strict       = 1,
    BindingType  = N'InternalApi',
    BindingConfig = N'{
  "baseUrl": "{api-contrato}",
  "method": "POST",
  "path": "/api/Preexistencias/ObtenerDiagnosticosPreexistentes",
  "paramMap": {},
  "bodyMap": ["identificaciones"],
  "authMode": "saludsa-oauth"
}',
    IsActive     = 1,
    VersionNumber = 1
WHEN NOT MATCHED THEN INSERT
    (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES (
    N'consultar_diagnosticos_preexistentes',
    N'consultar_diagnosticos_preexistentes',
    N'Obtiene los diagnósticos preexistentes para una o varias cédulas. Devuelve los diagnósticos CIE-10 registrados como preexistencias para cada beneficiario.',
    N'{
  "type": "object",
  "properties": {
    "identificaciones": {
      "type": "array",
      "items": { "type": "string" },
      "description": "Lista de números de cédula de los beneficiarios a consultar."
    }
  },
  "required": ["identificaciones"]
}',
    1,
    N'InternalApi',
    N'{
  "baseUrl": "{api-contrato}",
  "method": "POST",
  "path": "/api/Preexistencias/ObtenerDiagnosticosPreexistentes",
  "paramMap": {},
  "bodyMap": ["identificaciones"],
  "authMode": "saludsa-oauth"
}',
    1, 1, SYSUTCDATETIME()
);

-- ============================================================
-- 4. consultar_coberturas_plan [B5]
--    Requiere contrato/persona (cadena B5).
--    Endpoint: POST api-armonix /api/IAConsultas/BuscarCoberturasIA
-- ============================================================
MERGE dbo.OPAITool AS tgt
USING (SELECT N'consultar_coberturas_plan' AS Code) AS src
ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    Name         = N'consultar_coberturas_plan',
    Description  = N'Consulta las coberturas del plan del afiliado en Armonix, incluyendo carencias ambulatorias y hospitalarias. Requiere los datos del contrato (resolver_contrato_por_cedula primero).',
    InputSchema  = N'{
  "type": "object",
  "properties": {
    "region": {
      "type": "string",
      "description": "Región del contrato (obtenida de resolver_contrato_por_cedula)."
    },
    "codigoProducto": {
      "type": "string",
      "description": "Código del producto (obtenido de resolver_contrato_por_cedula)."
    },
    "codigoPlan": {
      "type": "string",
      "description": "Código del plan (obtenido de resolver_contrato_por_cedula)."
    },
    "versionPlan": {
      "type": "string",
      "description": "Versión del plan (obtenida de resolver_contrato_por_cedula)."
    },
    "contratoNumero": {
      "type": "string",
      "description": "Número de contrato (obtenido de resolver_contrato_por_cedula)."
    },
    "personaNumero": {
      "type": "string",
      "description": "Número de persona/beneficiario (obtenido de resolver_contrato_por_cedula)."
    }
  },
  "required": ["region", "codigoProducto", "codigoPlan", "versionPlan", "contratoNumero", "personaNumero"]
}',
    Strict       = 1,
    BindingType  = N'Armonix',
    BindingConfig = N'{
  "baseUrl": "{api-armonix}",
  "method": "POST",
  "path": "/api/IAConsultas/BuscarCoberturasIA",
  "paramMap": {},
  "bodyMap": ["region", "codigoProducto", "codigoPlan", "versionPlan", "contratoNumero", "personaNumero"],
  "authMode": "saludsa-oauth"
}',
    IsActive     = 1,
    VersionNumber = 1
WHEN NOT MATCHED THEN INSERT
    (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES (
    N'consultar_coberturas_plan',
    N'consultar_coberturas_plan',
    N'Consulta las coberturas del plan del afiliado en Armonix, incluyendo carencias ambulatorias y hospitalarias. Requiere los datos del contrato (resolver_contrato_por_cedula primero).',
    N'{
  "type": "object",
  "properties": {
    "region": {
      "type": "string",
      "description": "Región del contrato (obtenida de resolver_contrato_por_cedula)."
    },
    "codigoProducto": {
      "type": "string",
      "description": "Código del producto (obtenido de resolver_contrato_por_cedula)."
    },
    "codigoPlan": {
      "type": "string",
      "description": "Código del plan (obtenido de resolver_contrato_por_cedula)."
    },
    "versionPlan": {
      "type": "string",
      "description": "Versión del plan (obtenida de resolver_contrato_por_cedula)."
    },
    "contratoNumero": {
      "type": "string",
      "description": "Número de contrato (obtenido de resolver_contrato_por_cedula)."
    },
    "personaNumero": {
      "type": "string",
      "description": "Número de persona/beneficiario (obtenido de resolver_contrato_por_cedula)."
    }
  },
  "required": ["region", "codigoProducto", "codigoPlan", "versionPlan", "contratoNumero", "personaNumero"]
}',
    1,
    N'Armonix',
    N'{
  "baseUrl": "{api-armonix}",
  "method": "POST",
  "path": "/api/IAConsultas/BuscarCoberturasIA",
  "paramMap": {},
  "bodyMap": ["region", "codigoProducto", "codigoPlan", "versionPlan", "contratoNumero", "personaNumero"],
  "authMode": "saludsa-oauth"
}',
    1, 1, SYSUTCDATETIME()
);

-- ============================================================
-- 5. consultar_deducible_contrato [B5]
--    Requiere contrato/persona (cadena B5).
--    Endpoint: GET api-contrato /api/contrato/ObtenerDeduciblePorContratoNumeroPersona
-- ============================================================
MERGE dbo.OPAITool AS tgt
USING (SELECT N'consultar_deducible_contrato' AS Code) AS src
ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    Name         = N'consultar_deducible_contrato',
    Description  = N'Consulta el deducible del contrato para un beneficiario específico. Requiere los datos del contrato (resolver_contrato_por_cedula primero). Devuelve el deducible aplicado y el saldo restante.',
    InputSchema  = N'{
  "type": "object",
  "properties": {
    "region": {
      "type": "string",
      "description": "Región del contrato (obtenida de resolver_contrato_por_cedula)."
    },
    "codigoProducto": {
      "type": "string",
      "description": "Código del producto (obtenido de resolver_contrato_por_cedula)."
    },
    "numeroContrato": {
      "type": "string",
      "description": "Número de contrato (obtenido de resolver_contrato_por_cedula)."
    },
    "numeroPersonaBeneficiario": {
      "type": "string",
      "description": "Número de persona/beneficiario (obtenido de resolver_contrato_por_cedula)."
    }
  },
  "required": ["region", "codigoProducto", "numeroContrato", "numeroPersonaBeneficiario"]
}',
    Strict       = 1,
    BindingType  = N'InternalApi',
    BindingConfig = N'{
  "baseUrl": "{api-contrato}",
  "method": "GET",
  "path": "/api/contrato/ObtenerDeduciblePorContratoNumeroPersona",
  "paramMap": {
    "region": "region",
    "codigoProducto": "codigoProducto",
    "numeroContrato": "numeroContrato",
    "numeroPersonaBeneficiario": "numeroPersonaBeneficiario"
  },
  "bodyMap": [],
  "authMode": "saludsa-oauth"
}',
    IsActive     = 1,
    VersionNumber = 1
WHEN NOT MATCHED THEN INSERT
    (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES (
    N'consultar_deducible_contrato',
    N'consultar_deducible_contrato',
    N'Consulta el deducible del contrato para un beneficiario específico. Requiere los datos del contrato (resolver_contrato_por_cedula primero). Devuelve el deducible aplicado y el saldo restante.',
    N'{
  "type": "object",
  "properties": {
    "region": {
      "type": "string",
      "description": "Región del contrato (obtenida de resolver_contrato_por_cedula)."
    },
    "codigoProducto": {
      "type": "string",
      "description": "Código del producto (obtenido de resolver_contrato_por_cedula)."
    },
    "numeroContrato": {
      "type": "string",
      "description": "Número de contrato (obtenido de resolver_contrato_por_cedula)."
    },
    "numeroPersonaBeneficiario": {
      "type": "string",
      "description": "Número de persona/beneficiario (obtenido de resolver_contrato_por_cedula)."
    }
  },
  "required": ["region", "codigoProducto", "numeroContrato", "numeroPersonaBeneficiario"]
}',
    1,
    N'InternalApi',
    N'{
  "baseUrl": "{api-contrato}",
  "method": "GET",
  "path": "/api/contrato/ObtenerDeduciblePorContratoNumeroPersona",
  "paramMap": {
    "region": "region",
    "codigoProducto": "codigoProducto",
    "numeroContrato": "numeroContrato",
    "numeroPersonaBeneficiario": "numeroPersonaBeneficiario"
  },
  "bodyMap": [],
  "authMode": "saludsa-oauth"
}',
    1, 1, SYSUTCDATETIME()
);

-- ============================================================
-- 6. consultar_deducibles_coberturas_plan [B5 parcial]
--    Requiere producto/plan (sin número de contrato necesariamente).
--    Endpoint: POST api-armonix /api/ConsultaPlanes/buscarCoberturas
-- ============================================================
MERGE dbo.OPAITool AS tgt
USING (SELECT N'consultar_deducibles_coberturas_plan' AS Code) AS src
ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    Name         = N'consultar_deducibles_coberturas_plan',
    Description  = N'Consulta los deducibles y coberturas del plan en Armonix a nivel de plan/producto. Requiere código de producto y plan (obtenidos de resolver_contrato_por_cedula).',
    InputSchema  = N'{
  "type": "object",
  "properties": {
    "codigoProducto": {
      "type": "string",
      "description": "Código del producto del plan."
    },
    "codigoPlan": {
      "type": "string",
      "description": "Código del plan."
    },
    "versionPlan": {
      "type": "string",
      "description": "Versión del plan."
    },
    "region": {
      "type": "string",
      "description": "Región del contrato."
    }
  },
  "required": ["codigoProducto", "codigoPlan"]
}',
    Strict       = 1,
    BindingType  = N'Armonix',
    BindingConfig = N'{
  "baseUrl": "{api-armonix}",
  "method": "POST",
  "path": "/api/ConsultaPlanes/buscarCoberturas",
  "paramMap": {},
  "bodyMap": ["codigoProducto", "codigoPlan", "versionPlan", "region"],
  "authMode": "saludsa-oauth"
}',
    IsActive     = 1,
    VersionNumber = 1
WHEN NOT MATCHED THEN INSERT
    (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES (
    N'consultar_deducibles_coberturas_plan',
    N'consultar_deducibles_coberturas_plan',
    N'Consulta los deducibles y coberturas del plan en Armonix a nivel de plan/producto. Requiere código de producto y plan (obtenidos de resolver_contrato_por_cedula).',
    N'{
  "type": "object",
  "properties": {
    "codigoProducto": {
      "type": "string",
      "description": "Código del producto del plan."
    },
    "codigoPlan": {
      "type": "string",
      "description": "Código del plan."
    },
    "versionPlan": {
      "type": "string",
      "description": "Versión del plan."
    },
    "region": {
      "type": "string",
      "description": "Región del contrato."
    }
  },
  "required": ["codigoProducto", "codigoPlan"]
}',
    1,
    N'Armonix',
    N'{
  "baseUrl": "{api-armonix}",
  "method": "POST",
  "path": "/api/ConsultaPlanes/buscarCoberturas",
  "paramMap": {},
  "bodyMap": ["codigoProducto", "codigoPlan", "versionPlan", "region"],
  "authMode": "saludsa-oauth"
}',
    1, 1, SYSUTCDATETIME()
);

-- ============================================================
-- 7. consultar_coberturas_plan_prestador [INACTIVA — ruta pendiente T0a]
--    Variante inactiva hasta confirmar ruta real en api-prestador.
--    Endpoint: POST api-prestador /CoberturasPlan (sin RoutePrefix confirmado)
-- ============================================================
MERGE dbo.OPAITool AS tgt
USING (SELECT N'consultar_coberturas_plan_prestador' AS Code) AS src
ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    Name         = N'consultar_coberturas_plan_prestador',
    Description  = N'[INACTIVA — pendiente T0a] Variante de coberturas del plan desde api-prestador. Inactiva hasta confirmar la ruta real (RoutePrefix sin confirmar en CoberturasController).',
    InputSchema  = N'{
  "type": "object",
  "properties": {
    "region": { "type": "string" },
    "codigoProducto": { "type": "string" },
    "codigoPlan": { "type": "string" },
    "numeroContrato": { "type": "string" },
    "numeroPersona": { "type": "string" }
  },
  "required": ["region", "codigoProducto", "codigoPlan", "numeroContrato", "numeroPersona"]
}',
    Strict       = 1,
    BindingType  = N'InternalApi',
    BindingConfig = N'{
  "baseUrl": "{api-prestador}",
  "method": "POST",
  "path": "/CoberturasPlan",
  "paramMap": {},
  "bodyMap": ["region", "codigoProducto", "codigoPlan", "numeroContrato", "numeroPersona"],
  "authMode": "saludsa-oauth"
}',
    IsActive     = 1,   -- sembrada activa (catálogo); NO vinculada a ningún agente productivo
    VersionNumber = 1
WHEN NOT MATCHED THEN INSERT
    (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES (
    N'consultar_coberturas_plan_prestador',
    N'consultar_coberturas_plan_prestador',
    N'[INACTIVA — pendiente T0a] Variante de coberturas del plan desde api-prestador. Inactiva hasta confirmar la ruta real (RoutePrefix sin confirmar en CoberturasController).',
    N'{
  "type": "object",
  "properties": {
    "region": { "type": "string" },
    "codigoProducto": { "type": "string" },
    "codigoPlan": { "type": "string" },
    "numeroContrato": { "type": "string" },
    "numeroPersona": { "type": "string" }
  },
  "required": ["region", "codigoProducto", "codigoPlan", "numeroContrato", "numeroPersona"]
}',
    1,
    N'InternalApi',
    N'{
  "baseUrl": "{api-prestador}",
  "method": "POST",
  "path": "/CoberturasPlan",
  "paramMap": {},
  "bodyMap": ["region", "codigoProducto", "codigoPlan", "numeroContrato", "numeroPersona"],
  "authMode": "saludsa-oauth"
}',
    1, 1, SYSUTCDATETIME()
);

-- ============================================================
-- 8. obtener_documentos_sobre_armonix
--    BindingType = Armonix (fuente documental MFiles).
--    Endpoint: POST api-armonix /api/sobres/BuscarDocumentosCompleto
-- ============================================================
MERGE dbo.OPAITool AS tgt
USING (SELECT N'obtener_documentos_sobre_armonix' AS Code) AS src
ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    Name         = N'obtener_documentos_sobre_armonix',
    Description  = N'Obtiene los documentos de un sobre de reembolso desde Armonix (MFiles). Devuelve la lista de documentos con nombre, extensión y contenido en base64. Identificados por número de sobre y contrato.',
    InputSchema  = N'{
  "type": "object",
  "properties": {
    "NumeroSobre": {
      "type": "string",
      "description": "Número del sobre de reembolso en Armonix."
    },
    "NumeroContrato": {
      "type": "string",
      "description": "Número de contrato del afiliado."
    },
    "CodigoProducto": {
      "type": "string",
      "description": "Código del producto del contrato."
    },
    "CodigoRegion": {
      "type": "string",
      "description": "Código de región del contrato."
    },
    "NumeroPersonaPaciente": {
      "type": "string",
      "description": "Número de persona del paciente. Opcional si se especifica NumeroSobre."
    }
  },
  "required": ["NumeroSobre", "NumeroContrato"]
}',
    Strict       = 1,
    BindingType  = N'Armonix',
    BindingConfig = N'{
  "baseUrl": "{api-armonix}",
  "method": "POST",
  "path": "/api/sobres/BuscarDocumentosCompleto",
  "paramMap": {},
  "bodyMap": ["NumeroSobre", "NumeroContrato", "CodigoProducto", "CodigoRegion", "NumeroPersonaPaciente"],
  "binaryField": "Contenido",
  "authMode": "saludsa-oauth"
}',
    IsActive     = 1,
    VersionNumber = 1
WHEN NOT MATCHED THEN INSERT
    (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES (
    N'obtener_documentos_sobre_armonix',
    N'obtener_documentos_sobre_armonix',
    N'Obtiene los documentos de un sobre de reembolso desde Armonix (MFiles). Devuelve la lista de documentos con nombre, extensión y contenido en base64. Identificados por número de sobre y contrato.',
    N'{
  "type": "object",
  "properties": {
    "NumeroSobre": {
      "type": "string",
      "description": "Número del sobre de reembolso en Armonix."
    },
    "NumeroContrato": {
      "type": "string",
      "description": "Número de contrato del afiliado."
    },
    "CodigoProducto": {
      "type": "string",
      "description": "Código del producto del contrato."
    },
    "CodigoRegion": {
      "type": "string",
      "description": "Código de región del contrato."
    },
    "NumeroPersonaPaciente": {
      "type": "string",
      "description": "Número de persona del paciente. Opcional si se especifica NumeroSobre."
    }
  },
  "required": ["NumeroSobre", "NumeroContrato"]
}',
    1,
    N'Armonix',
    N'{
  "baseUrl": "{api-armonix}",
  "method": "POST",
  "path": "/api/sobres/BuscarDocumentosCompleto",
  "paramMap": {},
  "bodyMap": ["NumeroSobre", "NumeroContrato", "CodigoProducto", "CodigoRegion", "NumeroPersonaPaciente"],
  "binaryField": "Contenido",
  "authMode": "saludsa-oauth"
}',
    1, 1, SYSUTCDATETIME()
);

COMMIT TRANSACTION;

-- ============================================================
-- Verificación post-seed
-- ============================================================
SELECT Code, Name, BindingType, IsActive,
       LEFT(BindingConfig, 60) + N'...' AS BindingConfigResumen
FROM   dbo.OPAITool
WHERE  Code IN (
    N'resolver_contrato_por_cedula',
    N'consultar_preexistencias_por_cedula',
    N'consultar_diagnosticos_preexistentes',
    N'consultar_coberturas_plan',
    N'consultar_deducible_contrato',
    N'consultar_deducibles_coberturas_plan',
    N'consultar_coberturas_plan_prestador',
    N'obtener_documentos_sobre_armonix'
)
ORDER BY Code;

-- ============================================================
-- EJEMPLO COMENTADO: cómo vincular tools a un agente de pruebas
-- Descomentar solo en dev/test para el agente con Code = 'MI_AGENTE_TEST'
-- NO hacer MERGE automático a agentes productivos.
-- ============================================================
/*
INSERT INTO dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT 'MI_AGENTE_TEST', t.Code, ROW_NUMBER() OVER (ORDER BY t.Code), 1
FROM dbo.OPAITool t
WHERE t.Code IN (
    'resolver_contrato_por_cedula',
    'consultar_preexistencias_por_cedula',
    'consultar_coberturas_plan',
    'consultar_deducible_contrato',
    'obtener_documentos_sobre_armonix'
)
AND NOT EXISTS (
    SELECT 1 FROM dbo.OPAIModelTool mt
    WHERE mt.ModelCode = 'MI_AGENTE_TEST' AND mt.ToolCode = t.Code
);
*/
