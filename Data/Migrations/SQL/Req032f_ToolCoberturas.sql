/* =============================================================================
   REQ-032f - Las coberturas del plan y el monto disponible de cada una
   -----------------------------------------------------------------------------
   Nestor: "lo de las coberturas del plan, montos disponibles por cada cosa,
   como maternidad". Estaba en el mapa de motivos como pendiente: topes de
   cobertura (58 llamadas), monto disponible (25) y terapias anuales (11).

   Vive en Pr04Coberturas: una fila por cobertura x plan x version x region, con
   MontoCobertura, PeriodoCobertura y los dias de carencia ambulatoria y
   hospitalaria. El plan N5-C v32 Sierra tiene 28.

   -- Dos cosas medidas que habrian dado una respuesta falsa --------------
   1. TituloPantalla, que parecia el nombre legible, esta VACIO en las 13.110
      filas del plan. El nombre vive en Pr10CatalogoCoberturas. Sin ese JOIN la
      respuesta seria "MAT01: 500,00", que no le dice nada a nadie. Con el:
      "MATERNIDAD, 100.000,00 al anio, carencia 45 dias".

   2. MontoCobertura = 999999 no es un tope: es como se guarda el "sin limite",
      y lo llevan 9.717 de las 13.110 filas. Enseñarlo tal cual le haria creer al
      afiliado que tiene un techo de un millon de dolares. Se devuelve vacio y se
      dice en SobreElTope.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @code nvarchar(100)    = N'coberturas_y_topes_del_plan';
DECLARE @desc nvarchar(1000)   = N'Las COBERTURAS del plan del afiliado con su MONTO MAXIMO disponible y su carencia: maternidad, emergencia, discapacidad, cuidados paliativos, recien nacido... Dice cuanto tiene, cada cuanto se repone y cuantos dias de carencia lleva cada una. Un monto en blanco significa SIN TOPE, no cero: lee SobreElTope. Necesita el plan, y con version y region da lo suyo exacto.';
DECLARE @schema nvarchar(max)  = N'{
  "type": "object",
  "properties": {
    "codigoPlan": {
      "type": "string",
      "description": "OBLIGATORIO. Codigo del plan del afiliado, del contrato que viene al principio del mensaje. Sin el serian las coberturas de los 53.000 planes, y la pregunta es que cubre EL SUYO."
    },
    "version": {
      "type": "string",
      "description": "Version del plan, del contrato resuelto. MANDALA: cada version tiene sus propios topes y sin ella salen mezcladas las de todas."
    },
    "codigoProducto": {
      "type": "string",
      "description": "Producto del contrato: IND, RTM, CORP..."
    },
    "region": {
      "type": "string",
      "description": "Region del contrato: Costa o Sierra. Los topes cambian por region."
    },
    "cobertura": {
      "type": "string",
      "description": "Para preguntar por UNA: ''maternidad'', ''emergencia'', ''discapacidad''. Busca en el nombre y en el codigo. Sin esto salen todas las del plan, con las que tienen tope primero."
    }
  },
  "required": [
    "codigoPlan"
  ]
}';
DECLARE @binding nvarchar(max) = N'{
  "connection": "SaludReclamos",
  "maxRows": 40,
  "query": "SELECT TOP 40 cob.CodigoCobertura, /* El nombre legible. Sin el, esto es \"MAT01: 500,00\" y no le dice nada a nadie. OJO: TituloPantalla de la propia tabla NO sirve — medido, esta vacio en las 13.110 filas del plan. El nombre vive en su catalogo. */ cat.NombreCobertura AS Cobertura, cob.TipoCobertura AS AmbulatorioUHospitalario, /* 999999 no es un tope: es como se guarda \"sin limite\". Medido: 9.717 de las 13.110 filas del plan lo llevan. Enseñarlo tal cual le haria creer al afiliado que tiene un techo de un millon de dolares. */ CASE WHEN cob.MontoCobertura >= 999999 THEN NULL ELSE cob.MontoCobertura END AS MontoMaximo, CASE WHEN cob.MontoCobertura >= 999999 THEN ''sin tope: el limite lo pone la cobertura general del plan'' END AS SobreElTope, CASE WHEN cob.PeriodoCobertura = 365 THEN ''al anio'' WHEN cob.PeriodoCobertura = 1 THEN ''por evento'' WHEN cob.PeriodoCobertura IS NULL OR cob.PeriodoCobertura = 0 THEN NULL ELSE ''cada '' + CONVERT(varchar(8), cob.PeriodoCobertura) + '' dias'' END AS CadaCuanto, cob.DiasCarenciaAmb AS CarenciaAmbulatoriaDias, cob.DiasCarenciaHosp AS CarenciaHospitalariaDias, cob.CodigoPlan, cob.VersionPlan, cob.Region, cob.CodigoProducto, CONVERT(varchar(10), cob.FechaInicioCobertura, 120) AS VigenteDesde FROM Salud.dbo.Pr04Coberturas cob WITH (NOLOCK) LEFT JOIN Salud.dbo.Pr10CatalogoCoberturas cat WITH (NOLOCK) ON cat.CodigoCobertura = cob.CodigoCobertura /* El plan es obligatorio: sin el esto son las coberturas de los 53.000 planes. Y ademas no significaria nada: la pregunta es \"que cubre MI plan\". */ WHERE cob.CodigoPlan = @codigoPlan AND (@version IS NULL OR LEN(@version) = 0 OR cob.VersionPlan = TRY_CAST(@version AS int)) AND (@codigoProducto IS NULL OR LEN(@codigoProducto) = 0 OR cob.CodigoProducto = @codigoProducto) AND (@region IS NULL OR LEN(@region) = 0 OR cob.Region = @region) /* Vigente hoy: un tope que caduco no es el suyo. */ AND (cob.FechaFinCobertura IS NULL OR cob.FechaFinCobertura >= CAST(GETDATE() AS date)) /* Por nombre, para cuando pregunta por una en concreto: \"maternidad\". */ AND (@cobertura IS NULL OR LEN(@cobertura) = 0 OR cat.NombreCobertura LIKE ''%'' + @cobertura + ''%'' OR cob.CodigoCobertura LIKE ''%'' + @cobertura + ''%'') /* Las que tienen tope primero: son las que el afiliado necesita saber. Una cobertura sin limite no le preocupa. */ ORDER BY CASE WHEN cob.MontoCobertura >= 999999 THEN 1 ELSE 0 END, cat.NombreCobertura, cob.CodigoCobertura"
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

INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT g.Code, @code,
       ISNULL((SELECT MAX(mt.[Order]) FROM dbo.OPAIModelTool mt WHERE mt.ModelCode = g.Code), 0) + 1, 1
  FROM dbo.Agent g
 WHERE g.Code IN (N'AGENTE_CHAT_CLIENTE', N'AGENTE_AUDITOR_MEDICINA', N'AGENTE_CLAUDE')
   AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt
                    WHERE mt.ModelCode = g.Code AND mt.ToolCode = @code);

SELECT mt.ModelCode, mt.ToolCode FROM dbo.OPAIModelTool mt WHERE mt.ToolCode = @code ORDER BY mt.ModelCode;
