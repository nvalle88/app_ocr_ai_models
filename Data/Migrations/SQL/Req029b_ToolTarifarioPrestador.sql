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
DECLARE @desc nvarchar(1000)   = N'El precio NEGOCIADO de una prestacion. El valor real esta en Tarifario.Valor -vigente hoy- y depende del NIVEL del plan: el tipo de tarifa codifica nivel y ambulatorio/hospitalario. Si el prestador que se pidio no tiene esa prestacion NO devuelve vacio: da la de otros de mas barato a mas caro, marcada como referencia. Lee DeQuienEsEstePrecio antes de decir un precio: puede no ser el de su prestador.';
DECLARE @schema nvarchar(max)  = N'{
  "type": "object",
  "properties": {
    "numeroConvenio": {
      "type": "string",
      "description": "Convenio del prestador, de buscar_prestador_convenio. NO filtra: ORDENA. Si ese prestador tiene la prestacion sale primero; si no la tiene, salen los de OTROS prestadores de mas barato a mas caro, marcados como referencia en DeQuienEsEstePrecio. Lee ese campo SIEMPRE antes de dar un precio."
    },
    "prestacion": {
      "type": "string",
      "description": "Parte del nombre: RESONANCIA, COLONOSCOPIA, TAC, ECO... Busca en los dos nombres."
    },
    "codigoSaludsa": {
      "type": "string",
      "description": "Codigo de prestacion de Saludsa (CodigoPrestacionS), si se conoce. Es lo mas exacto."
    },
    "tipoTarifa": {
      "type": "string",
      "description": "Tipo de tarifa, que codifica el NIVEL del plan y si es ambulatorio u hospitalario: n3a, n4a, n5a -nivel 3/4/5 ambulatorio-, n3h, n4h, n5h -hospitalario-, tpa, tph, starta, starth, skya. El precio DEPENDE del nivel contratado. Si no se pasa se devuelve el mas barato vigente, y entonces hay que decir que es el mas bajo y no necesariamente el suyo."
    }
  },
  "required": []
}';
DECLARE @binding nvarchar(max) = N'{
  "connection": "SaludsaCreditoFarmacia",
  "maxRows": 30,
  "query": "SELECT TOP 30 t.NumeroConvenio, CASE WHEN @numeroConvenio IS NULL OR LEN(@numeroConvenio) = 0 THEN ''consulta general'' WHEN t.NumeroConvenio = TRY_CAST(@numeroConvenio AS int) THEN ''ESTE es el prestador que se pidio'' ELSE ''OTRO prestador: es una REFERENCIA de lo que cuesta en otro sitio, no lo que cobra el suyo'' END AS DeQuienEsEstePrecio, t.CodigoPrestacionP AS CodigoDelPrestador, t.NombrePrestacionP AS ComoLoLlamaElPrestador, t.CodigoPrestacionS AS CodigoDeSaludsa, t.NombrePrestacionS AS ComoLoLlamamosNosotros, tar.Valor AS PrecioVigente, tar.IdentTipoTarifario AS TipoTarifa, CONVERT(varchar(10), tar.FechaInicio, 120) AS VigenteDesde, CONVERT(varchar(10), tar.FechaFin, 120) AS VigenteHasta, t.PVP AS PrecioDeLista, t.IdentServicio AS TipoServicio, t.NombreGrupoPrestacionS AS Grupo, CASE WHEN t.PrecioPreferencialPrestacion = 1 THEN ''precio preferencial'' ELSE NULL END AS Preferencial, t.ObservacionPrestacion AS Observacion, CASE WHEN p.Hospitalario = 1 THEN ''atiende hospitalario'' ELSE NULL END AS Hospitalario, p.PorcentajeDescuento AS DescuentoNegociado FROM Tarifario.PrestacionPrestador t WITH (NOLOCK) LEFT JOIN Tarifario.PrestadorTarifario p WITH (NOLOCK) ON p.NumeroConvenio = t.NumeroConvenio /* El precio de verdad esta en Tarifario.Valor, no en PVP. 123.607 tarifas para 87.216 prestaciones: una prestacion tiene VARIAS, una por tipo de tarifa. Y el tipo codifica el nivel del plan y si es ambulatorio u hospitalario -n3a, n4a, n5a, n3h...-, asi que el precio depende del nivel que tenga contratado el afiliado. Solo las VIGENTES: Estado 1 y con la fecha de hoy dentro. Una tarifa caducada no es un precio, es historia. */ OUTER APPLY (SELECT TOP 1 x.Valor, x.IdentTipoTarifario, x.FechaInicio, x.FechaFin FROM Tarifario.Tarifario x WITH (NOLOCK) WHERE x.IdPrestacionPrestador = t.IdPrestacionPrestador AND x.Estado = 1 AND (x.FechaInicio IS NULL OR x.FechaInicio <= GETDATE()) AND (x.FechaFin IS NULL OR x.FechaFin >= GETDATE()) AND (@tipoTarifa IS NULL OR LEN(@tipoTarifa) = 0 OR x.IdentTipoTarifario = @tipoTarifa) ORDER BY x.Valor ASC) tar WHERE t.Estado = 1 AND (@prestacion IS NULL OR LEN(@prestacion) = 0 OR t.NombrePrestacionP LIKE ''%'' + @prestacion + ''%'' OR t.NombrePrestacionS LIKE ''%'' + @prestacion + ''%'') AND (@codigoSaludsa IS NULL OR LEN(@codigoSaludsa) = 0 OR t.CodigoPrestacionS = TRY_CAST(@codigoSaludsa AS int)) /* El convenio NO filtra: ORDENA. Si el prestador que se pidio tiene la prestacion, sale primero. Si no la tiene, en vez de devolver vacio salen los de OTROS prestadores ordenados de mas barato a mas caro, marcados como referencia. Un vacio obliga a otra consulta y deja al afiliado sin ninguna idea del precio, y una referencia le dice al menos cuanto cuesta esto en el mercado con convenio. */ ORDER BY CASE WHEN @numeroConvenio IS NOT NULL AND LEN(@numeroConvenio) > 0 AND t.NumeroConvenio = TRY_CAST(@numeroConvenio AS int) THEN 0 ELSE 1 END, CASE WHEN tar.Valor IS NULL THEN 1 ELSE 0 END, tar.Valor ASC"
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
