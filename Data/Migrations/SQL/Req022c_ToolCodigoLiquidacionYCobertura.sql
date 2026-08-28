/* =============================================================================
   REQ-022c - La cadena completa: texto del item -> codigo de liquidacion -> %
   -----------------------------------------------------------------------------
   El codigo que trae la factura es DEL PRESTADOR (CO-01, BP-01): cada uno usa
   sus propios IDs. El de Saludsa lo ponemos nosotros al liquidar.

   api-reembolso-automatico lo traduce con el tarifario del convenio:

       Saludsa.Tarifario.PrestacionPrestador
       CodigoPrestacionP  ->  CodigoPrestacionS  (= Lr05.CodigoHarvard)

   pero MEDIDO: solo 213 convenios tienen tarifario. Lo que no homologa cae al
   cajon MISCELANEO_LABORATORIO = 504001, que en 2026 lleva 30.235 lineas y
   $4.217.747 presentados.

   Nuestra propia factura de prueba es una de esas: 001-100-000000916, una
   colonoscopia de $358,08 y una biopsia de $120,00, se liquido en el reclamo
   2133124323 como CodigoProcedimiento 504001 / CodigoBeneficio A003
   LABORATORIO CLINICO. Una colonoscopia con los topes del laboratorio, porque
   el convenio del Dr. Munoz no tiene tarifario.

   Ahi es donde entra Nexus: homologar el TEXTO contra Lr05 -que es lo que ya
   hace HomologadorProcedimientos- y poner el codigo correcto.

   -- Lo que esta tool corrige de la anterior --------------------------------
   cobertura_beneficio_plan pedia el CodigoBeneficio como entrada, como si
   codigo y beneficio fueran dos busquedas. No lo son: en
   ReembolsoAutomaticoServices.cs ~6880 los dos salen de la MISMA fila de Lr05.

       var procedimiento = ObtenerProcedimientoPorCodigoHarvard(key);
       CodigoBeneficio     = procedimiento.CodigoBeneficio,
       CodigoProcedimiento = procedimiento.CodigoHarvard.ToString(),

   Asi que esta tool parte del procedimiento y saca las dos, mas el porcentaje,
   en una sola ida. La anterior queda desactivada: dos tools que contestan lo
   mismo acaban contestando distinto.

   -- Por que NUNCA devuelve vacio ----------------------------------------------
   Si el procedimiento no se identifica, la tool NO se calla: cataloga con el
   generico 504001 MISCELANEO LABORATORIO y lo marca EsGenerico = 1. Es lo mismo
   que hace api-reembolso-automatico cuando la correlacion no homologa
   (Procedimientos.MISCELANEO_LABORATORIO), y por una razon practica: devolver
   vacio PARA el caso, y un caso parado es un afiliado esperando. El generico lo
   deja seguir. Lo que no se hace es callarlo: el gasto queda con los topes del
   beneficio generico y NO con los del procedimiento real, y eso se dice.

   -- Por que entra codigoCobertura ---------------------------------------------
   api-liquidaciones no busca el beneficio como yo lo buscaba. Su llave real, en
   AccesoDatosCore/Planes/DatosBeneficioPlan.ObtenerBeneficio, son SEIS campos:

       Region + CodigoProducto + CodigoPlan + VersionPlan + CodigoCobertura
              + TipoCobertura IN ('AMBOS', el del reclamo) + CodigoBeneficio

   Faltaban CodigoCobertura -que ni existia como parametro- y Region, que yo
   trataba como opcional. Y no es cosmetico: MEDIDO sobre 3.000 llaves
   (plan+version+producto+beneficio), 1.066 -el 36%- tienen MAS DE UN porcentaje
   segun la cobertura, hasta cinco distintos. Sin ese campo, una de cada tres
   respuestas podia ser el porcentaje de otra cobertura.

   Cuando no se pasa, la tool NO elige: devuelve las filas y avisa AMBIGUO con el
   rango que hay. Elegir a ciegas en un dato de dinero es peor que no contestar.

   -- Un nombre que enganna ---------------------------------------------------
   En Lr04DetalleReclamo hay DOS columnas parecidas y no son lo mismo:
       CodigoProcedimiento = Lr05.CodigoHarvard        (504001, 99201, 221121)
       NumeroProcedimiento = la fila de Lr05           (7044,   5027,  9324)
   Verificado: la fila 7044 tiene Harvard 504001, y es 504001 el que aparece en
   el reclamo.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @code nvarchar(100)  = N'codigo_liquidacion_y_cobertura';
DECLARE @desc nvarchar(1000) = N'De un procedimiento homologado contra Lr05 devuelve en UNA ida a la base lo que necesita una liquidacion: el CodigoProcedimiento del reclamo (es el CodigoHarvard, NO el NumeroProcedimiento), el CodigoBeneficio de esa misma fila, y el porcentaje real del plan con topes y carencias. Si el procedimiento no se identifico NO devuelve vacio: cataloga con el generico 504001 y lo marca EsGenerico, igual que la liquidacion real. El porcentaje sale de Pr05Beneficios, nunca se inventa.';
DECLARE @schema nvarchar(max) = N'{
  "type": "object",
  "properties": {
    "numeroProcedimiento": {
      "type": "string",
      "description": "Fila de Lr05 que devolvio la homologacion del texto del item. Via preferida. Si la homologacion no identifico nada, NO lo inventes: dejalo vacio y la tool cataloga con el generico."
    },
    "codigoProcedimiento": {
      "type": "string",
      "description": "Codigo Harvard, si ya se conoce (p.ej. 504001). Alternativa a numeroProcedimiento. Si no se sabe, vacio."
    },
    "codigoPlan": {
      "type": "string",
      "description": "Plan del afiliado. Obligatorio: hay 53.000 planes."
    },
    "versionPlan": {
      "type": "string",
      "description": "Version del plan. El mismo plan cambia de porcentajes entre versiones."
    },
    "codigoProducto": {
      "type": "string",
      "description": "Producto del contrato: IND, COR, POO, EXP."
    },
    "tipoCobertura": {
      "type": "string",
      "description": "ambulatorio u hospitalario. Opcional."
    },
    "region": {
      "type": "string",
      "description": "Sierra o Costa. El motor la exige en su llave; sin ella la fila puede no ser la que el elegiria."
    },
    "prestadorEnConvenio": {
      "type": "string",
      "description": "true si el prestador tiene convenio. Resolverlo antes con resolver_convenio_por_ruc: cambia el porcentaje."
    },
    "esAccidente": {
      "type": "string",
      "description": "true si el reclamo es por accidente: entonces se aplica el porcentaje DE convenio aunque no lo tenga."
    },
    "codigoCobertura": {
      "type": "string",
      "description": "Codigo de cobertura del plan (INC01, PRX01, MAT01, CRN01...). IMPORTANTE: el motor lo exige en su llave, y medido, 1 de cada 3 llaves tiene porcentajes DISTINTOS segun la cobertura. Sin el, la tool avisa de ambiguedad en vez de elegir."
    }
  },
  "required": [
    "codigoPlan",
    "versionPlan",
    "codigoProducto"
  ]
}';
DECLARE @binding nvarchar(max) = N'{
  "connection": "SaludReclamos",
  "maxRows": 5,
  "query": "WITH objetivo AS ( /* 1) Lo que se pidio: por la fila de Lr05 o por el codigo Harvard. */ SELECT TOP 1 NumeroProcedimiento, CodigoHarvard, CodigoBeneficio, NombreEspanol, CAST(0 AS bit) AS EsGenerico FROM Salud.dbo.Lr05Procedimientos WITH (NOLOCK) WHERE NumeroProcedimiento = TRY_CAST(@numeroProcedimiento AS int) OR CodigoHarvard = TRY_CAST(@codigoProcedimiento AS int) UNION ALL /* 2) Si no se identifico nada, NO se devuelve vacio: se cataloga con el generico 504001 MISCELANEO LABORATORIO, que es exactamente lo que hace api-reembolso-automatico cuando la correlacion no homologa. Dejarlo en blanco pararia el caso; el generico lo deja seguir y VISIBLE. */ SELECT TOP 1 NumeroProcedimiento, CodigoHarvard, CodigoBeneficio, NombreEspanol, CAST(1 AS bit) AS EsGenerico FROM Salud.dbo.Lr05Procedimientos WITH (NOLOCK) WHERE CodigoHarvard = 504001 AND NOT EXISTS (SELECT 1 FROM Salud.dbo.Lr05Procedimientos WITH (NOLOCK) WHERE NumeroProcedimiento = TRY_CAST(@numeroProcedimiento AS int) OR CodigoHarvard = TRY_CAST(@codigoProcedimiento AS int)) ), cand AS ( SELECT o.NumeroProcedimiento, o.CodigoHarvard AS CodigoProcedimiento, o.NombreEspanol AS NombreProcedimiento, o.CodigoBeneficio, o.EsGenerico, cb.NombreBeneficio, b.CodigoPlan, b.VersionPlan, b.CodigoProducto, b.CodigoCobertura, b.TipoCobertura, b.Region, b.PorcentajeConConvenio, b.PorcentajeSinConvenio, b.PorcentajeRedEspecifica, b.PorcentajeOtrosNoAfiliados, b.PorcentajeExceso, b.MontoPorPrestacion, b.CantidadPorPrestacion, b.PeriodoMonto, b.PeriodoCantidad, b.AplicaDeducible, b.AplicaCarencia, b.DiasCarenciaBeneficios, b.DiasReclamo, b.EdadDesde, b.EdadHasta, b.BeneficioGenero, b.TopaProcedimiento, b.ValorProcedimiento, b.CodigoSecuencia, a.Pct, a.Por FROM objetivo o LEFT JOIN Salud.dbo.Pr07CatalogoBeneficios cb WITH (NOLOCK) ON cb.CodigoBeneficio = o.CodigoBeneficio LEFT JOIN Salud.dbo.Pr05Beneficios b WITH (NOLOCK) ON b.CodigoBeneficio = o.CodigoBeneficio AND b.CodigoPlan = @codigoPlan AND b.VersionPlan = TRY_CAST(@versionPlan AS int) AND b.CodigoProducto = @codigoProducto AND (@codigoCobertura IS NULL OR LEN(@codigoCobertura) = 0 OR b.CodigoCobertura = @codigoCobertura) AND (@tipoCobertura IS NULL OR LEN(@tipoCobertura) = 0 OR b.TipoCobertura = @tipoCobertura OR b.TipoCobertura = ''Ambos'') AND (@region IS NULL OR LEN(@region) = 0 OR b.Region = @region) CROSS APPLY ( SELECT CASE WHEN LOWER(ISNULL(@prestadorEnConvenio,'''')) IN (''1'',''true'',''si'') THEN b.PorcentajeConConvenio WHEN LOWER(ISNULL(@esAccidente,'''')) IN (''1'',''true'',''si'') THEN b.PorcentajeConConvenio ELSE b.PorcentajeSinConvenio END AS Pct, CASE WHEN LOWER(ISNULL(@prestadorEnConvenio,'''')) IN (''1'',''true'',''si'') THEN ''prestador EN convenio con Saludsa'' WHEN LOWER(ISNULL(@esAccidente,'''')) IN (''1'',''true'',''si'') THEN ''sin convenio, pero el reclamo es por ACCIDENTE: se aplica el porcentaje de convenio'' ELSE ''prestador SIN convenio (si el convenio no se verifico, verificarlo: cambia el porcentaje)'' END AS Por ) a ) SELECT TOP 5 NumeroProcedimiento, CodigoProcedimiento, NombreProcedimiento, CodigoBeneficio, NombreBeneficio, EsGenerico, CodigoPlan, VersionPlan, CodigoProducto, CodigoCobertura, TipoCobertura, Region, PorcentajeConConvenio, PorcentajeSinConvenio, PorcentajeRedEspecifica, PorcentajeOtrosNoAfiliados, PorcentajeExceso, MontoPorPrestacion, CantidadPorPrestacion, PeriodoMonto, PeriodoCantidad, AplicaDeducible, AplicaCarencia, DiasCarenciaBeneficios, DiasReclamo, EdadDesde, EdadHasta, BeneficioGenero, TopaProcedimiento, ValorProcedimiento, Pct AS PorcentajeQueAplica, Por AS PorQueEsePorcentaje, CASE WHEN EsGenerico = 1 THEN ''GENERICO: no se identifico el procedimiento, asi que se cataloga como 504001 MISCELANEO LABORATORIO, que es lo que hace la liquidacion real cuando no homologa. El gasto sigue, pero con los topes del beneficio generico y NO los del procedimiento real. Decirlo, no callarlo.'' WHEN CodigoPlan IS NULL THEN ''El plan no lista este beneficio. No es un 0%: es que no hay fila. Revisar plan, version, producto y region antes de concluir nada.'' WHEN (@codigoCobertura IS NULL OR LEN(@codigoCobertura) = 0) AND MIN(Pct) OVER (PARTITION BY CodigoBeneficio) <> MAX(Pct) OVER (PARTITION BY CodigoBeneficio) THEN ''AMBIGUO: no se paso codigoCobertura y este beneficio tiene porcentajes DISTINTOS segun la cobertura (de '' + CONVERT(varchar(12), MIN(Pct) OVER (PARTITION BY CodigoBeneficio)) + '' a '' + CONVERT(varchar(12), MAX(Pct) OVER (PARTITION BY CodigoBeneficio)) + ''). Pasar codigoCobertura: pasa en 1 de cada 3 llaves.'' WHEN Pct IS NULL THEN ''SIN DATO: la casilla viene vacia en el plan. No inventar un porcentaje: escalar.'' WHEN Pct > 100 THEN ''NO USAR: valor mayor que 100 en una casilla de porcentaje, asi que no es un porcentaje. Hay 1.867 filas asi en 680 planes. Aplicarlo pagaria mas que la factura. Escalar, no pagar.'' WHEN Pct = 0 THEN ''El plan cubre 0% este beneficio por esta via. Es una respuesta valida: NO cubre.'' ELSE NULL END AS Alerta, ''CodigoProcedimiento es el CodigoHarvard de Lr05: el que la liquidacion escribe en Lr04DetalleReclamo. NumeroProcedimiento es la fila de Lr05, NO es lo mismo. El motor (api-liquidaciones, DatosBeneficioPlan.ObtenerBeneficio) busca por Region + Producto + Plan + Version + CodigoCobertura + TipoCobertura + Beneficio. Resueltas las ramas de convenio y accidente; NO evaluadas: coordinacion de beneficios, exceso, beneficio propio del prestador, convenio ALIADO, castigo por nivel y castigo Veris.'' AS Advertencia FROM cand ORDER BY CASE WHEN TipoCobertura = ''Ambos'' THEN 1 ELSE 0 END, CodigoBeneficio, CodigoSecuencia"
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

/* La anterior se retira: preguntaba lo mismo por una via peor. */
UPDATE dbo.OPAITool SET IsActive = 0 WHERE Code = N'cobertura_beneficio_plan';

/* Enlace a los tres agentes que resuelven un reembolso. */
INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT g.Code, @code,
       ISNULL((SELECT MAX(mt.[Order]) FROM dbo.OPAIModelTool mt WHERE mt.ModelCode = g.Code), 0) + 1, 1
  FROM dbo.Agent g
 WHERE g.Code IN (N'AGENTE_AUDITOR_MEDICINA', N'AGENTE_CLAUDE', N'AGENTE_PORTAL_CLIENTE')
   AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt
                    WHERE mt.ModelCode = g.Code AND mt.ToolCode = @code);

UPDATE dbo.OPAIModelTool SET IsEnabled = 0 WHERE ToolCode = N'cobertura_beneficio_plan';

SELECT t.Code, t.IsActive, (SELECT COUNT(*) FROM dbo.OPAIModelTool mt
                             WHERE mt.ToolCode = t.Code AND mt.IsEnabled = 1) AS AgentesActivos
  FROM dbo.OPAITool t
 WHERE t.Code IN (N'codigo_liquidacion_y_cobertura', N'cobertura_beneficio_plan');
