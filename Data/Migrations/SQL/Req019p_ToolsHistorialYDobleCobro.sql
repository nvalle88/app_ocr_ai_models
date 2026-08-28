/* =============================================================================
   REQ-019p — Tres herramientas para que el agente DEJE DE PEDIR lo que puede
              consultar por su cuenta
   -----------------------------------------------------------------------------
   PROBLEMA
     La auditoria medica cerraba con "Informacion faltante":
       · "no se adjunta liquidacion anterior ni desglose de valores liquidados
          por el consultor (ValorConsultor nulo)"
       · "no se dispone del detalle de facturacion del estudio de patologia
          para descartar doble cobro"
     Eso no se le pide al operador: esta en las tablas. Un auditor con acceso a
     la base no pregunta, consulta.

   LAS TRES HERRAMIENTAS
     1. historial_reembolsos_cliente_bd  (bdd_Salud_Consultas)
        Sobres anteriores del contrato: fecha, canal, estado legible, valor
        presentado y valor LIQUIDADO (suma de DetalleSobre.ValorConsultor).
        Responde "no se adjunta liquidacion anterior".

     2. consultar_liquidacion_sobre_bd   (bdd_Salud_Consultas)
        Desglose por detalle de UN sobre: presentado, ValorConsultor,
        ValorPendiente y las observaciones del consultor.
        Responde "no hay desglose de valores liquidados por el consultor".

     3. buscar_factura_repetida_bd       (db-nexus-test)
        Busca la MISMA factura (numero y/o RUC del emisor y/o valor) en OTROS
        casos ya tipificados por Nexus. Responde "para descartar doble cobro".

   POR QUE LA 3 VA CONTRA NUESTRA PROPIA BASE
     Se busco el numero de factura en bdd_Salud_Consultas y NO esta:
     DatosSobreApp.NumeroFactura es un INT que guarda la CANTIDAD de facturas
     del sobre (3, 4, 1...), no el numero; DetalleSobre no lo tiene. El numero
     de factura solo existe en el documento (M-Files) y, una vez pasado por
     OCR, en DocumentoClasificacion. Por eso el cruce anti-duplicados se hace
     sobre lo que Nexus ya tipifico — y hay que decirlo asi al agente, sin
     prometer una cobertura que no existe.

   COMPROBADO EN VIVO (2026-08-21, contrato 549616 / caso NA-2612602)
     · historial: 12 sobres previos, con estado legible (Liquidado, QPRA,
       Asignado, Reasignado) y canal (Reembolso electronico / Regestion online).
     · OJO: en PRUEBAS ValorConsultor viene 0 en todos los sobres. La consulta
       es correcta; el dato no esta poblado en este ambiente.
     · doble cobro: la factura 001-100-000000916 por $478.08 aparece en DOS
       casos Nexus distintos (03EE3DC0... y A55C8EC4...). La herramienta lo
       detecta.

   OJO CON OPAITool.Name
     Es el nombre que viaja a la API de Anthropic, no una etiqueta: debe cumplir
     ^[a-zA-Z0-9_-]{1,128}$. Por convencion de este catalogo, Name = Code. Poner
     un titulo legible con espacios revienta la llamada con
     "tools.0.custom.name: String should match pattern". El texto para humanos
     va en Description, que es lo que el modelo lee.

   Solo LECTURA: el ejecutor SQL solo admite SELECT de una sentencia.
   Idempotente y con guarda de base.
   ============================================================================= */

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    RAISERROR('Este script solo debe correr en db-nexus-test. Base actual: %s', 16, 1, @@SERVERNAME);
    RETURN;
END
GO

/* ---------------------------------------------------------------------------
   1) historial_reembolsos_cliente_bd
   --------------------------------------------------------------------------- */
DECLARE @sqlHist nvarchar(max) = N'
SELECT TOP (30)
       s.NumeroSobre,
       CONVERT(varchar(10), s.FechaRecepcion, 23)  AS FechaRecepcion,
       es.NombreEstado                             AS EstadoSobre,
       ses.NombreSubEstado                         AS SubEstado,
       e.NombreEstablecimiento                     AS Canal,
       s.ValorPresentado,
       SUM(ISNULL(d.ValorConsultor, 0))            AS ValorLiquidado,
       SUM(ISNULL(d.ValorPendiente, 0))            AS ValorPendiente,
       COUNT(d.IdDetalleSobre)                     AS Detalles
FROM dbo.Sobre s WITH (NOLOCK)
LEFT JOIN dbo.DetalleSobre    d   WITH (NOLOCK) ON d.IdSobre = s.IdSobre
LEFT JOIN dbo.EstadosSobre    es  WITH (NOLOCK) ON es.IdEstadoSobre = s.IdEstadoSobre
LEFT JOIN dbo.SubEstadosSobre ses WITH (NOLOCK) ON ses.IdSubEstadoSobre = s.IdSubEstadoSobre
LEFT JOIN dbo.Establecimiento e   WITH (NOLOCK) ON e.IdEstablecimiento = s.IdEstablecimiento
WHERE s.NumeroContrato = @numeroContrato
  AND (@numeroSobreExcluir IS NULL OR s.NumeroSobre <> @numeroSobreExcluir)
GROUP BY s.NumeroSobre, s.FechaRecepcion, es.NombreEstado, ses.NombreSubEstado,
         e.NombreEstablecimiento, s.ValorPresentado
ORDER BY s.FechaRecepcion DESC';

DECLARE @schHist nvarchar(max) = N'{
  "type": "object",
  "properties": {
    "numeroContrato": {
      "type": "string",
      "description": "Numero de contrato del afiliado (viene en el contexto del sobre)."
    },
    "numeroSobreExcluir": {
      "type": "string",
      "description": "Sobre que se esta analizando, para excluirlo del historial. Opcional."
    }
  },
  "required": ["numeroContrato"]
}';

MERGE dbo.OPAITool AS tgt
USING (SELECT 'historial_reembolsos_cliente_bd' AS Code) AS src ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    tgt.Name          = N'historial_reembolsos_cliente_bd',
    tgt.Description   = N'Sobres de reembolso ANTERIORES del mismo contrato, con su estado, canal, valor presentado y valor LIQUIDADO por el consultor. Usala para saber que se le liquido antes al cliente en vez de reportar "no se adjunta liquidacion anterior". NOTA: en el ambiente de PRUEBAS el campo ValorConsultor suele venir en 0.',
    tgt.InputSchema   = @schHist,
    tgt.BindingType   = 'Sql',
    tgt.BindingConfig = (SELECT 'SaludConsultas' AS connection, 30 AS maxRows, @sqlHist AS query FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
    tgt.IsActive      = 1,
    tgt.VersionNumber = ISNULL(tgt.VersionNumber, 0) + 1
WHEN NOT MATCHED THEN INSERT (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES ('historial_reembolsos_cliente_bd', N'historial_reembolsos_cliente_bd',
        N'Sobres de reembolso ANTERIORES del mismo contrato, con su estado, canal, valor presentado y valor LIQUIDADO por el consultor. Usala para saber que se le liquido antes al cliente en vez de reportar "no se adjunta liquidacion anterior". NOTA: en el ambiente de PRUEBAS el campo ValorConsultor suele venir en 0.',
        @schHist, 0, 'Sql',
        (SELECT 'SaludConsultas' AS connection, 30 AS maxRows, @sqlHist AS query FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
        1, 1, GETUTCDATE());
GO

/* ---------------------------------------------------------------------------
   2) consultar_liquidacion_sobre_bd
   --------------------------------------------------------------------------- */
DECLARE @sqlLiq nvarchar(max) = N'
SELECT TOP (50)
       s.NumeroSobre,
       d.IdDetalleSobre,
       d.NumeroPersona,
       d.IdTipoCobertura,
       d.ValorPresentadoDetalle,
       d.ValorConsultor,
       d.ValorPendiente,
       CONVERT(varchar(10), d.FechaIngresoDetalle, 23) AS FechaIngresoDetalle,
       CONVERT(varchar(10), d.FechaCambioEstado, 23)   AS FechaCambioEstado,
       d.ObservacionesConsultor,
       d.ObservacionesGestion,
       d.ClausulaNegativa,
       d.ClausulaDevolucion
FROM dbo.DetalleSobre d WITH (NOLOCK)
INNER JOIN dbo.Sobre s WITH (NOLOCK) ON s.IdSobre = d.IdSobre
WHERE s.NumeroSobre = @numeroSobre
ORDER BY d.IdDetalleSobre';

DECLARE @schLiq nvarchar(max) = N'{
  "type": "object",
  "properties": {
    "numeroSobre": {
      "type": "string",
      "description": "Numero del sobre cuyo desglose de liquidacion se quiere (p. ej. NA-2612602)."
    }
  },
  "required": ["numeroSobre"]
}';

MERGE dbo.OPAITool AS tgt
USING (SELECT 'consultar_liquidacion_sobre_bd' AS Code) AS src ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    tgt.Name          = N'consultar_liquidacion_sobre_bd',
    tgt.Description   = N'Detalle por linea de UN sobre: valor presentado, ValorConsultor (lo que el consultor liquido), ValorPendiente, clausulas de negativa/devolucion y las observaciones del consultor y de gestion. Usala para el desglose de lo liquidado, tanto del sobre actual como de uno anterior que hayas encontrado en el historial.',
    tgt.InputSchema   = @schLiq,
    tgt.BindingType   = 'Sql',
    tgt.BindingConfig = (SELECT 'SaludConsultas' AS connection, 50 AS maxRows, @sqlLiq AS query FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
    tgt.IsActive      = 1,
    tgt.VersionNumber = ISNULL(tgt.VersionNumber, 0) + 1
WHEN NOT MATCHED THEN INSERT (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES ('consultar_liquidacion_sobre_bd', N'consultar_liquidacion_sobre_bd',
        N'Detalle por linea de UN sobre: valor presentado, ValorConsultor (lo que el consultor liquido), ValorPendiente, clausulas de negativa/devolucion y las observaciones del consultor y de gestion. Usala para el desglose de lo liquidado, tanto del sobre actual como de uno anterior que hayas encontrado en el historial.',
        @schLiq, 0, 'Sql',
        (SELECT 'SaludConsultas' AS connection, 50 AS maxRows, @sqlLiq AS query FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
        1, 1, GETUTCDATE());
GO

/* ---------------------------------------------------------------------------
   3) buscar_factura_repetida_bd  (contra db-nexus-test: es donde vive el
      numero de factura, extraido por OCR)
   --------------------------------------------------------------------------- */
DECLARE @sqlDup nvarchar(max) = N'
SELECT TOP (40)
       c.NumeroFactura,
       c.EmisorRuc,
       c.EmisorNombre,
       c.ValorTotal,
       CONVERT(varchar(10), c.FechaEmision, 23) AS FechaEmision,
       f.OriginalName                           AS Documento,
       CONVERT(varchar(36), f.CaseCode)         AS CaseCode,
       CONVERT(varchar(19), c.CreatedDate, 120) AS Tipificado
FROM dbo.DocumentoClasificacion c WITH (NOLOCK)
INNER JOIN dbo.DataFile f WITH (NOLOCK) ON f.Id = c.DataFileId
WHERE c.IsCurrent = 1
  AND c.EsFacturaValida = 1
  AND (@caseCodeExcluir IS NULL OR CONVERT(varchar(36), f.CaseCode) <> @caseCodeExcluir)
  AND (
        (@numeroFactura IS NOT NULL AND c.NumeroFactura = @numeroFactura)
     OR (@emisorRuc    IS NOT NULL AND c.EmisorRuc      = @emisorRuc)
     OR (@valorTotal   IS NOT NULL AND ABS(c.ValorTotal - @valorTotal) < 0.05)
      )
ORDER BY c.CreatedDate DESC';

DECLARE @schDup nvarchar(max) = N'{
  "type": "object",
  "properties": {
    "numeroFactura": {
      "type": "string",
      "description": "Numero completo de la factura (p. ej. 001-100-000000916). Opcional, pero es el criterio mas fuerte."
    },
    "emisorRuc": {
      "type": "string",
      "description": "RUC del prestador que emite. Opcional: sirve para ver otras facturas del mismo emisor."
    },
    "valorTotal": {
      "type": "number",
      "description": "Valor total de la factura, para detectar el mismo gasto presentado con otro numero. Opcional (tolerancia 0.05)."
    },
    "caseCodeExcluir": {
      "type": "string",
      "description": "CaseCode del caso que se esta analizando, para no contarse a si mismo. Opcional."
    }
  },
  "required": []
}';

MERGE dbo.OPAITool AS tgt
USING (SELECT 'buscar_factura_repetida_bd' AS Code) AS src ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    tgt.Name          = N'buscar_factura_repetida_bd',
    tgt.Description   = N'Busca una factura ya tipificada por Nexus en OTROS casos, por numero, por RUC del emisor o por valor. Usala para descartar doble cobro en vez de reportar que falta informacion. LIMITE que debes declarar si aplica: solo cubre sobres que YA pasaron por Nexus (el numero de factura no existe en las tablas de Salud, solo en el documento y en el OCR).',
    tgt.InputSchema   = @schDup,
    tgt.BindingType   = 'Sql',
    tgt.BindingConfig = (SELECT 'OcrAiConnection' AS connection, 40 AS maxRows, @sqlDup AS query FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
    tgt.IsActive      = 1,
    tgt.VersionNumber = ISNULL(tgt.VersionNumber, 0) + 1
WHEN NOT MATCHED THEN INSERT (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES ('buscar_factura_repetida_bd', N'buscar_factura_repetida_bd',
        N'Busca una factura ya tipificada por Nexus en OTROS casos, por numero, por RUC del emisor o por valor. Usala para descartar doble cobro en vez de reportar que falta informacion. LIMITE que debes declarar si aplica: solo cubre sobres que YA pasaron por Nexus (el numero de factura no existe en las tablas de Salud, solo en el documento y en el OCR).',
        @schDup, 0, 'Sql',
        (SELECT 'OcrAiConnection' AS connection, 40 AS maxRows, @sqlDup AS query FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
        1, 1, GETUTCDATE());
GO

/* ---------------------------------------------------------------------------
   4) Habilitarlas para los agentes que las necesitan
      OPAIModelTool es la FRONTERA: una tool que no este aqui no existe para
      el agente, aunque este en el catalogo.
   --------------------------------------------------------------------------- */
;WITH nuevas ([ToolCode], [Order]) AS (
    SELECT 'historial_reembolsos_cliente_bd', 60 UNION ALL
    SELECT 'consultar_liquidacion_sobre_bd',  61 UNION ALL
    SELECT 'buscar_factura_repetida_bd',      62
),
modelos (ModelCode) AS (
    SELECT DISTINCT ModelCode FROM dbo.OPAIModelTool
)
MERGE dbo.OPAIModelTool AS tgt
USING (
    SELECT m.ModelCode, n.[ToolCode], n.[Order]
    FROM nuevas n CROSS JOIN modelos m
    JOIN dbo.OPAITool t ON t.Code = n.[ToolCode] AND t.IsActive = 1
) AS src
   ON tgt.ModelCode = src.ModelCode AND tgt.ToolCode = src.ToolCode
WHEN MATCHED THEN UPDATE SET tgt.[Order] = src.[Order], tgt.IsEnabled = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (ModelCode, ToolCode, [Order], IsEnabled)
    VALUES (src.ModelCode, src.[ToolCode], src.[Order], 1);
GO

/* ---------------------------------------------------------------------------
   Verificacion
   --------------------------------------------------------------------------- */
SELECT 'tools' AS q, Code, BindingType,
       JSON_VALUE(BindingConfig, '$.connection') AS Conexion,
       IsActive, VersionNumber
FROM dbo.OPAITool
WHERE Code IN ('historial_reembolsos_cliente_bd','consultar_liquidacion_sobre_bd','buscar_factura_repetida_bd');

SELECT 'links' AS q, ModelCode, ToolCode, [Order], IsEnabled
FROM dbo.OPAIModelTool
WHERE ToolCode IN ('historial_reembolsos_cliente_bd','consultar_liquidacion_sobre_bd','buscar_factura_repetida_bd')
ORDER BY ModelCode, [Order];
GO
