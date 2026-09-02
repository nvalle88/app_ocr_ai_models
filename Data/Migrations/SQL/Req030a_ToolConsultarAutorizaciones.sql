/* =============================================================================
   REQ-030 - Autorizaciones: estado, motivo y la carta
   -----------------------------------------------------------------------------
   'Me autorizaron la resonancia?' y 'por que me la negaron?' son dos de las
   preguntas mas frecuentes, y no habia con que contestarlas.

   Viven en bdd_Salud_Consultas.dbo.Autorizacion -salud34-, que es el modelo del
   portal / DrSalud / IMED con motor de triaje IA. NO confundir con
   Salud.dbo.Li01Autorizacion, que es otro modelo.

   -- El motivo REAL de la negativa ------------------------------------------
   EstadoCobertura dice Cubierto o No Cubierto, y DetalleMotivoNoCubierto dice
   POR QUE. Ese campo es lo que convierte 'se lo negaron' en algo que el afiliado
   puede resolver, asi que se devuelve siempre.

   -- La carta NO esta guardada ----------------------------------------------
   La genera Armonix on-demand:

       GET ServicioArmonix/api/autorizacion/getLetterBase64/{Id}/{Estado}/{ciudad}

   y vuelve en base64. Meter eso en la respuesta de un chat serian cientos de
   kilobytes de texto, asi que la tool devuelve la RUTA y el Id, y la descarga es
   un paso aparte. La carta tambien queda adjunta en el ticket de Zendesk, con el
   nombre empezando por CARTA.

   -- La cedula NO sale del chat --------------------------------------------
   El parametro cedula tiene que venir del contrato YA RESUELTO que se le pasa al
   agente al principio del mensaje. Si saliera de lo que escribe la persona,
   bastaria con teclear una cedula ajena para leer las autorizaciones -y los
   diagnosticos- de otro. Va dicho en la descripcion del parametro, donde el
   modelo lo lee cada vez.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @code nvarchar(100)    = N'consultar_autorizaciones';
DECLARE @desc nvarchar(1000)   = N'Las autorizaciones medicas del afiliado con su ESTADO y el motivo real cuando se negaron: Cubierto o No Cubierto, con DetalleMotivoNoCubierto. Busca por numero, cedula o contrato, y filtra por estado y fecha. Devuelve tambien la ruta para generar la CARTA en PDF -la genera Armonix on-demand, no esta guardada, asi que se descarga aparte-. Usala cuando pregunte si le autorizaron algo, por que se lo negaron, o quiera su carta.';
DECLARE @schema nvarchar(max)  = N'{
  "type": "object",
  "properties": {
    "numeroAutorizacion": {
      "type": "string",
      "description": "Numero de autorizacion, si el afiliado lo tiene a mano. Es lo mas exacto."
    },
    "cedula": {
      "type": "string",
      "description": "Cedula del beneficiario. TIENE que salir del contrato ya resuelto que viene al principio del mensaje, NUNCA de una cedula que la persona escriba en el chat: seria leer las autorizaciones -y los diagnosticos- de otra persona."
    },
    "contrato": {
      "type": "string",
      "description": "Numero de contrato, para ver las de todo el contrato. Del contrato resuelto, no del texto."
    },
    "estado": {
      "type": "string",
      "description": "Cubierto o No Cubierto. Sin esto salen todas, que suele ser lo que quiere el afiliado. NO identifica a nadie: hace falta ademas la cedula, el contrato o el numero de autorizacion, o no se devuelve nada."
    },
    "desde": {
      "type": "string",
      "description": "Fecha desde, formato aaaa-mm-dd. Para acotar a lo reciente."
    }
  },
  "required": []
}';
DECLARE @binding nvarchar(max) = N'{
  "connection": "SaludConsultas",
  "maxRows": 20,
  "query": "SELECT TOP 20 a.NumeroAutorizacion, CONVERT(varchar(16), a.FechaCreacion, 120) AS Fecha, a.EstadoCobertura AS Estado, CASE WHEN a.EstadoCobertura = ''Cubierto'' THEN ''AUTORIZADA: puede atenderse con esta autorizacion.'' WHEN a.EstadoCobertura = ''No Cubierto'' THEN ''NO autorizada. El motivo va en PorQueNoSeAutorizo.'' ELSE ''En tramite todavia: aun no hay decision.'' END AS QueSignifica, a.DetalleMotivoNoCubierto AS PorQueNoSeAutorizo, a.NombrePrestadorEmpresa AS Prestador, a.TipoReclamo, a.LugarAtencion, a.CodigoProcedimiento, a.Canal, a.ContratoNumero, a.PersonaNumero, /* La carta la GENERA Armonix on-demand, no esta guardada: se compone el enlace y se descarga aparte. Devolver el PDF aqui seria meter cientos de kilobytes en base64 dentro de una respuesta de chat. */ ''ServicioArmonix/api/autorizacion/getLetterBase64/'' + CONVERT(varchar(12), a.Id) + ''/'' + a.EstadoCobertura + ''/{ciudad}'' AS RutaDeLaCarta, a.Id AS IdParaLaCarta FROM dbo.Autorizacion a WITH (NOLOCK) /* SIN identificar a NADIE no se devuelve nada. Sin esta guarda, pedir solo estado=''No Cubierto'' devolvia las autorizaciones -y con ellas los prestadores y los procedimientos- de personas cualesquiera. En un chat de afiliado eso es una fuga, no una consulta. Y de paso era lento: 11,5 s de barrido frente a 872 ms cuando se filtra por cedula. Hace falta AL MENOS UNO: numero de autorizacion, cedula o contrato. Estado y fecha son filtros, no identificadores: acotan, no dicen de quien. */ WHERE (LEN(ISNULL(@numeroAutorizacion,'''')) > 0 OR LEN(ISNULL(@cedula,'''')) > 0 OR LEN(ISNULL(@contrato,'''')) > 0) AND (@numeroAutorizacion IS NULL OR LEN(@numeroAutorizacion) = 0 OR a.NumeroAutorizacion = TRY_CAST(@numeroAutorizacion AS int)) AND (@cedula IS NULL OR LEN(@cedula) = 0 OR a.CedulaBeneficiario = @cedula) AND (@contrato IS NULL OR LEN(@contrato) = 0 OR a.ContratoNumero = TRY_CAST(@contrato AS int)) AND (@estado IS NULL OR LEN(@estado) = 0 OR a.EstadoCobertura = @estado) AND (@desde IS NULL OR LEN(@desde) = 0 OR a.FechaCreacion >= TRY_CAST(@desde AS date)) ORDER BY a.FechaCreacion DESC"
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
