/* =============================================================================
   REQ-036a - Enganchar el API con su gemela de SQL, en vez de borrar ninguna
   -----------------------------------------------------------------------------
   Nestor, cuando propuse convertir a SQL las dos tools de gateway: "pero no
   debemos borrar los anteriores, una forma de enganchar los apis o los de sql".

   Tenia razon y me corrigio a tiempo. Sustituir obliga a ELEGIR un ambiente:
   desde Azure no se alcanza el gateway -medido: puerto 443 sin abrir, 8 segundos-
   pero desde la red interna el API si responde, y ademas es la fuente de verdad
   porque aplica reglas de negocio que una consulta no reproduce entera. Con dos
   catalogos distintos, uno por ambiente, el dia que abran el cortafuegos habria
   que deshacerlo todo.

   Encadenar no tiene ese problema: el MISMO despliegue funciona dentro y fuera,
   y cuando el gateway vuelva se usa el API solo, sin tocar nada.

   -- Como funciona ----------------------------------------------------------
   La tool declara "fallbackTool" en su BindingConfig. Si falla -y solo si falla-
   el ejecutor prueba esa otra con el mismo input. Reglas:

     · El API manda. El SQL es la red de seguridad, no el camino normal.
     · Se encadena UNA vez. La alternativa de la alternativa no se sigue: dos
       tools apuntandose la una a la otra colgarian la conversacion.
     · El acceso denegado NO se reintenta. Si el guardian anti-IDOR dijo que no,
       la respuesta es no venga por donde venga; buscarle otro camino a un
       identificador ajeno seria justo el agujero que ese guardian tapa.
     · La respuesta lleva _porDondeVino. Presentar el resultado del camino B
       como si fuera el A seria dar por equivalente lo que no lo es.

   -- Este primer enganche ---------------------------------------------------
   consultar_coberturas_plan (Armonix) -> coberturas_y_topes_del_plan (SQL)

   La gemela ya existia, ya funcionaba y ya estaba en el chat: da las coberturas
   y los topes del plan desde Salud.dbo.Pr04Coberturas. Comprobado hoy tras
   arreglarla en REQ-034a: 28 coberturas para el plan IND/N5-C v32.

   Un detalle que habria roto el enganche en silencio: las dos NO llaman igual al
   mismo dato. El gateway manda "versionPlan" y la de SQL espera "version". Al
   encadenar se pasa el MISMO input, asi que la de SQL habria recibido la version
   vacia y habria devuelto TODAS las versiones del plan, con la mas nueva
   primero, que no tiene por que ser la del afiliado. Ahora entiende los dos
   nombres.

   Y una guarda que me fallo a mi: la primera version comprobaba
   NOT LIKE '%versionPlan%' para no aplicarse dos veces... y eso casaba con la
   COLUMNA cob.VersionPlan, porque LIKE es insensible a mayusculas. La migracion
   no hacia nada y el SELECT de comprobacion decia 'SI' por la misma razon. Se
   compara contra '@versionPlan', con arroba, que es el parametro.

   -- Lo que queda -----------------------------------------------------------
   consultar_deducible_contrato todavia no tiene gemela: hay que averiguar de
   que tablas sale el deducible y comprobar que las cifras cuadran con las del
   API antes de publicarla. Un deducible inventado es peor que no contestar.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

/* 1) La de SQL entiende tambien el nombre que usa el gateway. */
DECLARE @v nvarchar(200) = N'@version';
DECLARE @n nvarchar(400) = N'COALESCE(NULLIF(@version,''''), NULLIF(@versionPlan,''''))';

UPDATE dbo.OPAITool
   SET BindingConfig = REPLACE(CONVERT(nvarchar(max), BindingConfig), @v, @n)
 WHERE Code = N'coberturas_y_topes_del_plan'
   AND CONVERT(nvarchar(max), BindingConfig) NOT LIKE N'%@versionPlan%';

/* 2) Y el API queda enganchado con ella. */
UPDATE dbo.OPAITool
   SET BindingConfig = JSON_MODIFY(CONVERT(nvarchar(max), BindingConfig),
                                   '$.fallbackTool', N'coberturas_y_topes_del_plan')
 WHERE Code = N'consultar_coberturas_plan'
   AND ISJSON(CONVERT(nvarchar(max), BindingConfig)) = 1;

SELECT Code,
       Json = ISJSON(CONVERT(nvarchar(max), BindingConfig)),
       Alternativa = JSON_VALUE(CONVERT(nvarchar(max), BindingConfig), '$.fallbackTool'),
       EntiendeVersionPlan = CASE WHEN CONVERT(nvarchar(max), BindingConfig) LIKE N'%@versionPlan%'
                                  THEN 'SI' ELSE 'no' END
  FROM dbo.OPAITool
 WHERE Code IN (N'consultar_coberturas_plan', N'coberturas_y_topes_del_plan')
 ORDER BY Code;
