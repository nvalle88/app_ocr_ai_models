/* =============================================================================
   REQ-033a - Sucursales cerca de donde esta el afiliado
   -----------------------------------------------------------------------------
   Nestor: "estoy buscando una farmacia en Quito por la Carolina y debemos darle
   las sucursales cerca y de diferentes convenios".

   Lo que ya habia NO servia. buscar_prestador_convenio devuelve CONVENIOS -una
   fila por prestador, con sus sucursales pegadas en un texto- y su parametro
   @cerca es un LIKE plano. Con 'la carolina' habria devuelto las 6 sucursales
   que la nombran, no las farmacias que estan ALREDEDOR, que es lo que pide
   quien pregunta.

   -- Por que no se puede usar el campo Sector, medido -----------------------
   Sector esta lleno al 72,3%, pero en Quito solo tiene tres valores:

       Norte 444 · Sur 95 · Centro 83

   Es un cuadrante, no un barrio. 'Sector LIKE %carolina%' devuelve CERO en las
   5.418 sucursales. Cualquier busqueda por barrio montada sobre ese campo
   falla en silencio. Y IdCatalogoSector esta al 0,0%: no lo escribe nadie.

   -- Lo que si hay: coordenadas ---------------------------------------------
   De 5.418 sucursales, 4.612 (85%) traen latitud y longitud usables. El resto:
   549 en 0,0 y 257 fuera de Ecuador, que se descartan por rango.

   -- La idea: nuestras direcciones son el callejero -------------------------
   No hace falta Google Maps. Se promedian las coordenadas de las sucursales
   cuya DIRECCION o nombre menciona el sitio, y ese centro es el ancla del
   radio. Comprobado con La Carolina de Quito: las que la nombran caen dentro
   de 0,93 km unas de otras y su centro queda a 0,55 km del parque real.

   Resultado medido de 'farmacia / Quito / la carolina', en 481 ms:

       0,26 km  Fybeca Jardin        0,46 km  Cruz Azul Eloy Alfaro
       0,40 km  Fybeca La Pradera    0,56 km  Medicity Republica

   Cuatro cadenas distintas: justo lo de "diferentes convenios".

   -- Lo que NO se promete ---------------------------------------------------
   HorarioAtencion esta al 0,9% -49 de 5.418-. No existe. La tool no lo
   devuelve: decir "abierto hasta las 20h" con ese relleno seria inventarselo.

   -- Y no se queda callada --------------------------------------------------
   Si el sitio no se ubica, en vez de cero filas devuelve las de la ciudad y lo
   DICE en ComoSeBusco. Cero filas en silencio le hace creer al afiliado que no
   hay ninguna farmacia cerca, que es peor que una lista mas amplia etiquetada.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @code nvarchar(100)   = N'buscar_sucursales_cerca';
DECLARE @desc nvarchar(1000)  = N'SUCURSALES CERCA DE UN SITIO: farmacias, hospitales, laboratorios, imagen o centros medicos alrededor de la referencia que da el afiliado -la Carolina, Kennedy, el Bosque-, de TODOS los convenios y ordenadas por distancia real en km. Devuelve direccion, telefono, coordenadas y si el convenio esta vigente. Usala cuando pregunten donde hay una farmacia o donde atenderse por tal zona. Lee ComoSeBusco: dice si ubico la zona o esta dando la ciudad entera. Sin horarios: ese dato no existe.';
DECLARE @schema nvarchar(max) = N'{
  "type": "object",
  "properties": {
    "ciudad": {
      "type": "string",
      "description": "Ciudad del afiliado: Quito, Guayaquil, Cuenca... Acepta uio/gye/pichincha/guayas. Mandala siempre: sin ella se mezclan sucursales de todo el pais y la mas cercana puede estar a 300 km."
    },
    "cerca": {
      "type": "string",
      "description": "La referencia tal como la dijo el afiliado: ''la carolina'', ''kennedy'', ''el bosque'', ''mall del sol''. La zona se ubica promediando las coordenadas de las sucursales que la nombran en su direccion, asi que sirve cualquier sitio conocido. Sin esto salen las de toda la ciudad."
    },
    "tipo": {
      "type": "string",
      "description": "farmacia, hospital, laboratorio, imagen (rayos X, ecografia, resonancia) o centro. Se traduce sola: ''donde compro medicina'' es farmacia. Sin esto salen todos los tipos."
    },
    "radioKm": {
      "type": "string",
      "description": "Radio de busqueda en kilometros. Por omision 3, que en Quito o Guayaquil es el barrio y lo de al lado. Subelo a 6 u 8 solo si la primera busqueda devolvio poco."
    }
  },
  "required": [
    "ciudad"
  ]
}';
DECLARE @binding nvarchar(max) = N'{
  "connection": "SaludReclamos",
  "maxRows": 25,
  "query": "WITH par AS ( SELECT Ciudad = CASE WHEN LOWER(ISNULL(@ciudad,'''')) IN (''uio'',''pichincha'') THEN ''Quito'' WHEN LOWER(ISNULL(@ciudad,'''')) IN (''gye'',''guayas'') THEN ''Guayaquil'' WHEN LOWER(ISNULL(@ciudad,'''')) IN (''azuay'') THEN ''Cuenca'' ELSE ISNULL(@ciudad,'''') END, Tipo = CASE WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%farmac%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%botic%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%medicin%'' THEN ''Farmacia'' WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%hospital%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%clinic%'' THEN ''Hospital'' WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%imagen%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%rayos%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%ecograf%'' THEN ''Laboratorio Imagen'' WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%laborator%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%examen%'' THEN ''Laboratorio'' WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%centro%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%consultor%'' THEN ''Centro'' ELSE ISNULL(@tipo,'''') END, Radio = COALESCE(TRY_CONVERT(float, @radioKm), 3.0) ), suc AS ( SELECT s.NumeroConvenio, s.NumeroSucursal, s.NombreComercial, s.Direccion, s.Sector, s.Telefono1, s.Servicios, Lat = TRY_CONVERT(float, s.Latitud), Lon = TRY_CONVERT(float, s.Longitud), NombreCiudad = cd.NombreCiudad FROM Salud.dbo.Co13SucursalesConvenio s WITH (NOLOCK) LEFT JOIN Salud.dbo.Tg04Ciudades cd WITH (NOLOCK) ON cd.CodigoCiudad = s.CodigoCiudad CROSS JOIN par WHERE s.EsActivo = 1 AND (LEN(par.Ciudad) = 0 OR cd.NombreCiudad LIKE ''%'' + par.Ciudad + ''%'') ), ancla AS ( SELECT Lat = AVG(Lat), Lon = AVG(Lon), Cuantas = COUNT(*) FROM suc WHERE LEN(ISNULL(@cerca,'''')) > 0 AND Lat BETWEEN -5.2 AND 1.6 AND Lon BETWEEN -81.2 AND -74.9 AND Lat <> 0 AND (suc.Direccion LIKE ''%'' + @cerca + ''%'' OR suc.NombreComercial LIKE ''%'' + @cerca + ''%'' OR suc.Sector LIKE ''%'' + @cerca + ''%'') ) SELECT TOP 25 s.NumeroConvenio, Prestador = c.NombrePrestador, Sucursal = NULLIF(LTRIM(RTRIM(s.NombreComercial)), ''''), Tipo = c.TipoPrestador, Direccion = NULLIF(LTRIM(RTRIM(s.Direccion)), ''''), Sector = NULLIF(LTRIM(RTRIM(s.Sector)), ''''), Ciudad = s.NombreCiudad, Telefono = NULLIF(LTRIM(RTRIM(s.Telefono1)), ''''), DistanciaKm = CASE WHEN a.Cuantas > 0 AND s.Lat <> 0 THEN ROUND(2 * 6371.0 * ASIN(SQRT( POWER(SIN(RADIANS(s.Lat - a.Lat) / 2), 2) + COS(RADIANS(a.Lat)) * COS(RADIANS(s.Lat)) * POWER(SIN(RADIANS(s.Lon - a.Lon) / 2), 2))), 2) END, Mapa = CASE WHEN s.Lat BETWEEN -5.2 AND 1.6 AND s.Lat <> 0 THEN CONVERT(varchar(24), s.Lat) + '','' + CONVERT(varchar(24), s.Lon) END, Servicios = NULLIF(LTRIM(RTRIM(CONVERT(varchar(300), s.Servicios))), ''''), Convenio = CASE WHEN c.EstadoConvenio IN (1, 41) THEN ''VIGENTE'' ELSE ''NO vigente'' END, QueSignifica = CASE WHEN c.EstadoConvenio IN (1, 41) THEN ''Trabaja con Salud S.A.: se aplica el porcentaje CON convenio.'' ELSE ''Sin convenio vigente: se aplica el porcentaje SIN convenio, que suele ser menor.'' END, ComoSeBusco = CASE WHEN a.Cuantas > 0 THEN ''por cercania a '' + @cerca + '' ('' + CONVERT(varchar(6), a.Cuantas) + '' referencias, radio '' + CONVERT(varchar(8), par.Radio) + '' km)'' WHEN LEN(ISNULL(@cerca,'''')) > 0 THEN ''NO se pudo ubicar '' + @cerca + '' en el mapa: esta lista es de toda la ciudad, no de esa zona'' ELSE ''toda la ciudad'' END FROM suc s JOIN Salud.dbo.Co03Convenio c WITH (NOLOCK) ON c.NumeroConvenio = s.NumeroConvenio CROSS JOIN par CROSS JOIN ancla a WHERE (LEN(par.Tipo) = 0 OR c.TipoPrestador LIKE ''%'' + par.Tipo + ''%'') /* Si la referencia no se ubica en el mapa NO se devuelve vacio: se ensenan las de la ciudad y la columna ComoSeBusco lo dice. Cero filas en silencio le hace creer al afiliado que no hay ninguna, que es peor que una lista mas amplia bien etiquetada. */ AND (a.Cuantas = 0 OR s.Lat = 0 OR 2 * 6371.0 * ASIN(SQRT(POWER(SIN(RADIANS(s.Lat - a.Lat) / 2), 2) + COS(RADIANS(a.Lat)) * COS(RADIANS(s.Lat)) * POWER(SIN(RADIANS(s.Lon - a.Lon) / 2), 2))) <= par.Radio) ORDER BY CASE WHEN c.EstadoConvenio IN (1, 41) THEN 0 ELSE 1 END, CASE WHEN a.Cuantas > 0 AND s.Lat <> 0 THEN 2 * 6371.0 * ASIN(SQRT(POWER(SIN(RADIANS(s.Lat - a.Lat) / 2), 2) + COS(RADIANS(a.Lat)) * COS(RADIANS(s.Lat)) * POWER(SIN(RADIANS(s.Lon - a.Lon) / 2), 2))) ELSE 999 END, c.NombrePrestador"
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
 WHERE g.Code IN (N'AGENTE_CHAT_CLIENTE', N'AGENTE_PORTAL_CLIENTE', N'AGENTE_CLAUDE')
   AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt
                    WHERE mt.ModelCode = g.Code AND mt.ToolCode = @code);

SELECT mt.ModelCode, mt.ToolCode FROM dbo.OPAIModelTool mt WHERE mt.ToolCode = @code ORDER BY mt.ModelCode;
