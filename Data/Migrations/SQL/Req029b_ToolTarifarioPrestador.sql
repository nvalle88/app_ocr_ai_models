/* =============================================================================
   REQ-029b - El tarifario: cuanto costaria ahi
   -----------------------------------------------------------------------------
   Tool APARTE, y con su propia conexion, por una razon que ya nos costo un
   fallo delante de un afiliado: Tarifario vive en la base Saludsa de salud37 y
   los convenios en Salud de SQLMIGRACION. Son SERVIDORES DISTINTOS. Meter
   Saludsa.Tarifario.PrestacionPrestador en la consulta de convenios reventaba
   con 'Invalid object name', y no hay forma de unirlas: se consultan por
   separado y se cruzan por NumeroConvenio fuera de la base.

   -- Que aporta -------------------------------------------------------------
   El PRECIO NEGOCIADO. Medido: 87.816 prestaciones en 213 convenios, 73.857 con
   PVP. Es lo que permite contestar 'cuanto me costaria una resonancia ahi' en
   vez de solo 'ese sitio tiene convenio'.

   Y trae los DOS nombres: como lo llama el prestador (RES010 'RM DE TORAX') y
   como lo llamamos nosotros. Esa doble nomenclatura es la misma que hace falta
   para liquidar, asi que sirve para las dos cosas.

   -- Lo que hay que decir cuando no hay ------------------------------------
   Solo 213 convenios tienen tarifario. Que no haya NO significa que no cubran:
   significa que el precio no esta negociado por escrito y se liquidara por
   arancel. Confundirlo seria negarle cobertura a alguien por una laguna
   administrativa.

   -- Un aviso sobre la calidad del dato ------------------------------------
   Mirando la muestra hay emparejamientos que no cuadran: el codigo RES010 'RM DE
   TORAX Y APARATO CARDIOVASCULAR' del prestador esta mapeado a 'PESS VISUAL' de
   Saludsa. No se corrige aqui -es dato de negocio- pero por eso la tool devuelve
   los dos nombres: para que quien lo lea pueda ver que no cuadran.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @code nvarchar(100)    = N'tarifario_prestador';
DECLARE @desc nvarchar(1000)   = N'El precio NEGOCIADO de una prestacion y QUE SERVICIOS hace cada prestador -Rayos X, Laboratorio, Ecografia, Resonancia, Tomografia, Terapias...- con el nombre real del catalogo. Filtra por servicio, que es fiable, o por nombre. Devuelve NumeroConvenio para pegarlo con buscar_prestador_convenio. PrecioVigente es el negociado, PrecioDeLista casi siempre es mayor.';
DECLARE @schema nvarchar(max)  = N'{
  "type": "object",
  "properties": {
    "servicio": {
      "type": "string",
      "description": "QUE tipo de servicio, con el CODIGO del catalogo. Es el filtro FIABLE: buscar ''rayos x'' por nombre encuentra 65 convenios, por servicio son 90 -hay centros que hacen rayos X y llaman a sus prestaciones ''ANTEBRAZO AP-L''-. Codigos: rx=Rayos X, lc=Laboratorio clinico, ed=Ecografia, rm=Resonancia Magnetica, t=Tomografia (TAC), ma=Mamografia, des=Densitometria, te=Terapias, odo=Odontologia, cm=Consulta Medica, hm=Honorarios Medicos, p=Procedimientos, pa=Paquetes, me=Medicinas, va=Vacunas, emer=Emergencia, amb=Ambulancia, qui=Quirofano, cya=Cuarto y Alimento, so=Servicios hospitalarios, ti=Terapia intensiva, em=Equipos Medicos, su=Suministros, aos=Administracion y otros. OJO: t es Tomografia, las terapias son te."
    },
    "prestacion": {
      "type": "string",
      "description": "Parte del nombre de la prestacion, si se busca una concreta. Combinalo con servicio, no lo uses solo: el 34,8% de las prestaciones se llaman ''no homologada'' o no tienen nombre, y esas no salen."
    },
    "numeroConvenio": {
      "type": "string",
      "description": "Convenio del prestador. NO filtra: ORDENA. Si ese prestador tiene la prestacion sale primero, y si no, salen los de otros de mas barato a mas caro marcados como referencia. Es la MISMA llave que devuelve buscar_prestador_convenio: con ella se pegan las dos listas."
    },
    "codigoSaludsa": {
      "type": "string",
      "description": "Codigo de la prestacion en Saludsa, si ya se conoce."
    },
    "tipoTarifa": {
      "type": "string",
      "description": "Nivel del plan: n3a/n4a/n5a ambulatorio, n3h/n4h/n5h hospitalario. Sin esto sale la MAS BARATA vigente, asi que entonces di ''desde X''."
    }
  },
  "required": []
}';
DECLARE @binding nvarchar(max) = N'{
  "connection": "SaludsaCreditoFarmacia",
  "maxRows": 30,
  "query": "SELECT TOP 30 t.NumeroConvenio, /* Es la UNICA llave con la que se puede pegar esta lista a la de buscar_prestador_convenio: el nombre del prestador NO esta en el tarifario -PrestadorTarifario no tiene columna de nombre- y las dos fuentes viven en SERVIDORES DISTINTOS, asi que no hay JOIN posible. */ CASE WHEN @numeroConvenio IS NULL OR LEN(@numeroConvenio) = 0 THEN ''consulta general'' WHEN t.NumeroConvenio = TRY_CAST(@numeroConvenio AS int) THEN ''ESTE es el prestador que se pidio'' ELSE ''OTRO prestador: es una REFERENCIA de lo que cuesta en otro sitio, no lo que cobra el suyo'' END AS DeQuienEsEstePrecio, /* QUE HACE el prestador, con su nombre de verdad. Sale del catalogo Tarifario.TipoServicio, no de adivinar el codigo: ''t'' es Tomografia y no Terapia, que es ''te''. Y NombreGrupoPrestacionS -lo que esta tool devolvia como Grupo- esta NULL en las 87.816 filas: era una columna que no podia decir nada. */ ts.Nombre AS Servicio, t.NombrePrestacionS AS ComoLoLlamamosNosotros, t.NombrePrestacionP AS ComoLoLlamaElPrestador, t.CodigoPrestacionS AS CodigoDeSaludsa, tar.Valor AS PrecioVigente, t.PVP AS PrecioDeLista, tar.IdentTipoTarifario AS TipoTarifa, CONVERT(varchar(10), tar.FechaInicio, 120) AS VigenteDesde, CONVERT(varchar(10), tar.FechaFin, 120) AS VigenteHasta, CASE WHEN t.PrecioPreferencialPrestacion = 1 THEN ''precio preferencial'' END AS Preferencial, t.ObservacionPrestacion AS Observacion, CASE WHEN p.Hospitalario = 1 THEN ''atiende hospitalario'' END AS Hospitalario, p.PorcentajeDescuento AS DescuentoNegociado FROM Tarifario.PrestacionPrestador t WITH (NOLOCK) LEFT JOIN Tarifario.PrestadorTarifario p WITH (NOLOCK) ON p.NumeroConvenio = t.NumeroConvenio LEFT JOIN Tarifario.TipoServicio ts WITH (NOLOCK) ON ts.IdentServicio = t.IdentServicio OUTER APPLY (SELECT TOP 1 tt.Valor, tt.IdentTipoTarifario, tt.FechaInicio, tt.FechaFin FROM Tarifario.Tarifario tt WITH (NOLOCK) WHERE tt.IdPrestacionPrestador = t.IdPrestacionPrestador AND (@tipoTarifa IS NULL OR LEN(@tipoTarifa) = 0 OR tt.IdentTipoTarifario = @tipoTarifa) AND (tt.FechaFin IS NULL OR tt.FechaFin >= CAST(GETDATE() AS date)) ORDER BY tt.Valor ASC) tar /* Fuera lo que no se puede ensenar: 18.858 prestaciones se llaman ''Prestacion no homologada'' y 11.694 no tienen nombre -34,8% del total-. Una fila sin nombre en una tabla de precios no es informacion, es ruido con un numero. */ WHERE NULLIF(LTRIM(RTRIM(t.NombrePrestacionS)), '''') IS NOT NULL AND t.NombrePrestacionS NOT LIKE ''%no homologada%'' /* El SERVICIO es el filtro fiable, no el texto del nombre. Medido: buscar ''rayos x'' por nombre encuentra 65 convenios, pero por IdentServicio=''rx'' son 90. AXXISCAN hace rayos X y ninguna de sus prestaciones se llama asi: se llaman ''ANTEBRAZO AP-L'' o ''PIE 3 POSC. AP, L Y OBLICUA''. Buscar por el texto se los saltaba. */ AND (@servicio IS NULL OR LEN(@servicio) = 0 OR t.IdentServicio = @servicio) AND (@prestacion IS NULL OR LEN(@prestacion) = 0 OR t.NombrePrestacionS LIKE ''%'' + @prestacion + ''%'' OR t.NombrePrestacionP LIKE ''%'' + @prestacion + ''%'') AND (@codigoSaludsa IS NULL OR LEN(@codigoSaludsa) = 0 OR t.CodigoPrestacionS = TRY_CAST(@codigoSaludsa AS int)) /* El convenio NO filtra: ORDENA. Si el prestador que se pidio tiene la prestacion, sale primero. Si no la tiene, en vez de devolver vacio salen los de OTROS prestadores de mas barato a mas caro, marcados como referencia. */ ORDER BY CASE WHEN @numeroConvenio IS NOT NULL AND LEN(@numeroConvenio) > 0 AND t.NumeroConvenio = TRY_CAST(@numeroConvenio AS int) THEN 0 ELSE 1 END, CASE WHEN tar.Valor IS NULL THEN 1 ELSE 0 END, tar.Valor ASC"
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
 WHERE g.Code IN (N'AGENTE_CHAT_CLIENTE', N'AGENTE_AUDITOR_MEDICINA', N'AGENTE_CLAUDE')
   AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt
                    WHERE mt.ModelCode = g.Code AND mt.ToolCode = @code);

SELECT mt.ModelCode, mt.ToolCode FROM dbo.OPAIModelTool mt WHERE mt.ToolCode = @code ORDER BY mt.ModelCode;
