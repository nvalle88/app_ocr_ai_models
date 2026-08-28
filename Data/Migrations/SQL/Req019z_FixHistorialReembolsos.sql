/* =============================================================================
   REQ-019z — La tool historial_reembolsos_cliente_bd fallaba en 0,1s
   -----------------------------------------------------------------------------
   Error real (guardado en ToolInvocation.ResponseJson):
       Invalid column name 'IdSubEstadoSobre'
       Invalid column name 'NombreSubEstado'  (x2: SELECT y GROUP BY)

   Causa: el JOIN al catalogo de subestados se escribio suponiendo la
   convencion Id<Tabla>/Nombre<Tabla>, y esta tabla no la sigue:

       dbo.Sobre           -> IdSubEstadoSobre   (correcto, si existe)
       dbo.SubEstadosSobre -> Id, Descripcion    (NO IdSubEstadoSobre/NombreSubEstado)

   Como el SQL se compila entero, el fallo era inmediato y la tool nunca
   devolvia nada: el agente se quedaba sin el historial del cliente y resolvia
   el caso sin saber si ese contrato ya habia presentado sobres antes.

   El subestado merece conservarse: sus 5 valores son Fusionado, Duplicado,
   Error de cliente, Rechazado y Pruebas — exactamente las senales que hay que
   mirar en un historial de reembolsos.

   Verificado contra salud37/bdd_Salud_Consultas: 30 sobres para el contrato
   1398083 y sp_describe_first_result_set resuelve el esquema sin errores.
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

DECLARE @query nvarchar(max) = N'
SELECT TOP (30)
       s.NumeroSobre,
       CONVERT(varchar(10), s.FechaRecepcion, 23)  AS FechaRecepcion,
       es.NombreEstado                             AS EstadoSobre,
       ses.Descripcion                             AS SubEstado,
       e.NombreEstablecimiento                     AS Canal,
       s.ValorPresentado,
       SUM(ISNULL(d.ValorConsultor, 0))            AS ValorLiquidado,
       SUM(ISNULL(d.ValorPendiente, 0))            AS ValorPendiente,
       COUNT(d.IdDetalleSobre)                     AS Detalles
FROM dbo.Sobre s WITH (NOLOCK)
LEFT JOIN dbo.DetalleSobre    d   WITH (NOLOCK) ON d.IdSobre = s.IdSobre
LEFT JOIN dbo.EstadosSobre    es  WITH (NOLOCK) ON es.IdEstadoSobre    = s.IdEstadoSobre
LEFT JOIN dbo.SubEstadosSobre ses WITH (NOLOCK) ON ses.Id              = s.IdSubEstadoSobre
LEFT JOIN dbo.Establecimiento e   WITH (NOLOCK) ON e.IdEstablecimiento = s.IdEstablecimiento
WHERE s.NumeroContrato = @numeroContrato
  AND (@numeroSobreExcluir IS NULL OR s.NumeroSobre <> @numeroSobreExcluir)
GROUP BY s.NumeroSobre, s.FechaRecepcion, es.NombreEstado, ses.Descripcion,
         e.NombreEstablecimiento, s.ValorPresentado
ORDER BY s.FechaRecepcion DESC';

UPDATE dbo.OPAITool
   SET BindingConfig = JSON_MODIFY(BindingConfig, '$.query', @query),
       VersionNumber = ISNULL(VersionNumber, 1) + 1
 WHERE Code = 'historial_reembolsos_cliente_bd';

SELECT 'historial_reembolsos_cliente_bd' AS Tool,
       CASE WHEN CHARINDEX('ses.Descripcion', BindingConfig) > 0
                 AND CHARINDEX('NombreSubEstado', BindingConfig) = 0
                 AND CHARINDEX('ses.Id ', BindingConfig) > 0
            THEN 'CORREGIDA' ELSE 'PENDIENTE' END AS Estado,
       VersionNumber
  FROM dbo.OPAITool
 WHERE Code = 'historial_reembolsos_cliente_bd';
GO
