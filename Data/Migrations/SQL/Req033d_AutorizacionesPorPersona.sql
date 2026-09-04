/* =============================================================================
   REQ-033d - Las autorizaciones entran por indice, no por barrido
   -----------------------------------------------------------------------------
   "puedes darme las autorizaciones que tengo" -> "Lo siento: la parte del
   sistema que consulta sus autorizaciones sigue sin responder". Tres veces.

   -- Lo que NO era, descartado midiendo --------------------------------------
   La consulta desde un portatil en la VPN tarda 0,4-1,3 s. Se descartaron uno
   por uno el coste de la consulta, los indices sueltos (0,13-0,21 s), el tipo
   de los parametros (1,0 s con varchar(400), como manda la app), el plan
   cacheado (1,3 s via sp_executesql, igual que la app), los reintentos (no hay)
   y los bloqueos (la consulta ya lleva NOLOCK en sus dos tablas).

   Tambien di por hecho que la red no llegaba desde Azure. Era falso, y lo
   probo un diagnostico corriendo DENTRO del App Service:

       ping TCP 1433 a 10.10.26.66 -> ABRE en 105 ms
       las cinco conexiones        -> las cinco OK

   -- Lo que SI era ----------------------------------------------------------
   La misma consulta, medida DESDE Azure, tres veces seguidas:

       1a (en frio)   abrir 1.330 ms · primera fila 24.891 ms
       2a             abrir   600 ms · primera fila  3.736 ms
       3a             abrir     5 ms · primera fila  2.916 ms

   En caliente sobra; en frio se come casi los 30 segundos del CommandTimeout.
   Y el chat la llama SIEMPRE en frio: es una tool de uso ocasional.

   La causa de fondo: dbo.Autorizacion tiene 4.555.053 filas y NINGUN indice que
   empiece por ContratoNumero, que es por lo que filtra. Hay uno que empieza por
   CodigoContrato -otra columna: con 4102902 devuelve 0 filas- y otro,
   IdxPersonContratoRegionProducto, que empieza por PersonaNumero.

   -- El arreglo -------------------------------------------------------------
   Se anade @personas, que NO lo manda el modelo: lo rellena el ejecutor con las
   personas del caso, que ya vienen validadas por el guardian anti-IDOR. Asi no
   depende de que el modelo se acuerde y no ensancha lo que el afiliado puede
   ver: son las personas de su propio contrato, las mismas de siempre.

   Van TODAS las del contrato, no una: en uno familiar el titular ve a los
   suyos, como hasta ahora. Un IN con cuatro valores son cuatro seeks; el
   barrido eran 4,5 millones de filas.

   Asi que el arreglo DE VERDAD es el otro: el CommandTimeout del ejecutor SQL
   sube de 30 a 60 segundos, que es lo que cubre el arranque en frio de 25. El
   filtro por persona se queda porque suma un 1,5x y no cuesta nada, pero seria
   deshonesto presentarlo como la solucion.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @ancla nvarchar(400) = N'WHERE (LEN(ISNULL(@numeroAutorizacion,'''')) > 0 OR LEN(ISNULL(@contrato,'''')) > 0)';
DECLARE @nuevo nvarchar(900) = N'WHERE (LEN(ISNULL(@numeroAutorizacion,'''')) > 0 OR LEN(ISNULL(@contrato,'''')) > 0) AND (@personas IS NULL OR LEN(@personas) = 0 OR a.PersonaNumero IN (SELECT TRY_CAST(LTRIM(RTRIM(value)) AS int) FROM STRING_SPLIT(@personas, '','')))';

UPDATE dbo.OPAITool
   SET BindingConfig = REPLACE(CONVERT(nvarchar(max), BindingConfig), @ancla, @nuevo)
 WHERE Code = N'consultar_autorizaciones'
   AND CONVERT(nvarchar(max), BindingConfig) LIKE N'%' + @ancla + N'%'
   AND CONVERT(nvarchar(max), BindingConfig) NOT LIKE N'%@personas%';

SELECT Code,
       Tiene = CASE WHEN CONVERT(nvarchar(max), BindingConfig) LIKE N'%@personas%'
                    THEN 'SI' ELSE 'NO' END,
       Json  = ISJSON(CONVERT(nvarchar(max), BindingConfig))
  FROM dbo.OPAITool
 WHERE Code = N'consultar_autorizaciones';
