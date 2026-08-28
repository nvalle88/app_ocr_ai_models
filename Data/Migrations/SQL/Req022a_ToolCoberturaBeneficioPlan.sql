/* =============================================================================
   REQ-022 - De donde sale el porcentaje de cobertura
   -----------------------------------------------------------------------------
   Hasta hoy el porcentaje que veia el afiliado lo escribia el MODELO en su JSON:

       ClienteController.cs:906   Porcentaje = D(x, "porcentaje")

   Nadie lo contrastaba contra ninguna tabla, y por eso el caso 9da6b9f4 pudo
   decir que de $478,08 presentados se cubrian $478,08 -el 100%- sin que
   chirriara nada. Era prosa con formato de numero.

   -- Por que no sirve el codigo que trae la factura ---------------------------
   La factura electronica trae CodigoProcedimiento, pero no es un campo del SRI:
   vive en Detalles[].DetallesAdicionales[], que el prestador llena a mano. En la
   factura 001-100-000000916 del Dr. Munoz las DOS lineas traen el mismo 99201:

       CO-01  COLONOSCOPIA (VCC)   ->  99201
       BP-01  BIOPSIA              ->  99201

   y 99201 en CPT es "consulta de consultorio, paciente nuevo". Ni colonoscopia
   (45378) ni biopsia (45380). Es relleno. Liquidar con ese numero seria pagar
   dos procedimientos como si fueran dos consultas.

   -- De donde sale entonces --------------------------------------------------
   Del catalogo, homologando el TEXTO del item, que es lo que ya hace
   HomologadorProcedimientos contra Lr05Procedimientos (37.448 filas) y valida
   contra Lr46CorrelacionDXProcedimiento (583.590). Eso devuelve CodigoBeneficio.

   Ese CodigoBeneficio ya se calculaba... y no llegaba a ninguna parte. Esta tool
   es el cable que faltaba: CodigoBeneficio + plan del afiliado -> Pr05Beneficios.

   -- Por que hay que acertar el beneficio ------------------------------------
   Medido en Lr05: "colonoscopia" no es un beneficio, son tres.

       COLONOSCOPIA GYE .......................... H001
       SALAS COLONOSCOPIA ........................ H012
       ENDOSCOPIA+COLONOSCOPIA, TAC, PEDIATRICA,
       COLONOSCOPIA + POLIPECTOMIA ............... A005

   Por eso el homologador devuelve Score y marca Ambigua: cuando empata, la
   respuesta correcta es decirlo, no elegir.

   -- Por que la tool NO decide sola el porcentaje ----------------------------
   El motor de verdad es api-liquidaciones, CalculosLiquidaciones.cs, metodo
   ObtenerPorcentajeCobertura (lineas 1630-1745). Tiene diez ramas:

       1  coordinacion de beneficios ................. 100% y sale
       2  porcentaje aplicado desde pantalla ......... el que puso el auditor
       3  exceso ..................................... PorcentajeExceso
       4  en convenio, con beneficio propio .......... beneficioPrestador
       5  en convenio, sin beneficio propio .......... CalcularPorcentajeCoberturaArx
       6  sin convenio + ACCIDENTE ................... PorcentajeConConvenio
       7  sin convenio, resto ........................ PorcentajeSinConvenio
       8  convenio ALIADO y no hospital .............. sube
       9  Individual/Experience fuera de oficina ..... castigo por nivel
      10  Veris nivel 3 .............................. porcentaje parametrizado

   Replicar las diez aqui seria un SEGUNDO motor de dinero, y dos motores acaban
   discrepando. Cuando discrepen, nadie sabra cual miente.

   Asi que esta tool hace lo que si es solido: LEE la fila real, resuelve las
   ramas 6 y 7 -las unicas que se pueden decidir con lo que se observa en un
   reembolso- y DECLARA las que no puede evaluar, en vez de rellenarlas.

   -- Un guardarrail medido --------------------------------------------------
   De 26,9 millones de filas de Pr05Beneficios, 1.867 traen PorcentajeConConvenio
   MAYOR que 100 -hasta 8.060-, repartidas en 41 beneficios y 680 planes. Los
   valores se agrupan en cifras redondas (120, 300, 600, 700, 800) que parecen
   montos, pero NO es eso: solo 56 de las 1.867 coinciden con MontoPorPrestacion.
   Que significan no esta establecido, y esta tool no lo adivina. Lo unico seguro
   es que no son porcentajes y que aplicarlos pagaria MAS que la factura, asi que
   la tool los detecta, lo dice y no los usa.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @code nvarchar(100)  = N'cobertura_beneficio_plan';
DECLARE @desc nvarchar(1000) = N'Del CodigoBeneficio que devuelve la homologacion del item, mas el plan del afiliado, trae la fila REAL de Pr05Beneficios: los cinco porcentajes, topes, deducible, carencias y limites de edad/sexo. Dice CUAL porcentaje aplica y por que. El porcentaje NUNCA se inventa: sale de esta tabla. Exige codigoPlan, versionPlan y codigoProducto exactos, hay 53.000 planes. Si la fila trae un porcentaje mayor que 100 es un error de digitacion del maestro: avisa y NO se usa.';

DECLARE @schema nvarchar(max) = N'{
  "type": "object",
  "properties": {
    "codigoBeneficio": { "type": "string", "description": "Codigo del beneficio, p.ej. A005 o H001. Sale de homologar el TEXTO del item contra Lr05, NUNCA del CodigoProcedimiento de la factura, que el prestador rellena a mano." },
    "codigoPlan":      { "type": "string", "description": "Codigo del plan del afiliado. Obligatorio: hay 53.000 planes y sin el la respuesta no significa nada." },
    "versionPlan":     { "type": "string", "description": "Version del plan, numero entero. Obligatorio: el mismo plan cambia de porcentajes entre versiones." },
    "codigoProducto":  { "type": "string", "description": "Producto del contrato, p.ej. IND, COR, POO, EXP." },
    "tipoCobertura":   { "type": "string", "description": "ambulatorio u hospitalario. Opcional; si se omite trae ambos. Las filas Ambos siempre entran." },
    "region":          { "type": "string", "description": "Sierra o Costa. Opcional." },
    "prestadorEnConvenio": { "type": "string", "description": "true si el prestador que emite la factura tiene convenio con Saludsa. Resolverlo antes con resolver_convenio_por_ruc. Si no se sabe, va false y la tool lo advierte." },
    "esAccidente":     { "type": "string", "description": "true si el reclamo es por accidente. Cambia el porcentaje: sin convenio pero por accidente se aplica el DE convenio." }
  },
  "required": ["codigoBeneficio", "codigoPlan", "versionPlan", "codigoProducto"]
}';

DECLARE @binding nvarchar(max) = N'{
  "connection": "SaludReclamos",
  "maxRows": 5,
  "query": "SELECT TOP 5 b.CodigoPlan, b.VersionPlan, b.CodigoProducto, b.CodigoBeneficio, cb.NombreBeneficio, b.TipoCobertura, b.Region, b.PorcentajeConConvenio, b.PorcentajeSinConvenio, b.PorcentajeRedEspecifica, b.PorcentajeOtrosNoAfiliados, b.PorcentajeExceso, b.MontoPorPrestacion, b.CantidadPorPrestacion, b.PeriodoMonto, b.PeriodoCantidad, b.AplicaDeducible, b.AplicaCarencia, b.DiasCarenciaBeneficios, b.DiasReclamo, b.EdadDesde, b.EdadHasta, b.BeneficioGenero, b.TopaProcedimiento, b.ValorProcedimiento, b.FechaInicioCobertura, b.FechaFinCobertura, a.Pct AS PorcentajeQueAplica, a.Por AS PorQueEsePorcentaje, CASE WHEN a.Pct IS NULL THEN ''SIN DATO: esa casilla viene vacia en el plan. No inventar un porcentaje: escalar.'' WHEN a.Pct > 100 THEN ''NO USAR: valor mayor que 100 en una casilla de porcentaje, asi que no es un porcentaje. Hay 1.867 filas asi en 680 planes; que significan no esta establecido. Aplicarlo pagaria mas que la factura. Escalar, no pagar.'' WHEN a.Pct = 0 THEN ''El plan cubre 0% este beneficio por esta via. Es una respuesta valida: NO cubre.'' ELSE NULL END AS Alerta, ''Resueltas las ramas de convenio y accidente. NO evaluadas aqui: coordinacion de beneficios, exceso, beneficio propio del prestador, convenio ALIADO, castigo por nivel en Individual/Experience y castigo Veris nivel 3. Si alguna aplica, el porcentaje final lo fija api-liquidaciones.'' AS Advertencia FROM Salud.dbo.Pr05Beneficios b WITH (NOLOCK) LEFT JOIN Salud.dbo.Pr07CatalogoBeneficios cb WITH (NOLOCK) ON cb.CodigoBeneficio = b.CodigoBeneficio CROSS APPLY ( SELECT CASE WHEN LOWER(ISNULL(@prestadorEnConvenio,'''')) IN (''1'',''true'',''si'') THEN b.PorcentajeConConvenio WHEN LOWER(ISNULL(@esAccidente,'''')) IN (''1'',''true'',''si'') THEN b.PorcentajeConConvenio ELSE b.PorcentajeSinConvenio END AS Pct, CASE WHEN LOWER(ISNULL(@prestadorEnConvenio,'''')) IN (''1'',''true'',''si'') THEN ''prestador EN convenio con Saludsa'' WHEN LOWER(ISNULL(@esAccidente,'''')) IN (''1'',''true'',''si'') THEN ''sin convenio, pero el reclamo es por ACCIDENTE: se aplica el porcentaje de convenio'' ELSE ''prestador SIN convenio (si el convenio no se verifico, verificarlo: cambia el porcentaje)'' END AS Por ) a WHERE b.CodigoBeneficio = @codigoBeneficio AND b.CodigoPlan = @codigoPlan AND b.VersionPlan = TRY_CAST(@versionPlan AS int) AND b.CodigoProducto = @codigoProducto AND (@tipoCobertura IS NULL OR LEN(@tipoCobertura) = 0 OR b.TipoCobertura = @tipoCobertura OR b.TipoCobertura = ''Ambos'') AND (@region IS NULL OR LEN(@region) = 0 OR b.Region = @region) ORDER BY CASE WHEN b.TipoCobertura = ''Ambos'' THEN 1 ELSE 0 END, b.CodigoSecuencia"
}';

IF LEN(@desc) > 500
BEGIN
    RAISERROR('La descripcion tiene mas de 500 caracteres, que es el limite REAL de la columna.', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Code = @code)
BEGIN
    UPDATE dbo.OPAITool
       SET Name = @code, Description = @desc, InputSchema = @schema,
           BindingType = N'Sql', BindingConfig = @binding, IsActive = 1
     WHERE Code = @code;
    PRINT 'Tool actualizada: cobertura_beneficio_plan';
END
ELSE
BEGIN
    INSERT dbo.OPAITool (Code, Name, Description, InputSchema, BindingType, BindingConfig,
                         Strict, IsActive, VersionNumber)
    VALUES (@code, @code, @desc, @schema, N'Sql', @binding, 0, 1, 1);
    PRINT 'Tool creada: cobertura_beneficio_plan';
END
