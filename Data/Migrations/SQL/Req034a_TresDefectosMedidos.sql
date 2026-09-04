/* =============================================================================
   REQ-034a - Tres defectos que la auditoria destapo, verificados a mano
   -----------------------------------------------------------------------------
   Nestor: "ya estan las tools completas???". Se ejercitaron las 15 del chat
   contra web-nexus-test, una a una, con parametros reales. Salieron cinco
   acusaciones; DOS eran falsas -datos de produccion probados contra una base de
   pruebas- y estas tres las confirme yo mismo llamando al endpoint.

   -- 1. coberturas_y_topes_del_plan devolvia CERO coberturas ----------------
   Medido, mismo plan, misma llamada:

       con p_region=Costa .... OK, 0 filas
       sin region ............ OK, 28 filas

   La causa NO es el texto ni las mayusculas -la columna es CI_AI-. Es que el
   plan IND / N5-C / v32 tiene sus 28 coberturas guardadas con Region='Sierra'
   aunque el contrato sea "Elite 5 Costa". Region en Pr04Coberturas no es la
   region del contrato, y filtrar por ella las borraba todas.

   Al afiliado: preguntaba "que cubre mi plan?" y el chat le contestaba, con
   toda seguridad y sin error que le hiciera dudar, que no tiene ninguna
   cobertura. Con 28 vigentes, maternidad incluida.

   Arreglo: la region pasa a ser filtro BLANDO. Filtra si el plan tiene
   coberturas de esa region; si no tiene ninguna, no filtra. Medido: de 209.582
   plan+version, 209.502 tienen UNA sola region -ahi el filtro sobraba- y solo
   80 (0,04%) tienen varias, que son las unicas donde de verdad hace falta. Y se
   devuelve RegionDeEstaCobertura para que no se calle de donde salio el tope.

   -- 2. buscar_prestador_convenio: la ciudad era la del REGISTRO ------------
   Medido:

       p_nombre=farcomed & p_ciudad=Guayaquil .... 0 filas
       p_nombre=farcomed & p_cerca=guayaquil ..... 1 fila (el mismo prestador)

   cd.NombreCiudad es la ciudad donde esta REGISTRADO el convenio, no donde
   tiene locales. Fybeca esta registrada en Quito, asi que al afiliado de
   Guayaquil que pregunta "hay una Fybeca por aqui?" se le decia que no hay
   ninguna con convenio. Se atiende creyendo que no hay convenio y paga el
   porcentaje SIN convenio, mas caro, teniendo derecho al otro.

   El propio SQL ya documentaba este fallo y lo cuantificaba -"de 32 llamadas,
   14 volvieron vacias"-, pero el arreglo se aplico SOLO a la rama de
   numeroConvenio. La de nombre/tipo/especialidad seguia rota. Ahora la ciudad
   tambien acepta que el prestador tenga una SUCURSAL ACTIVA en esa ciudad.

   -- 3. buscar_medicina no encontraba nada si se escribia con tildes --------
   Medido:

       "acido folico" .... 12 filas
       "acido folico" con tildes .... 0 filas

   La base es CI_AS: no distingue mayusculas pero SI tildes. Al afiliado que
   escribe en castellano correcto se le decia que su medicina no existe. Los
   LIKE pasan a COLLATE ..._CI_AI.

   -- Lo que NO se toca ------------------------------------------------------
   consultar_sobre_bd y factura_ya_pagada_bd salieron "ROTAS" en la auditoria y
   NO lo estan: se probaron con un sobre (NA-2942604) y una factura de
   PRODUCCION contra la base de PRUEBAS, donde el ultimo sobre es NA-2612807.
   Con un sobre que si existe alli: 1 fila en 121 ms.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @v1 nvarchar(max) = N'(@region IS NULL OR LEN(@region) = 0 OR cob.Region = @region)';
DECLARE @n1 nvarchar(max) = N'(@region IS NULL OR LEN(@region) = 0 OR cob.Region = @region OR NOT EXISTS (SELECT 1 FROM Salud.dbo.Pr04Coberturas r2 WITH (NOLOCK) WHERE r2.CodigoProducto = cob.CodigoProducto AND r2.CodigoPlan = cob.CodigoPlan AND r2.VersionPlan = cob.VersionPlan AND r2.Region = @region))';
UPDATE dbo.OPAITool
   SET BindingConfig = REPLACE(CONVERT(nvarchar(max), BindingConfig), @v1, @n1)
 WHERE Code = N'coberturas_y_topes_del_plan'
   AND CONVERT(nvarchar(max), BindingConfig) LIKE N'%' + @v1 + N'%';

DECLARE @v2 nvarchar(max) = N'SELECT TOP 40 cob.CodigoCobertura,';
DECLARE @n2 nvarchar(max) = N'SELECT TOP 40 cob.CodigoCobertura, RegionDeEstaCobertura = cob.Region,';
UPDATE dbo.OPAITool
   SET BindingConfig = REPLACE(CONVERT(nvarchar(max), BindingConfig), @v2, @n2)
 WHERE Code = N'coberturas_y_topes_del_plan'
   AND CONVERT(nvarchar(max), BindingConfig) LIKE N'%' + @v2 + N'%';

DECLARE @v3 nvarchar(max) = N'AND (@ciudad IS NULL OR LEN(@ciudad) = 0 OR LEN(ISNULL(@numeroConvenio,'''')) > 0 OR cd.NombreCiudad LIKE ''%'' + tt.Ciudad + ''%'')';
DECLARE @n3 nvarchar(max) = N'AND (@ciudad IS NULL OR LEN(@ciudad) = 0 OR LEN(ISNULL(@numeroConvenio,'''')) > 0 OR cd.NombreCiudad LIKE ''%'' + tt.Ciudad + ''%'' OR EXISTS (SELECT 1 FROM Salud.dbo.Co13SucursalesConvenio s7 WITH (NOLOCK) JOIN Salud.dbo.Tg04Ciudades cd7 WITH (NOLOCK) ON cd7.CodigoCiudad = s7.CodigoCiudad WHERE s7.NumeroConvenio = c.NumeroConvenio AND s7.EsActivo = 1 AND cd7.NombreCiudad LIKE ''%'' + tt.Ciudad + ''%''))';
UPDATE dbo.OPAITool
   SET BindingConfig = REPLACE(CONVERT(nvarchar(max), BindingConfig), @v3, @n3)
 WHERE Code = N'buscar_prestador_convenio'
   AND CONVERT(nvarchar(max), BindingConfig) LIKE N'%' + @v3 + N'%';

DECLARE @v4 nvarchar(max) = N'm.Descripcion LIKE ''%'' + @';
DECLARE @n4 nvarchar(max) = N'm.Descripcion COLLATE SQL_Latin1_General_CP1_CI_AI LIKE ''%'' + @';
UPDATE dbo.OPAITool
   SET BindingConfig = REPLACE(CONVERT(nvarchar(max), BindingConfig), @v4, @n4)
 WHERE Code = N'buscar_medicina'
   AND CONVERT(nvarchar(max), BindingConfig) LIKE N'%' + @v4 + N'%';

DECLARE @v5 nvarchar(max) = N'm.PrincipioActivo LIKE ''%'' + @';
DECLARE @n5 nvarchar(max) = N'm.PrincipioActivo COLLATE SQL_Latin1_General_CP1_CI_AI LIKE ''%'' + @';
UPDATE dbo.OPAITool
   SET BindingConfig = REPLACE(CONVERT(nvarchar(max), BindingConfig), @v5, @n5)
 WHERE Code = N'buscar_medicina'
   AND CONVERT(nvarchar(max), BindingConfig) LIKE N'%' + @v5 + N'%';

SELECT Code, Json = ISJSON(CONVERT(nvarchar(max), BindingConfig)),
       Largo = LEN(CONVERT(nvarchar(max), BindingConfig))
  FROM dbo.OPAITool
 WHERE Code IN (N'coberturas_y_topes_del_plan', N'buscar_prestador_convenio', N'buscar_medicina')
 ORDER BY Code;
