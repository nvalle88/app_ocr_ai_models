/* =============================================================================
   REQ-042a - La distancia dejaba de salir, y a veces mentia
   -----------------------------------------------------------------------------
   Nestor: "hay ocasiones que muestra la distancia de donde estoy y otras no".

   Tenia razon, y no era un defecto sino CUATRO, todos medidos hoy contra
   produccion. Los tres primeros dejan a TODAS las filas sin distancia -si no
   hay ancla no hay desde donde medir-, que es exactamente lo que se veia.

   1. LA REFERENCIA SE BUSCABA COMO FRASE ENTERA
      "la mariscal" -> CERO coincidencias. Y sin embargo 63 sucursales la
      nombran: en la direccion pone "Avenida Mariscal Sucre". Sobraba el
      articulo. Ahora se parte en palabras y se piden todas.

   2. EL RADIO SE VOLVIA CERO
      TRY_CONVERT(float, cadena vacia) no devuelve NULL: devuelve 0. Con
      radioKm vacio -que es lo normal- el COALESCE se quedaba con ese 0 y el
      radio era de cero kilometros. Solo pasaba lo que cayera justo en el
      centro, o sea casi nada.

   3. EL ANCLA SE CALCULABA CON PROMEDIO
      32 de las 1.114 sucursales de Quito llevan una coordenada que no esta en
      Quito -la mas lejana, a 380 km-. El promedio se va detras de ellas y el
      centro acaba en otra provincia; entonces un radio de 3 km no alcanza
      nada. Ahora es la MEDIANA, y ademas se descarta como ancla lo que este a
      mas de 30 km del centro de la ciudad.

   4. Y CUANDO SI MEDIA, A VECES MENTIA
      "norte" casaba con 430 sucursales repartidas por 452 km. Salia una
      distancia con dos decimales que no significaba nada. Ahora se mide cuanto
      se separan entre si las referencias, y ese numero distingue solo:

          un sitio de verdad   la carolina 0,56 · la pradera 0,06
                               urdesa 0,76 · el inca 0,73 · kennedy 1,90
          una avenida o un
          nombre repetido      norte 4,41 · la mariscal 5,31
                               quicentro 6,08 -hay dos, Norte y Sur-

      Por debajo de 2,5 km se contesta con el radio pedido. Entre 2,5 y 8 se
      contesta igual, pero el radio se ensancha y la respuesta DICE que es una
      zona ancha y que la distancia es aproximada. Por encima de 8 no se
      inventa un centro: se da la ciudad y se dice que no se ubico la zona.

   -- Y una casilla en blanco no explica nada -------------------------------
   70 de las 1.114 sucursales de Quito no tienen coordenada. Antes su distancia
   salia vacia, igual que cuando fallaba el ancla, y las dos cosas se leian
   como la misma. Ahora cada fila dice cual de las dos es.

   -- De regalo, un filtro de ciudad que se colaba ---------------------------
   El filtro era LIKE con comodines, que ademas de Quito se lleva Puerto Quito
   y Quito-NY. Ahora es igualdad.

   -- Comprobado despues del cambio ------------------------------------------
       farmacia / Quito / la carolina -> Fybeca Jardin 0,33 km -su direccion
       dice "frente al parque La Carolina"-, Cruz Azul Shyris 0,38, Fybeca La
       Pradera 0,65, Pharmacys America 0,70. Cuatro cadenas distintas. 1,15 s.
       "la mariscal" -> 32 referencias, marcado como zona de 5,3 km de ancho.

   -- Lo que sigue mal y no arregla esto -------------------------------------
   Hay sucursales que comparten una coordenada copiada de otra: "Medicity Quito
   La Y" -que esta en la Av. de la Prensa- lleva la misma coordenada que
   "Medicity Republica El Salvador". Eso es dato mal cargado, no calculo, y se
   arregla en el maestro de sucursales.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @binding nvarchar(max) = N'{"connection": "SaludReclamos", "maxRows": 25, "query": "WITH par AS ( SELECT Ciudad = CASE WHEN LOWER(ISNULL(@ciudad,'''')) IN (''uio'',''pichincha'') THEN ''Quito'' WHEN LOWER(ISNULL(@ciudad,'''')) IN (''gye'',''guayas'') THEN ''Guayaquil'' WHEN LOWER(ISNULL(@ciudad,'''')) IN (''azuay'') THEN ''Cuenca'' ELSE ISNULL(@ciudad,'''') END, Tipo = CASE WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%farmac%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%botic%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%medicin%'' THEN ''Farmacia'' WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%hospital%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%clinic%'' THEN ''Hospital'' WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%imagen%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%rayos%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%ecograf%'' THEN ''Laboratorio Imagen'' WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%laborator%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%examen%'' THEN ''Laboratorio'' WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%centro%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%consultor%'' THEN ''Centro'' ELSE ISNULL(@tipo,'''') END, Radio = COALESCE(NULLIF(TRY_CONVERT(float, NULLIF(LTRIM(RTRIM(@radioKm)),'''')), 0), 3.0) ), suc AS ( SELECT s.NumeroConvenio, s.NombreComercial, s.Direccion, s.Sector, s.Telefono1, s.Servicios, Lat = TRY_CONVERT(float,s.Latitud), Lon = TRY_CONVERT(float,s.Longitud), NombreCiudad = cd.NombreCiudad, Texto = LOWER(ISNULL(s.Direccion,'''')+'' ''+ISNULL(s.NombreComercial,'''')+'' ''+ISNULL(s.Sector,'''')) FROM Salud.dbo.Co13SucursalesConvenio s WITH (NOLOCK) JOIN Salud.dbo.Tg04Ciudades cd WITH (NOLOCK) ON cd.CodigoCiudad = s.CodigoCiudad CROSS JOIN par WHERE s.EsActivo = 1 AND (LEN(par.Ciudad) = 0 OR cd.NombreCiudad = par.Ciudad) ), geo AS (SELECT * FROM suc WHERE Lat BETWEEN -5.2 AND 1.6 AND Lon BETWEEN -81.2 AND -74.9 AND Lat <> 0), centro AS ( SELECT TOP 1 Lat = PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY Lat) OVER (), Lon = PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY Lon) OVER () FROM geo ), palabras AS ( SELECT p = LOWER(LTRIM(RTRIM(value))) FROM STRING_SPLIT(REPLACE(REPLACE(REPLACE(ISNULL(@cerca,''''),''.'','' ''),'','','' ''),''-'','' ''), '' '') WHERE LEN(LTRIM(RTRIM(value))) >= 3 AND LOWER(LTRIM(RTRIM(value))) NOT IN (''los'',''las'',''del'',''por'',''con'',''para'',''cerca'',''junto'',''avenida'',''calle'',''sector'', ''barrio'',''zona'',''que'',''una'',''este'',''esta'',''mas'',''ave'',''sur'',''frente'',''lado'') ), cand AS ( SELECT g.Lat, g.Lon FROM geo g CROSS JOIN centro c WHERE EXISTS (SELECT 1 FROM palabras) AND NOT EXISTS (SELECT 1 FROM palabras w WHERE g.Texto NOT LIKE ''%''+w.p+''%'') AND 2*6371.0*ASIN(SQRT(POWER(SIN(RADIANS(g.Lat-c.Lat)/2),2) + COS(RADIANS(c.Lat))*COS(RADIANS(g.Lat))*POWER(SIN(RADIANS(g.Lon-c.Lon)/2),2))) <= 30 ), ancla0 AS ( SELECT TOP 1 Lat = PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY Lat) OVER (), Lon = PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY Lon) OVER (), Cuantas = COUNT(*) OVER () FROM cand ), ancla1 AS ( SELECT a.Lat, a.Lon, a.Cuantas, Dispersion = ROUND(AVG(2*6371.0*ASIN(SQRT(POWER(SIN(RADIANS(k.Lat-a.Lat)/2),2) + COS(RADIANS(a.Lat))*COS(RADIANS(k.Lat))*POWER(SIN(RADIANS(k.Lon-a.Lon)/2),2)))),2) FROM ancla0 a CROSS JOIN cand k GROUP BY a.Lat, a.Lon, a.Cuantas ), ancla AS ( SELECT Lat, Lon, Dispersion, Cuantas = CASE WHEN Dispersion > 8 THEN 0 ELSE Cuantas END, Radio = CASE WHEN Dispersion > 2.5 THEN (SELECT Radio FROM par) + Dispersion ELSE (SELECT Radio FROM par) END FROM ancla1 UNION ALL SELECT NULL, NULL, NULL, 0, (SELECT Radio FROM par) WHERE NOT EXISTS (SELECT 1 FROM ancla1) ), res AS ( SELECT s.NumeroConvenio, Prestador = c.NombrePrestador, Sucursal = NULLIF(LTRIM(RTRIM(s.NombreComercial)),''''), Tipo = c.TipoPrestador, Direccion = NULLIF(LTRIM(RTRIM(s.Direccion)),''''), Ciudad = s.NombreCiudad, Telefono = NULLIF(LTRIM(RTRIM(s.Telefono1)),''''), Km = CASE WHEN a.Cuantas > 0 AND s.Lat IS NOT NULL AND s.Lat <> 0 THEN CONVERT(decimal(7,2), 2*6371.0*ASIN(SQRT(POWER(SIN(RADIANS(s.Lat-a.Lat)/2),2) + COS(RADIANS(a.Lat))*COS(RADIANS(s.Lat))*POWER(SIN(RADIANS(s.Lon-a.Lon)/2),2)))) END, TieneCoord = CASE WHEN s.Lat IS NOT NULL AND s.Lat <> 0 THEN 1 ELSE 0 END, Mapa = CASE WHEN s.Lat BETWEEN -5.2 AND 1.6 AND s.Lat <> 0 THEN CONVERT(varchar(24),s.Lat)+'',''+CONVERT(varchar(24),s.Lon) END, Servicios = NULLIF(LTRIM(RTRIM(CONVERT(varchar(300), s.Servicios))),''''), Convenio = CASE WHEN c.EstadoConvenio IN (1,41) THEN ''VIGENTE'' ELSE ''NO vigente'' END, QueSignifica = CASE WHEN c.EstadoConvenio IN (1,41) THEN ''Trabaja con Salud S.A.: se aplica el porcentaje CON convenio.'' ELSE ''Sin convenio vigente: se aplica el porcentaje SIN convenio, que suele ser menor.'' END, a.Cuantas, a.Dispersion, a.Radio FROM suc s JOIN Salud.dbo.Co03Convenio c WITH (NOLOCK) ON c.NumeroConvenio = s.NumeroConvenio CROSS JOIN par CROSS JOIN ancla a WHERE (LEN(par.Tipo) = 0 OR c.TipoPrestador LIKE ''%''+par.Tipo+''%'') ) SELECT TOP 25 NumeroConvenio, Prestador, Sucursal, Tipo, Direccion, Ciudad, Telefono, DistanciaKm = Km, Ubicacion = CASE WHEN TieneCoord = 0 THEN ''sin ubicacion cargada: de esta sucursal no se puede medir la distancia'' WHEN Km IS NULL THEN ''no se midio: no se ubico la zona en el mapa'' WHEN Km <= 1 THEN ''a menos de 1 km, caminando'' WHEN Km <= 3 THEN ''en el mismo sector'' ELSE ''algo mas lejos'' END, Mapa, Servicios, Convenio, QueSignifica, ComoSeBusco = CASE WHEN Cuantas = 0 AND LEN(ISNULL(@cerca,'''')) > 0 THEN ''NO se ubico esa referencia en el mapa: esta lista es de toda la ciudad y por eso va sin distancias'' WHEN Cuantas = 0 THEN ''toda la ciudad: no se dio una referencia con que medir'' WHEN Dispersion > 2.5 THEN ''esa referencia no es un punto sino una zona de ''+CONVERT(varchar(8),CONVERT(decimal(5,1),Dispersion)) +'' km de ancho: las distancias son al centro de la zona, dilas como aproximadas'' ELSE ''por cercania real, con ''+CONVERT(varchar(6),Cuantas)+'' referencias y radio '' +CONVERT(varchar(8),CONVERT(decimal(5,1),Radio))+'' km'' END FROM res WHERE Cuantas = 0 OR Km IS NULL OR Km <= Radio ORDER BY CASE WHEN Convenio = ''VIGENTE'' THEN 0 ELSE 1 END, CASE WHEN Km IS NULL THEN 999 ELSE Km END, Prestador"}';

UPDATE dbo.OPAITool
   SET BindingConfig = @binding
 WHERE Code = N'buscar_sucursales_cerca';

/* La ficha de los parametros tambien: es lo que el modelo lee al decidir que manda. */
UPDATE dbo.OPAITool
   SET InputSchema = JSON_MODIFY(CONVERT(nvarchar(max), InputSchema),
        '$.properties.cerca.description',
        N'La referencia tal como la dijo el afiliado: la carolina, kennedy, el bosque, mall del sol. Se parte en palabras, asi que el articulo no estorba y no hace falta escribirla igual que en la direccion. Lee ComoSeBusco: dice si la zona quedo bien ubicada o si es ancha y la distancia es aproximada.')
 WHERE Code = N'buscar_sucursales_cerca'
   AND ISJSON(CONVERT(nvarchar(max), InputSchema)) = 1;

UPDATE dbo.OPAITool
   SET InputSchema = JSON_MODIFY(CONVERT(nvarchar(max), InputSchema),
        '$.properties.radioKm.description',
        N'Radio en kilometros. Dejalo VACIO y sale 3, que en Quito o Guayaquil es el barrio y lo de al lado. Subelo a 6 u 8 solo si la primera busqueda devolvio poco.')
 WHERE Code = N'buscar_sucursales_cerca'
   AND ISJSON(CONVERT(nvarchar(max), InputSchema)) = 1;

SELECT Code,
       Json = ISJSON(CONVERT(nvarchar(max), BindingConfig)),
       PorPalabras = CASE WHEN CONVERT(nvarchar(max), BindingConfig) LIKE N'%STRING_SPLIT(REPLACE%' THEN 'SI' ELSE 'NO' END,
       Mediana     = CASE WHEN CONVERT(nvarchar(max), BindingConfig) LIKE N'%PERCENTILE_CONT%' THEN 'SI' ELSE 'NO' END,
       RadioCero   = CASE WHEN CONVERT(nvarchar(max), BindingConfig) LIKE N'%NULLIF(TRY_CONVERT(float%' THEN 'arreglado' ELSE 'NO' END
  FROM dbo.OPAITool WHERE Code = N'buscar_sucursales_cerca';
