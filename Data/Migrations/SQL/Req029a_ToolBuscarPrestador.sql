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
DECLARE @desc nvarchar(1000)   = N'Busca prestadores en los convenios: por RUC, nombre, convenio, CIUDAD, TIPO (hospital, farmacia, laboratorio, medico), ESPECIALIDAD -o el sintoma- o CERCA de un sector. Entiende las palabras del afiliado y las traduce a lo que dice la tabla. Dice si el convenio esta VIGENTE -y si se aplica el porcentaje CON convenio, que suele ser mayor-, con direccion, sector y telefono de sus sucursales.';
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
      "description": "Numero de convenio, o VARIOS separados por coma: ''11715,59141,5100023''. Es el que enlaza con tarifario_prestador. MANDALOS TODOS DE UNA VEZ: si vienes del tarifario con ocho convenios, una sola llamada con los ocho, no ocho llamadas. Cada llamada es un viaje entero y el afiliado espera."
    },
    "ciudad": {
      "type": "string",
      "description": "Ciudad, EN LAS PALABRAS DEL AFILIADO. Se traducen abreviaturas y provincias: GYE/Guayas -> Guayaquil, UIO/Pichincha -> Quito, Azuay -> Cuenca, Sto Domingo/Tsachilas -> Santo Domingo, Manabi -> Portoviejo. NO la mandes junto con numeroConvenio: la ciudad del convenio es la de su REGISTRO, no la de sus sucursales, y un prestador con sucursal ahi pero registrado en otra ciudad desaparecia. Si ya sabes el convenio, la ciudad sobra."
    },
    "tipo": {
      "type": "string",
      "description": "Tipo de prestador, EN LAS PALABRAS DEL AFILIADO. Se traduce: hospital/clinica/emergencia -> Clinica-Hospital; farmacia/botica/medicinas -> Farmacia; doctor/medico/especialista -> Medico; consultorio/centro -> Centro de Medicos; examenes/analisis/sangre -> Laboratorio; radiografia/ecografia/resonancia/rayos -> Laboratorio Imagen. Pasa lo que escribio la persona, NO lo traduzcas tu: si lo traduces, un sinonimo que no conozcas se pierde en silencio."
    },
    "especialidad": {
      "type": "string",
      "description": "Especialidad o EL SINTOMA, en las palabras del afiliado. Se traduce la parte del cuerpo al nombre de la tabla: oido/garganta/nariz -> OTORRINO, embarazada/parto -> GINECOLOGIA, ninos/bebe -> PEDIATRIA, corazon/presion -> CARDIOLOGIA, huesos/rodilla/columna -> TRAUMATOLOGIA, ojos/vista -> OFTALMOLOGIA, piel/lunar -> DERMATOLOGIA, estomago/colon -> GASTROENTEROLOGIA, prostata/orina -> UROLOGIA. OJO: solo 1.079 convenios tienen especialidad cargada; si no sale ninguno di que NO CONSTA, no que no hay especialistas."
    },
    "cerca": {
      "type": "string",
      "description": "Sector o parte de la direccion: ''Garzota'', ''Kennedy'', ''Av. 9 de Octubre''. Busca en las sucursales. Es lo que pregunta un afiliado de verdad: no ''que hospitales hay'' sino ''cual tengo cerca''."
    },
    "enCentro": {
      "type": "string",
      "description": "Nombre o numero de convenio de un CENTRO u HOSPITAL: devuelve los medicos de su STAFF. Atenderse con un medico dentro del centro suele salir mas barato que con el mismo medico por su cuenta, porque se liquida con el tarifario del centro. Hay 16.896 vinculos activos entre 336 centros y 11.276 medicos."
    },
    "soloActivos": {
      "type": "string",
      "description": "true para dejar solo los convenios vigentes (estados 1 y 41). Es lo que suele querer un afiliado: donde SI puede ir. El 41 TAMBIEN es activo, por el barrido historico."
    }
  },
  "required": []
}';
DECLARE @binding nvarchar(max) = N'{
  "connection": "SaludReclamos",
  "maxRows": 25,
  "query": "SELECT TOP 25 c.NumeroConvenio, c.Rucins AS Ruc, c.NombrePrestador AS Prestador, c.TipoPrestador, CASE c.EstadoConvenio WHEN 1 THEN ''ACTIVO'' WHEN 41 THEN ''ACTIVO (41)'' WHEN 39 THEN ''INACTIVO (39)'' ELSE ''estado '' + CONVERT(varchar(6), c.EstadoConvenio) END AS Estado, cd.NombreCiudad AS Ciudad, c.NivelPrestadorDesde AS NivelDesde, c.NivelPrestadorHasta AS NivelHasta, CASE WHEN c.EstadoConvenio IN (1, 41) THEN ''SI trabaja con Salud S.A.: al afiliado se le aplica el porcentaje CON convenio.'' ELSE ''Convenio NO vigente: se le aplica el porcentaje SIN convenio, que suele ser menor.'' END AS QueSignifica, NULLIF(LTRIM( CASE WHEN LEN(ISNULL(@tipo,'''')) > 0 AND LOWER(@tipo) <> LOWER(tt.Buscar) THEN '' tipo: '' + @tipo + '' -> '' + tt.Buscar ELSE '''' END + CASE WHEN LEN(ISNULL(@ciudad,'''')) > 0 AND LOWER(@ciudad) <> LOWER(tt.Ciudad) THEN '' ciudad: '' + @ciudad + '' -> '' + tt.Ciudad ELSE '''' END + CASE WHEN LEN(ISNULL(@especialidad,'''')) > 0 AND LOWER(@especialidad) <> LOWER(tt.Especialidad) THEN '' especialidad: '' + @especialidad + '' -> '' + tt.Especialidad ELSE '''' END ), '''') AS ComoSeTradujo, esp.Especialidades, COALESCE(NULLIF(LTRIM(RTRIM(c.DireccionConsultorio1)), ''''), suc.Direcciones) AS Direccion, NULLIF(LTRIM(RTRIM(c.DireccionConsultorio2)), '''') AS OtraDireccion, COALESCE(NULLIF(LTRIM(RTRIM(c.Celular)), ''''), NULLIF(LTRIM(RTRIM(c.Telefono2)), ''''), suc.Telefonos) AS Telefono, suc.Sucursales, suc.Sectores, suc.Servicios, staff.CuantosMedicos AS MedicosDeStaff, staff.Medicos AS AlgunosMedicosDeStaff, trabaja.Centros AS TrabajaEn, CASE WHEN staff.CuantosMedicos > 0 THEN ''Es un centro con medicos de staff: atenderse con uno de ellos suele salir mas barato que el mismo medico por su cuenta.'' WHEN trabaja.Centros IS NOT NULL THEN ''Este medico atiende dentro de un centro con convenio: preguntar por el precio de staff, que suele ser menor.'' ELSE NULL END AS SobreElStaff FROM Salud.dbo.Co03Convenio c WITH (NOLOCK) LEFT JOIN Salud.dbo.Tg04Ciudades cd WITH (NOLOCK) ON cd.CodigoCiudad = c.CodigoCiudad CROSS APPLY (SELECT Ciudad = CASE WHEN LOWER(ISNULL(@ciudad,'''')) IN (''gye'',''guayas'') THEN ''Guayaquil'' WHEN LOWER(ISNULL(@ciudad,'''')) IN (''uio'',''pichincha'') THEN ''Quito'' WHEN LOWER(ISNULL(@ciudad,'''')) IN (''azuay'') THEN ''Cuenca'' WHEN LOWER(ISNULL(@ciudad,'''')) LIKE ''%sto%domingo%'' OR LOWER(ISNULL(@ciudad,'''')) LIKE ''%tsachil%'' THEN ''Santo Domingo'' WHEN LOWER(ISNULL(@ciudad,'''')) LIKE ''%manabi%'' THEN ''Portoviejo'' ELSE ISNULL(@ciudad,'''') END, Especialidad = CASE WHEN LOWER(ISNULL(@especialidad,'''')) LIKE ''%ginecolog%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%embaraz%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%obstetr%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%parto%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%matern%'' THEN ''GINECOLOGIA'' WHEN LOWER(ISNULL(@especialidad,'''')) LIKE ''%pediatr%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%ni_o%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%bebe%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%infant%'' THEN ''PEDIATRIA'' WHEN LOWER(ISNULL(@especialidad,'''')) LIKE ''%cardio%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%corazon%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%presion%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%infarto%'' THEN ''CARDIOLOGIA'' WHEN LOWER(ISNULL(@especialidad,'''')) LIKE ''%traumat%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%ortoped%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%hueso%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%fractur%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%rodilla%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%columna%'' THEN ''TRAUMATOLOGIA'' WHEN LOWER(ISNULL(@especialidad,'''')) LIKE ''%oftalm%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%ojo%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%vista%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%lente%'' THEN ''OFTALMOLOGIA'' WHEN LOWER(ISNULL(@especialidad,'''')) LIKE ''%otorrino%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%oido%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%garganta%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%nariz%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%amigdal%'' THEN ''OTORRINO'' WHEN LOWER(ISNULL(@especialidad,'''')) LIKE ''%dermat%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%piel%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%acne%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%lunar%'' THEN ''DERMATOLOGIA'' WHEN LOWER(ISNULL(@especialidad,'''')) LIKE ''%gastro%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%estomago%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%digest%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%colon%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%higado%'' THEN ''GASTROENTEROLOGIA'' WHEN LOWER(ISNULL(@especialidad,'''')) LIKE ''%urolog%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%ri_on%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%prostata%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%orina%'' THEN ''UROLOGIA'' WHEN LOWER(ISNULL(@especialidad,'''')) LIKE ''%neuro%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%cerebro%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%migra_a%'' THEN ''NEURO'' WHEN LOWER(ISNULL(@especialidad,'''')) LIKE ''%internist%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%interna%'' THEN ''MEDICINA INTERNA'' WHEN LOWER(ISNULL(@especialidad,'''')) LIKE ''%cirug%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%operar%'' OR LOWER(ISNULL(@especialidad,'''')) LIKE ''%operacion%'' THEN ''CIRUGIA'' ELSE ISNULL(@especialidad,'''') END, Buscar = CASE WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%hospital%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%clinic%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%hospitaliz%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%emergenc%'' THEN ''Hospital'' WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%farmac%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%botic%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%medicin%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%remedi%'' THEN ''Farmacia'' WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%imagen%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%radiograf%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%ecograf%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%resonanc%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%tomograf%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%rayos%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%mamograf%'' THEN ''Laboratorio Imagen'' WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%laborator%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%examen%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%analisis%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%sangre%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%muestra%'' THEN ''Laboratorio'' WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%centro%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%consultor%'' THEN ''Centro'' WHEN LOWER(ISNULL(@tipo,'''')) LIKE ''%doctor%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%medic%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%especialist%'' OR LOWER(ISNULL(@tipo,'''')) LIKE ''%galen%'' THEN ''Medico'' ELSE ISNULL(@tipo,'''') END) tt OUTER APPLY (SELECT Sucursales = COUNT(*), Direcciones = STUFF((SELECT DISTINCT '' | '' + x.Direccion FROM Salud.dbo.Co13SucursalesConvenio x WITH (NOLOCK) WHERE x.NumeroConvenio = c.NumeroConvenio AND x.EsActivo = 1 AND x.Direccion IS NOT NULL FOR XML PATH('''')), 1, 3, ''''), Telefonos = STUFF((SELECT DISTINCT '', '' + y.Telefono1 FROM Salud.dbo.Co13SucursalesConvenio y WITH (NOLOCK) WHERE y.NumeroConvenio = c.NumeroConvenio AND y.EsActivo = 1 AND LEN(ISNULL(y.Telefono1,'''')) > 5 FOR XML PATH('''')), 1, 2, ''''), Sectores = STUFF((SELECT DISTINCT '', '' + z.Sector FROM Salud.dbo.Co13SucursalesConvenio z WITH (NOLOCK) WHERE z.NumeroConvenio = c.NumeroConvenio AND z.EsActivo = 1 AND LEN(ISNULL(z.Sector,'''')) > 1 FOR XML PATH('''')), 1, 2, ''''), Servicios = STUFF((SELECT DISTINCT '' | '' + w.Servicios FROM Salud.dbo.Co13SucursalesConvenio w WITH (NOLOCK) WHERE w.NumeroConvenio = c.NumeroConvenio AND w.EsActivo = 1 AND LEN(ISNULL(w.Servicios,'''')) > 2 FOR XML PATH('''')), 1, 3, '''') FROM Salud.dbo.Co13SucursalesConvenio s2 WITH (NOLOCK) WHERE s2.NumeroConvenio = c.NumeroConvenio AND s2.EsActivo = 1) suc OUTER APPLY (SELECT Especialidades = STUFF((SELECT DISTINCT '', '' + e.DescripcionEspecilidad FROM Salud.dbo.Cm19EspecialidadConvenio e WITH (NOLOCK) WHERE e.NumeroConvenio = c.NumeroConvenio AND e.DescripcionEspecilidad IS NOT NULL FOR XML PATH('''')), 1, 2, '''')) esp OUTER APPLY (SELECT CuantosMedicos = COUNT(*), Medicos = STUFF((SELECT DISTINCT '', '' + m.NombrePrestador FROM Salud.dbo.Co78EmpresaConvenioStaffMedico s4 WITH (NOLOCK) JOIN Salud.dbo.Co03Convenio m WITH (NOLOCK) ON m.NumeroConvenio = s4.NumeroConvenioMedico WHERE s4.NumeroConvenioEmpresa = c.NumeroConvenio AND s4.Estado = 1 AND m.EstadoConvenio IN (1,41) FOR XML PATH('''')), 1, 2, '''') FROM Salud.dbo.Co78EmpresaConvenioStaffMedico s3 WITH (NOLOCK) WHERE s3.NumeroConvenioEmpresa = c.NumeroConvenio AND s3.Estado = 1) staff OUTER APPLY (SELECT Centros = STUFF((SELECT DISTINCT '', '' + emp.NombrePrestador FROM Salud.dbo.Co78EmpresaConvenioStaffMedico s5 WITH (NOLOCK) JOIN Salud.dbo.Co03Convenio emp WITH (NOLOCK) ON emp.NumeroConvenio = s5.NumeroConvenioEmpresa WHERE s5.NumeroConvenioMedico = c.NumeroConvenio AND s5.Estado = 1 AND emp.EstadoConvenio IN (1,41) FOR XML PATH('''')), 1, 2, '''')) trabaja WHERE (@ruc IS NULL OR LEN(@ruc) = 0 OR c.Rucins = @ruc) AND (@nombre IS NULL OR LEN(@nombre) = 0 OR c.NombrePrestador LIKE ''%'' + @nombre + ''%'') /* Uno o VARIOS convenios de una vez. Medido en las llamadas reales: el agente venia del tarifario con ocho convenios y hacia OCHO llamadas, una por cada uno. Cada llamada es un viaje entero al modelo, no solo los 522 ms de la consulta: era el caso lento. Con la lista, ocho viajes se vuelven uno. */ AND (@numeroConvenio IS NULL OR LEN(@numeroConvenio) = 0 OR c.NumeroConvenio IN (SELECT TRY_CAST(LTRIM(RTRIM(value)) AS int) FROM STRING_SPLIT(@numeroConvenio, '',''))) /* La ciudad NO filtra cuando ya se pidieron convenios concretos. Medido: el convenio 5100023 devolvia 0 filas con ciudad=''Guayaquil'' y sus datos sin ella. La ciudad del convenio es la de su REGISTRO, no la de sus sucursales, asi que un prestador con sucursal en Guayaquil pero registrado en Quito desaparecia. De 32 llamadas, 14 volvieron vacias por esto y el agente reintentaba. Si ya se sabe el convenio, la ciudad no aporta. */ AND (@ciudad IS NULL OR LEN(@ciudad) = 0 OR LEN(ISNULL(@numeroConvenio,'''')) > 0 OR cd.NombreCiudad LIKE ''%'' + tt.Ciudad + ''%'') AND (@soloActivos IS NULL OR LOWER(@soloActivos) NOT IN (''1'',''true'',''si'') OR c.EstadoConvenio IN (1, 41)) AND (@tipo IS NULL OR LEN(@tipo) = 0 OR c.TipoPrestador LIKE ''%'' + tt.Buscar + ''%'') AND (@cerca IS NULL OR LEN(@cerca) = 0 OR EXISTS (SELECT 1 FROM Salud.dbo.Co13SucursalesConvenio s3 WITH (NOLOCK) WHERE s3.NumeroConvenio = c.NumeroConvenio AND s3.EsActivo = 1 AND (s3.Sector LIKE ''%'' + @cerca + ''%'' OR s3.Direccion LIKE ''%'' + @cerca + ''%''))) AND (@especialidad IS NULL OR LEN(@especialidad) = 0 OR EXISTS (SELECT 1 FROM Salud.dbo.Cm19EspecialidadConvenio e2 WITH (NOLOCK) WHERE e2.NumeroConvenio = c.NumeroConvenio AND e2.DescripcionEspecilidad LIKE ''%'' + tt.Especialidad + ''%'')) AND (@enCentro IS NULL OR LEN(@enCentro) = 0 OR EXISTS (SELECT 1 FROM Salud.dbo.Co78EmpresaConvenioStaffMedico s6 WITH (NOLOCK) JOIN Salud.dbo.Co03Convenio emp2 WITH (NOLOCK) ON emp2.NumeroConvenio = s6.NumeroConvenioEmpresa WHERE s6.NumeroConvenioMedico = c.NumeroConvenio AND s6.Estado = 1 AND (emp2.NombrePrestador LIKE ''%'' + @enCentro + ''%'' OR CONVERT(varchar(12), emp2.NumeroConvenio) = @enCentro))) ORDER BY CASE c.EstadoConvenio WHEN 1 THEN 0 WHEN 41 THEN 1 WHEN 39 THEN 2 ELSE 3 END, c.NombrePrestador"
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
