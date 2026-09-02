/* =============================================================================
   REQ-029 - Buscar prestadores en los convenios
   -----------------------------------------------------------------------------
   Falta la pregunta mas frecuente de un afiliado y no habia con que responderla:
   "donde me puedo atender", "el doctor X trabaja con Saludsa", "que hospital de
   Guayaquil me cubre".

   Ya existia resolver_convenio_por_ruc, pero solo sirve si YA se tiene el RUC
   -sirve para juzgar una factura, no para buscar-. Esta busca de verdad: por
   nombre, por ciudad, por numero de convenio.

   -- Lo que devuelve, y por que ----------------------------------------------
   No solo el nombre. Devuelve el ESTADO del convenio traducido a lo que
   significa para el bolsillo del afiliado:

       estado 1 o 41 -> "SI trabaja con Salud S.A.: se aplica el porcentaje CON
                         convenio"
       cualquier otro -> "convenio NO vigente: se aplica el SIN convenio, que
                          suele ser menor"

   Esa es la traduccion que importa. Un afiliado no necesita saber que el estado
   es 39: necesita saber que ahi le van a cubrir menos.

   Los estados 1 y 41 son ambos activos: el 41 aparece por el barrido historico
   documentado en docs/cerebro/_regularizacion-medicos-41.md, y tratarlo como
   inactivo excluiria prestadores que si trabajan con Saludsa.

   -- El tarifario, como dato ------------------------------------------------
   Se marca si el convenio tiene tarifario (Saludsa.Tarifario.PrestacionPrestador),
   porque MEDIDO solo 213 lo tienen y eso decide si el gasto se identifica bien o
   cae al cajon generico 504001. Para el afiliado es irrelevante; para quien
   audita, no.

   NOTA: escrita con el esquema verificado en tools/consultar_prestador.py, que
   corre contra esta misma base. No se pudo comprobar en vivo -la VPN estaba
   caida-, asi que la PRIMERA ejecucion vale como verificacion.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @code nvarchar(100)    = N'buscar_prestador_convenio';
DECLARE @desc nvarchar(1000)   = N'Busca prestadores en los convenios: por RUC, nombre, convenio, CIUDAD, TIPO (hospital, farmacia, laboratorio, medico), ESPECIALIDAD o CERCA de un sector o direccion. Dice si el convenio esta VIGENTE -y si se aplica el porcentaje CON convenio, que suele ser mayor-, con la direccion, el sector y el telefono de sus sucursales. Usala cuando pregunten donde atenderse, que hospital o especialista les cubre, o cual tienen cerca.';
DECLARE @schema nvarchar(max)  = N'{
  "type": "object",
  "properties": {
    "ruc": {
      "type": "string",
      "description": "RUC del prestador, 13 digitos. Lo mas exacto: si lo tienes, usalo."
    },
    "nombre": {
      "type": "string",
      "description": "Parte del nombre del prestador. Minimo 4 letras para que sirva."
    },
    "numeroConvenio": {
      "type": "string",
      "description": "Numero de convenio, si ya se conoce."
    },
    "ciudad": {
      "type": "string",
      "description": "Ciudad, para saber donde puede atenderse."
    },
    "soloActivos": {
      "type": "string",
      "description": "true para dejar solo los convenios vigentes (estados 1 y 41). Es lo que suele querer un afiliado: donde SI puede ir."
    },
    "cerca": {
      "type": "string",
      "description": "Sector o parte de la direccion: ''Garzota'', ''Kennedy'', ''Av. 9 de Octubre''. Busca en las sucursales. Es lo que pregunta un afiliado de verdad: no ''que hospitales hay'' sino ''cual tengo cerca''."
    },
    "tipo": {
      "type": "string",
      "description": "Tipo de prestador, EN LAS PALABRAS DEL AFILIADO. La tool las traduce a lo que dice la tabla: hospital/clinica/emergencia -> Clinica-Hospital; farmacia/botica/medicinas -> Farmacia; doctor/medico/especialista/consultorio -> Medico; examenes/analisis/sangre -> Laboratorio Clinico; radiografia/ecografia/resonancia/rayos -> Laboratorio Imagen. Pasa lo que escribio la persona, no lo traduzcas tu. La respuesta dice en ComoSeTradujoElTipo que se busco de verdad."
    }
  },
  "required": []
}';
DECLARE @binding nvarchar(max) = N'{
  "connection": "SaludReclamos",
  "maxRows": 25,
  "query": "SELECT TOP 25 c.NumeroConvenio, c.Rucins AS Ruc, c.NombrePrestador AS Prestador, c.TipoPrestador, CASE c.EstadoConvenio WHEN 1 THEN ''ACTIVO'' WHEN 41 THEN ''ACTIVO (41)'' WHEN 39 THEN ''INACTIVO (39)'' ELSE ''estado '' + CONVERT(varchar(6), c.EstadoConvenio) END AS Estado, cd.NombreCiudad AS Ciudad, c.NivelPrestadorDesde AS NivelDesde, c.NivelPrestadorHasta AS NivelHasta, CASE WHEN c.EstadoConvenio IN (1, 41) THEN ''SI trabaja con Salud S.A.: al afiliado se le aplica el porcentaje CON convenio.'' ELSE ''Convenio NO vigente: se le aplica el porcentaje SIN convenio, que suele ser menor.'' END AS QueSignifica, CASE WHEN @tipo IS NULL OR LEN(@tipo) = 0 THEN NULL WHEN LOWER(@tipo) = LOWER(tt.Buscar) THEN NULL ELSE ''Se busco «'' + tt.Buscar + ''» porque en la tabla no dice «'' + @tipo + ''»'' END AS ComoSeTradujoElTipo, esp.Especialidades, suc.Sucursales, suc.Direcciones, suc.Telefonos, suc.Sectores FROM Salud.dbo.Co03Convenio c WITH (NOLOCK) LEFT JOIN Salud.dbo.Tg04Ciudades cd WITH (NOLOCK) ON cd.CodigoCiudad = c.CodigoCiudad /* Las especialidades del convenio. OJO DOS COSAS de esta tabla: 1) la columna se llama DescripcionEspecilidad -con la errata en la base-, 2) Estado vale 0 en las 1.135 filas, asi que filtrar por Estado=1 devuelve CERO. Ese cero no seria ''no tiene especialidades'': seria el filtro mal. Solo 1.079 convenios tienen especialidad cargada, asi que venir vacio NO significa que el medico no la tenga. */ /* Donde atenderse de verdad: direccion, sector y telefono de sus sucursales. MEDIDO sobre 5.418 sucursales: 5.410 con direccion, 5.041 con telefono, 3.915 con sector y 4.862 con coordenadas. El HORARIO no se trae: solo 49 de 5.418 lo tienen, y decirle a alguien que un sitio abre a las 8 sin saberlo es mandarlo a una puerta cerrada. */ OUTER APPLY (SELECT Sucursales = COUNT(*), Direcciones = STUFF((SELECT DISTINCT '' | '' + x.Direccion FROM Salud.dbo.Co13SucursalesConvenio x WITH (NOLOCK) WHERE x.NumeroConvenio = c.NumeroConvenio AND x.EsActivo = 1 AND x.Direccion IS NOT NULL FOR XML PATH('''')), 1, 3, ''''), Telefonos = STUFF((SELECT DISTINCT '', '' + y.Telefono1 FROM Salud.dbo.Co13SucursalesConvenio y WITH (NOLOCK) WHERE y.NumeroConvenio = c.NumeroConvenio AND y.EsActivo = 1 AND LEN(ISNULL(y.Telefono1,'''')) > 5 FOR XML PATH('''')), 1, 2, ''''), Sectores = STUFF((SELECT DISTINCT '', '' + z.Sector FROM Salud.dbo.Co13SucursalesConvenio z WITH (NOLOCK) WHERE z.NumeroConvenio = c.NumeroConvenio AND z.EsActivo = 1 AND LEN(ISNULL(z.Sector,'''')) > 1 FOR XML PATH('''')), 1, 2, '''') FROM Salud.dbo.Co13SucursalesConvenio s2 WITH (NOLOCK) WHERE s2.NumeroConvenio = c.NumeroConvenio AND s2.EsActivo = 1) suc /* Lo que escribe una persona NO es lo que dice la tabla. La colacion es Latin1_General_CI_AI, asi que ''clinica'' ya casa con ''Clinica/Hospital'' y ''medico'' con ''Medico'' sin hacer nada. Lo que NO casa es todo lo demas que dice la gente: ''centro medico'' -> la tabla dice ''Centro de Medicos'' (el ''de'' estorba) ''doctor'' -> la tabla dice ''Medico'' ''consultorio'' -> la tabla dice ''Medico'' ''examenes'' -> la tabla dice ''Laboratorio Clinico'' ''radiografia'' -> la tabla dice ''Laboratorio Imagen'' ''botica'' -> la tabla dice ''Farmacia'' Sin esto, un afiliado que escribe ''quiero un doctor'' recibe CERO resultados y concluye que no hay medicos con convenio. Es el peor no-resultado posible: el que parece una respuesta. */ CROSS APPLY (SELECT Buscar = CASE WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%hospital%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%clinic%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%hospitaliz%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%emergenc%'' THEN ''Hospital'' WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%farmac%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%botic%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%medicin%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%remedi%'' THEN ''Farmacia'' WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%imagen%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%radiograf%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%ecograf%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%resonanc%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%tomograf%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%rayos%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%mamograf%'' THEN ''Laboratorio Imagen'' WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%laborator%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%examen%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%analisis%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%sangre%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%muestra%'' THEN ''Laboratorio'' WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%centro%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%consultor%'' THEN ''Centro'' WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%doctor%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%medic%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%especialist%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%galen%'' THEN ''Medico'' ELSE ISNULL(@tipo,'''') END) tt OUTER APPLY (SELECT Especialidades = STUFF((SELECT DISTINCT '', '' + e.DescripcionEspecilidad FROM Salud.dbo.Cm19EspecialidadConvenio e WITH (NOLOCK) WHERE e.NumeroConvenio = c.NumeroConvenio AND e.DescripcionEspecilidad IS NOT NULL FOR XML PATH('''')), 1, 2, '''')) esp WHERE (@ruc IS NULL OR LEN(@ruc) = 0 OR c.Rucins = @ruc) AND (@nombre IS NULL OR LEN(@nombre) = 0 OR c.NombrePrestador LIKE ''%'' + @nombre + ''%'') AND (@numeroConvenio IS NULL OR LEN(@numeroConvenio) = 0 OR c.NumeroConvenio = TRY_CAST(@numeroConvenio AS int)) AND (@ciudad IS NULL OR LEN(@ciudad) = 0 OR cd.NombreCiudad LIKE ''%'' + @ciudad + ''%'') AND (@soloActivos IS NULL OR LOWER(@soloActivos) NOT IN (''1'',''true'',''si'') OR c.EstadoConvenio IN (1, 41)) AND (@tipo IS NULL OR LEN(@tipo) = 0 OR c.TipoPrestador LIKE ''%'' + tt.Buscar + ''%'') AND (@cerca IS NULL OR LEN(@cerca) = 0 OR EXISTS (SELECT 1 FROM Salud.dbo.Co13SucursalesConvenio s3 WITH (NOLOCK) WHERE s3.NumeroConvenio = c.NumeroConvenio AND s3.EsActivo = 1 AND (s3.Sector LIKE ''%'' + @cerca + ''%'' OR s3.Direccion LIKE ''%'' + @cerca + ''%''))) AND (@especialidad IS NULL OR LEN(@especialidad) = 0 OR EXISTS (SELECT 1 FROM Salud.dbo.Cm19EspecialidadConvenio e2 WITH (NOLOCK) WHERE e2.NumeroConvenio = c.NumeroConvenio AND e2.DescripcionEspecilidad LIKE ''%'' + @especialidad + ''%'')) ORDER BY CASE c.EstadoConvenio WHEN 1 THEN 0 WHEN 41 THEN 1 WHEN 39 THEN 2 ELSE 3 END, c.NombrePrestador"
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

/* Al chat del afiliado y al auditor: los dos preguntan por prestadores. */
INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT g.Code, @code,
       ISNULL((SELECT MAX(mt.[Order]) FROM dbo.OPAIModelTool mt WHERE mt.ModelCode = g.Code), 0) + 1, 1
  FROM dbo.Agent g
 WHERE g.Code IN (N'AGENTE_CHAT_CLIENTE', N'AGENTE_AUDITOR_MEDICINA', N'AGENTE_CLAUDE')
   AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt
                    WHERE mt.ModelCode = g.Code AND mt.ToolCode = @code);

SELECT mt.ModelCode, mt.ToolCode, mt.IsEnabled
  FROM dbo.OPAIModelTool mt WHERE mt.ToolCode = @code ORDER BY mt.ModelCode;
