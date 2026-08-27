/* =============================================================================
   REQ-021b - Darle las tres tools nuevas a quien las necesita
   -----------------------------------------------------------------------------
   Se enlazan a los dos agentes que hoy ya usan buscar_factura_repetida_bd,
   porque son exactamente los que se hacen la pregunta que esa tool no sabe
   responder:

     AGENTE_AUDITOR_MEDICINA  - decide si el gasto se sostiene
     AGENTE_CLAUDE            - resuelve el reembolso

   El orden se coloca justo detras de buscar_factura_repetida_bd para que el
   modelo lea las tres seguidas y vea la secuencia: mirar el repositorio, traer
   del SRI si falta, y solo entonces preguntar por los reclamos.

   NO se enlazan a AGENTE_PORTAL_CLIENTE: ese agente solo resuelve el contrato
   del afiliado por su cedula y no tiene nada que hacer con comprobantes.

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

/* El orden de partida: justo despues de la tool de duplicidad que ya tenian. */
DECLARE @tools TABLE (ToolCode varchar(100), Salto int);
INSERT INTO @tools (ToolCode, Salto) VALUES
    ('obtener_factura_repositorio', 1),
    ('cargar_factura_desde_sri',    2),
    ('factura_ya_pagada_bd',        3);

INSERT INTO dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT m.ModelCode, t.ToolCode,
       ISNULL((SELECT MAX(x.[Order]) FROM dbo.OPAIModelTool x WHERE x.ModelCode = m.ModelCode), 0) + t.Salto,
       1
  FROM (SELECT DISTINCT ModelCode
          FROM dbo.OPAIModelTool
         WHERE ToolCode = 'buscar_factura_repetida_bd') m
 CROSS JOIN @tools t
 WHERE NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool e
                    WHERE e.ModelCode = m.ModelCode AND e.ToolCode = t.ToolCode);
GO

/* ---------------------------------------------------------------------------
   Verificacion
   --------------------------------------------------------------------------- */
SELECT mt.ModelCode, mt.ToolCode, mt.[Order], mt.IsEnabled
  FROM dbo.OPAIModelTool mt
 WHERE mt.ToolCode IN ('obtener_factura_repositorio', 'cargar_factura_desde_sri',
                       'factura_ya_pagada_bd', 'buscar_factura_repetida_bd')
 ORDER BY mt.ModelCode, mt.[Order];
GO
