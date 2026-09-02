/* =============================================================================
   REQ-028 - Que dice su contrato, en las palabras del contrato
   -----------------------------------------------------------------------------
   Un afiliado no pregunta "que porcentaje me cubre el beneficio A003". Pregunta
   "hasta cuando puedo presentar esta factura", "me cubren si me pasa algo de
   viaje", "por que me descontaron de los honorarios del medico".

   Eso no esta en Pr05Beneficios: esta escrito, con letras, en
   Salud.dbo.Pr34CondicionesPlan. Muestra de lo que hay:

       "Periodo de presentacion de reclamos 90 dias, contados a partir de la
        fecha de emision de cada factura."
       "Cobertura las 24 horas del dia, los 365 dias del ano y en cualquier
        parte del mundo (fuera del pais no aplica credito)."
       "Los honorarios medicos seran los razonables y acostumbrados, tomando
        como limite la referencia de la Tabla Harvard, segun nivel contratado."

   -- Por que entra 'planmadre' ------------------------------------------------
   MEDIDO: de los 224.571 planes, solo UNO tiene filas en Pr34CondicionesPlan, y
   es 'planmadre'. No es un plan de nadie: es la plantilla, y sus 32 filas son
   las CONDICIONES GENERALES del contrato, las que aplican a todo el mundo.

   Filtrar solo por el plan del afiliado devolveria vacio casi siempre. Asi que
   se traen las dos cosas y cada fila dice en 'Alcance' si es general o
   particular, primero las particulares. Confundirlas seria decirle a alguien
   que una condicion es suya cuando es de todos, o al reves.

   -- El PDF del contrato -----------------------------------------------------
   Pr02Planes.AnexoContrato guarda la URL del contrato impreso (por ejemplo
   https://go.saludsa.com/rs/978-NOT-358/images/contrato-individual_.pdf). Solo
   433 planes la tienen, asi que muchas veces vendra vacia -y entonces NO se le
   promete al afiliado un enlace que no existe-.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @code nvarchar(100)   = N'condiciones_del_plan';
DECLARE @desc nvarchar(1000)  = N'Las condiciones del contrato del afiliado, en el texto que salio impreso: periodo para presentar reclamos, cobertura 24/7 y en el exterior, limite de honorarios por la tabla Harvard, preexistencias y credito. Trae las particulares del plan y las GENERALES del contrato, mas el ENLACE al PDF del contrato -si su version no lo trae, se busca en otra version del mismo plan o del mismo producto, porque es el mismo documento-. Si viene vacio, NO le prometas un enlace.';
DECLARE @schema nvarchar(max) = N'{
  "type": "object",
  "properties": {
    "codigoPlan": {
      "type": "string",
      "description": "Plan del afiliado. Si no se pasa, solo salen las condiciones generales."
    },
    "versionPlan": {
      "type": "string",
      "description": "Version del plan, para traer el PDF correcto del contrato."
    }
  },
  "required": []
}';
DECLARE @binding nvarchar(max) = N'{
  "connection": "SaludReclamos",
  "maxRows": 40,
  "query": "SELECT TOP 40 c.CodigoPlan, c.VersionPlan, c.CodigoCondicion, c.NombreCondicion AS Condicion, c.AplicaCarencias, c.AplicaDeducible, c.AplicaCoparticipalcion AS AplicaCopago, c.AplicaPreexistencia, c.MontoPreexistencia, c.AplicaCongenitas, c.CreditoAmbulatorio, c.CreditoHospitalario, c.AplicaTitular, c.AplicaConyuge, c.AplicaHijos, CASE WHEN c.CodigoPlan = ''planmadre'' THEN ''Condicion GENERAL del contrato: aplica a todos los planes.'' ELSE ''Condicion PARTICULAR de este plan.'' END AS Alcance, COALESCE(p.AnexoContrato, otra.AnexoContrato, mismoProd.AnexoContrato) AS PdfDelContrato, COALESCE(p.AnexoPlan, otra.AnexoPlan) AS PdfDelPlan, CASE WHEN p.AnexoContrato IS NOT NULL THEN ''de su plan y version'' WHEN otra.AnexoContrato IS NOT NULL THEN ''de otra version del mismo plan: es el MISMO documento'' WHEN mismoProd.AnexoContrato IS NOT NULL THEN ''el contrato estandar de su producto'' ELSE NULL END AS DeDondeSaleElPdf FROM Salud.dbo.Pr34CondicionesPlan c WITH (NOLOCK) LEFT JOIN Salud.dbo.Pr02Planes p WITH (NOLOCK) ON p.CodigoPlan = @codigoPlan AND p.VersionPlan = TRY_CAST(@versionPlan AS int) /* El PDF no es del afiliado: es el contrato impreso de su PRODUCTO -se llama contrato-individual_.pdf-. Solo 433 de los 224.571 planes tienen la URL en su propia fila, asi que si la version que le toca no la trae se busca en otra version del MISMO plan, y si tampoco, en el mismo producto. Es el mismo documento: no dárselo por estar en otra fila seria absurdo. */ OUTER APPLY (SELECT TOP 1 x.AnexoContrato, x.AnexoPlan FROM Salud.dbo.Pr02Planes x WITH (NOLOCK) WHERE x.CodigoPlan = @codigoPlan AND x.AnexoContrato IS NOT NULL AND LEN(x.AnexoContrato) > 10 ORDER BY ABS(x.VersionPlan - TRY_CAST(@versionPlan AS int))) otra OUTER APPLY (SELECT TOP 1 y.AnexoContrato FROM Salud.dbo.Pr02Planes y WITH (NOLOCK) WHERE y.CodigoProducto = (SELECT TOP 1 z.CodigoProducto FROM Salud.dbo.Pr02Planes z WITH (NOLOCK) WHERE z.CodigoPlan = @codigoPlan) AND y.AnexoContrato IS NOT NULL AND LEN(y.AnexoContrato) > 10 ORDER BY y.VersionPlan DESC) mismoProd WHERE c.CodigoPlan = @codigoPlan OR c.CodigoPlan = ''planmadre'' ORDER BY CASE WHEN c.CodigoPlan = @codigoPlan THEN 0 ELSE 1 END, c.CodigoCondicion"
}';

IF EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Code = @code)
    UPDATE dbo.OPAITool
       SET Name = @code, Description = @desc, InputSchema = @schema,
           BindingType = N'Sql', BindingConfig = @binding, IsActive = 1
     WHERE Code = @code;
ELSE
    INSERT dbo.OPAITool (Code, Name, Description, InputSchema, BindingType, BindingConfig,
                         Strict, IsActive, VersionNumber)
    VALUES (@code, @code, @desc, @schema, N'Sql', @binding, 0, 1, 1);

SELECT Code, IsActive, LEN(Description) AS TamDesc FROM dbo.OPAITool WHERE Code = @code;
