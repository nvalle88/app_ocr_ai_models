/* =============================================================================
   REQ-045a - La liquidacion sale del SERVICIO con los TRES parametros
   -----------------------------------------------------------------------------
   Nestor: "no todos tienen liquidaciones, mira el api-liquidaciones en listar
   sobres y detalle sobre".

   Confirmado leyendo LogicaReembolsoElectronico.ObtenerDetalleSobre (linea 1376):
   ResumenLiquidacion y Liquidaciones SOLO se llenan si se pasan codigoContrato Y
   numeroPersonaBeneficiario -ahi lee el reclamo por numero de sobre-. Sin ellos
   vuelve en ceros. Probado contra produccion, sobre NA-2735345:

       solo idSobre                          -> Cubierto 0, Pagado 0   (parece sin liquidar)
       idSobre + contrato 1350017 + persona  -> Cubierto 3.109,99, Pagado 3.109,99,
       5446025                                  NoCubierto 34,76, dx "Calculo del rinon"

   Por eso el chat no devolvia valores: los dos parametros eran OPCIONALES y el
   modelo los omitia. Aqui pasan a OBLIGATORIOS y el prompt explica la trampa y
   que no todos los sobres tienen liquidacion (ceros = aun no liquidado, no error).
   El servicio manda; la gemela SQL (Req044a) queda de respaldo.

   Aplicar los cambios de esquema/prompt desde el generador
   scratchpad/_gen_liq3.py (este .sql es la bitacora del cambio).
   ============================================================================= */
IF DB_NAME() NOT IN ('db-nexus-test','db-nexus-aud')
BEGIN RAISERROR('Solo db-nexus-*',16,1); RETURN; END
SET NOCOUNT ON;
UPDATE dbo.OPAITool
   SET InputSchema = JSON_MODIFY(CONVERT(nvarchar(max),InputSchema),'$.required',
        JSON_QUERY('["idSobre","codigoContrato","numeroPersonaBeneficiario"]'))
 WHERE Code='consultar_liquidacion_sobre' AND ISJSON(CONVERT(nvarchar(max),InputSchema))=1;
SELECT Code, Required=JSON_QUERY(CONVERT(nvarchar(max),InputSchema),'$.required')
  FROM dbo.OPAITool WHERE Code='consultar_liquidacion_sobre';
