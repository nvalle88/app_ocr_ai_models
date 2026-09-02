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
DECLARE @desc nvarchar(1000)   = N'El tarifario NEGOCIADO con un prestador: el precio (PVP) que Saludsa acordo para cada prestacion, con el nombre que usa el prestador y el nuestro. Contesta ''cuanto me costaria una resonancia ahi''. Busca por numero de convenio -sale de buscar_prestador_convenio- o por el nombre de la prestacion. Solo 213 convenios tienen tarifario de 87.816 prestaciones: si no hay, NO significa que no cubran, significa que el precio no esta negociado por escrito.';
DECLARE @schema nvarchar(max)  = N'{
  "type": "object",
  "properties": {
    "numeroConvenio": {
      "type": "string",
      "description": "Convenio del prestador. Se obtiene con buscar_prestador_convenio. Sin esto salen prestaciones de cualquier prestador y el precio no significa nada."
    },
    "prestacion": {
      "type": "string",
      "description": "Parte del nombre: RESONANCIA, COLONOSCOPIA, TAC, ECO... Busca en el nombre del prestador y en el nuestro."
    },
    "codigoSaludsa": {
      "type": "string",
      "description": "Codigo de prestacion de Saludsa (CodigoPrestacionS), si ya se conoce."
    }
  },
  "required": []
}';
DECLARE @binding nvarchar(max) = N'{
  "connection": "SaludsaCreditoFarmacia",
  "maxRows": 30,
  "query": "SELECT TOP 30 t.NumeroConvenio, /* El NOMBRE del prestador NO esta en esta base: vive en Salud.dbo.Co03Convenio, en el otro servidor. Se cruza por NumeroConvenio con buscar_prestador_convenio, fuera de la base. */ t.CodigoPrestacionP AS CodigoDelPrestador, t.NombrePrestacionP AS ComoLoLlamaElPrestador, t.CodigoPrestacionS AS CodigoDeSaludsa, t.NombrePrestacionS AS ComoLoLlamamosNosotros, t.PVP AS PrecioNegociado, t.IdentServicio AS TipoServicio, t.NombreGrupoPrestacionS AS Grupo, CASE WHEN t.PrecioPreferencialPrestacion = 1 THEN ''precio preferencial'' ELSE NULL END AS Preferencial, t.ObservacionPrestacion AS Observacion, CASE WHEN p.Hospitalario = 1 THEN ''atiende hospitalario'' ELSE NULL END AS Hospitalario, CASE WHEN p.PaquetesHospitalarios = 1 THEN ''tiene paquetes hospitalarios'' ELSE NULL END AS Paquetes, p.PorcentajeDescuento AS DescuentoNegociado FROM Tarifario.PrestacionPrestador t WITH (NOLOCK) LEFT JOIN Tarifario.PrestadorTarifario p WITH (NOLOCK) ON p.NumeroConvenio = t.NumeroConvenio WHERE t.Estado = 1 AND (@numeroConvenio IS NULL OR LEN(@numeroConvenio) = 0 OR t.NumeroConvenio = TRY_CAST(@numeroConvenio AS int)) AND (@prestacion IS NULL OR LEN(@prestacion) = 0 OR t.NombrePrestacionP LIKE ''%'' + @prestacion + ''%'' OR t.NombrePrestacionS LIKE ''%'' + @prestacion + ''%'') AND (@codigoSaludsa IS NULL OR LEN(@codigoSaludsa) = 0 OR t.CodigoPrestacionS = TRY_CAST(@codigoSaludsa AS int)) ORDER BY t.PVP DESC"
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
