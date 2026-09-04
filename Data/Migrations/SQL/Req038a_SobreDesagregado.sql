/* =============================================================================
   REQ-038a - El sobre, desagregado; y el prestador, corregido
   -----------------------------------------------------------------------------
   Nestor: "utiliza ese codigo para formar tres tools... para que quede
   desagregadas y se demoren menos las consultas".

   -- La pista era suya ------------------------------------------------------
   Yo iba a usar api-armonix/BuscarDocumentosCompleto. El dijo que mirara
   api-liquidaciones, y tenia razon: ReembolsoElectronicoController resuelve
   TODO el flujo del sobre, y ademas amarrado al afiliado.

       GET /api/ReembolsoElectronico/ObtenerSobre
           codigoContrato + personaNumero + fechas + paginado
       GET /api/ReembolsoElectronico/ObtenerDetalleSobre
           idSobre + codigoContrato + numeroPersonaBeneficiario

   Ventaja sobre Armonix, y no es menor: aquella solo aceptaba NumeroSobre suelto
   -sus filtros de contrato VACIABAN la respuesta-, asi que el guardian anti-IDOR
   no podia cubrirla. Estas dos llevan contrato y persona.

   -- Por que se parte en varias --------------------------------------------
   ObtenerDetalleSobre devuelve en UNA respuesta la cabecera, los estados, la
   liquidacion Y los ficheros. Medido en la version de Armonix: 1,69 MB en un
   sobre real. Volcarle eso al modelo cuando solo preguntaron "en que va mi
   reembolso" le revienta el contexto y encarece cada turno.

   Asi que tres tools sobre el MISMO endpoint, cada una con su porcion -Pick y
   Omit, nuevos en ToolBindingConfig-:

       consultar_detalle_sobre      cabecera y estados   sin liquidacion ni ficheros
       consultar_liquidacion_sobre  que le van a pagar   solo si ya se liquido
       consultar_documentos_sobre   sus papeles          la pesada, solo si preguntan

   Mas la de entrada (consultar_mis_reembolsos) y la del ticket, que no viene del
   servicio sino de bdd_MessageBroker.dbo.Sobre -NumeroTicketZendesk, lleno en el
   98,5% de 18.817 sobres, con CodigoContrato y NumeroPersonaBeneficiario para
   atarlo al afiliado-.

   -- Y el prestador --------------------------------------------------------
   consultar_coberturas_plan_prestador estaba DESACTIVADA desde el 21-ago por
   404. La causa no era el path: era la BASE. Estaba escrita
   'ServicioPrestador' y el servicio es 'servicioprestadores'. Se nota en el
   tamano de la respuesta -1245 bytes siempre, un 404 de IIS, frente a los
   147/239 del 404 del propio API-.

   Ademas su binding estaba mal armado: mandaba region/contrato/persona en el
   cuerpo, y el Swagger dice que quiere numeroConvenio, codigoProducto y
   codigoPlan por QUERY y codigosBeneficio en el cuerpo. Corregido y probado:
   HTTP 200 con datos.

   Y trae tipoSucursal, que es justo lo que le falta hoy a copago_del_prestador
   -el defecto de ConvenioPlanExcepcion: la misma cadena cobra distinto segun el
   tipo de local-. Por eso queda de principal y aquella de respaldo.

   -- La regla de Nestor, aplicada ------------------------------------------
   "las de servicios ganan por arriba que las que son de sql, esas quedan como
   respaldo... pero no las quites". Ninguna se borra ni se desactiva: cada tool
   nueva declara su fallbackTool a la de SQL equivalente.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

/* ---- consultar_coberturas_plan_prestador ---- */
DECLARE @c1 nvarchar(100)   = N'consultar_coberturas_plan_prestador';
DECLARE @d1 nvarchar(1000)  = N'COBERTURAS DEL PLAN EN UN PRESTADOR CONCRETO: que cubre el plan del afiliado cuando se atiende con ese convenio, y acepta tipoSucursal, que cambia el copago -una misma cadena cobra distinto segun el tipo de local-. Usala cuando ya se sabe el convenio y hay que decir lo que le van a cobrar ALLI. Necesita numeroConvenio, codigoProducto y codigoPlan.';
DECLARE @e1 nvarchar(max)   = N'{
  "type": "object",
  "properties": {
    "numeroConvenio": {
      "type": "string",
      "description": "Convenio del prestador. Obligatorio: sin el, la pregunta no tiene sentido."
    },
    "codigoProducto": {
      "type": "string",
      "description": "Del contrato ya resuelto. Obligatorio."
    },
    "codigoPlan": {
      "type": "string",
      "description": "Del contrato ya resuelto. Obligatorio."
    },
    "versionPlan": {
      "type": "string",
      "description": "Version del plan. Sin ella responde por la vigente, que puede no ser la del afiliado."
    },
    "tipoSucursal": {
      "type": "string",
      "description": "CMV, PMF y demas. CAMBIA el copago: la misma cadena cobra distinto segun el tipo de local. Si el afiliado dijo a que local va, mandalo."
    },
    "soloBeneficiosActivos": {
      "type": "string",
      "description": "true para dejar fuera los beneficios ya caducados."
    },
    "codigosBeneficio": {
      "type": "string",
      "description": "Lista de beneficios a consultar, separados por coma. A002 y A007 son la consulta medica, A003 el porcentaje de cobertura."
    }
  },
  "required": [
    "numeroConvenio",
    "codigoProducto",
    "codigoPlan"
  ]
}';
DECLARE @b1 nvarchar(max)   = N'{
  "baseUrl": "{api-prestador}",
  "method": "POST",
  "path": "/CoberturasPlan",
  "paramMap": {
    "numeroConvenio": "numeroConvenio",
    "codigoProducto": "codigoProducto",
    "codigoPlan": "codigoPlan",
    "versionPlan": "versionPlan",
    "tipoSucursal": "tipoSucursal",
    "soloBeneficiosActivos": "soloBeneficiosActivos"
  },
  "bodyMap": [
    "codigosBeneficio"
  ],
  "authMode": "saludsa-oauth",
  "fallbackTool": "copago_del_prestador"
}';

IF EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Code = @c1)
    UPDATE dbo.OPAITool
       SET Name = @c1, Description = @d1, InputSchema = @e1,
           BindingType = N'InternalApi', BindingConfig = @b1, IsActive = 1
     WHERE Code = @c1;
ELSE
    INSERT dbo.OPAITool (Code, Name, Description, InputSchema, BindingType, BindingConfig,
                         Strict, IsActive, VersionNumber)
    VALUES (@c1, @c1, @d1, @e1, N'InternalApi', @b1, 0, 1, 1);

INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT g.Code, @c1,
       ISNULL((SELECT MAX(mt.[Order]) FROM dbo.OPAIModelTool mt WHERE mt.ModelCode = g.Code), 0) + 1, 1
  FROM dbo.Agent g
 WHERE g.Code = N'AGENTE_CHAT_CLIENTE'
   AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt
                    WHERE mt.ModelCode = g.Code AND mt.ToolCode = @c1);

/* ---- consultar_mis_reembolsos ---- */
DECLARE @c2 nvarchar(100)   = N'consultar_mis_reembolsos';
DECLARE @d2 nvarchar(1000)  = N'LOS REEMBOLSOS DEL AFILIADO: la lista de sus sobres con su estado y su valor presentado, por contrato y persona, con rango de fechas y paginado. Es la PRIMERA de la cadena del sobre: de aqui sale el IdSobre que necesitan las otras. Usala para ''en que van mis reembolsos'' o ''que he presentado''. No trae ni la liquidacion ni los documentos: para eso hay una tool por cada cosa.';
DECLARE @e2 nvarchar(max)   = N'{
  "type": "object",
  "properties": {
    "codigoContrato": {
      "type": "string",
      "description": "Codigo de contrato del afiliado, del caso ya identificado. Obligatorio."
    },
    "personaNumero": {
      "type": "string",
      "description": "Numero de persona del beneficiario. Sin el salen los de todo el contrato."
    },
    "fechaDesde": {
      "type": "string",
      "description": "aaaa-mm-dd. Sin rango salen los mas recientes."
    },
    "fechaHasta": {
      "type": "string",
      "description": "aaaa-mm-dd."
    },
    "numeroRegistros": {
      "type": "string",
      "description": "Cuantos traer. Cinco o diez bastan para una conversacion."
    },
    "numeroPagina": {
      "type": "string",
      "description": "Pagina, empezando en 1."
    }
  },
  "required": [
    "codigoContrato"
  ]
}';
DECLARE @b2 nvarchar(max)   = N'{
  "baseUrl": "{api-liquidacion}",
  "method": "GET",
  "path": "/api/ReembolsoElectronico/ObtenerSobre",
  "paramMap": {
    "codigoContrato": "codigoContrato",
    "personaNumero": "personaNumero",
    "fechaDesde": "fechaDesde",
    "fechaHasta": "fechaHasta",
    "numeroPagina": "numeroPagina",
    "numeroRegistros": "numeroRegistros"
  },
  "bodyMap": [],
  "authMode": "saludsa-oauth",
  "fallbackTool": "historial_reembolsos_cliente_bd"
}';

IF EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Code = @c2)
    UPDATE dbo.OPAITool
       SET Name = @c2, Description = @d2, InputSchema = @e2,
           BindingType = N'InternalApi', BindingConfig = @b2, IsActive = 1
     WHERE Code = @c2;
ELSE
    INSERT dbo.OPAITool (Code, Name, Description, InputSchema, BindingType, BindingConfig,
                         Strict, IsActive, VersionNumber)
    VALUES (@c2, @c2, @d2, @e2, N'InternalApi', @b2, 0, 1, 1);

INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT g.Code, @c2,
       ISNULL((SELECT MAX(mt.[Order]) FROM dbo.OPAIModelTool mt WHERE mt.ModelCode = g.Code), 0) + 1, 1
  FROM dbo.Agent g
 WHERE g.Code = N'AGENTE_CHAT_CLIENTE'
   AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt
                    WHERE mt.ModelCode = g.Code AND mt.ToolCode = @c2);

/* ---- consultar_detalle_sobre ---- */
DECLARE @c3 nvarchar(100)   = N'consultar_detalle_sobre';
DECLARE @d3 nvarchar(1000)  = N'EN QUE VA UN SOBRE: titular, beneficiario, valor presentado, banco, observacion y el historial de ESTADOS por el que ha pasado. Ligera a proposito: NO trae la liquidacion ni los ficheros -para eso estan consultar_liquidacion_sobre y consultar_documentos_sobre-. Necesita el idSobre, que da consultar_mis_reembolsos.';
DECLARE @e3 nvarchar(max)   = N'{
  "type": "object",
  "properties": {
    "idSobre": {
      "type": "string",
      "description": "IdSobre, tal como lo devolvio consultar_mis_reembolsos. NO es el NumeroSobre con NA-."
    },
    "codigoContrato": {
      "type": "string",
      "description": "Contrato del afiliado. Mandalo: ata el sobre a su duenno."
    },
    "numeroPersonaBeneficiario": {
      "type": "string",
      "description": "Persona del beneficiario."
    }
  },
  "required": [
    "idSobre"
  ]
}';
DECLARE @b3 nvarchar(max)   = N'{
  "baseUrl": "{api-liquidacion}",
  "method": "GET",
  "path": "/api/ReembolsoElectronico/ObtenerDetalleSobre",
  "paramMap": {
    "idSobre": "idSobre",
    "codigoContrato": "codigoContrato",
    "numeroPersonaBeneficiario": "numeroPersonaBeneficiario"
  },
  "bodyMap": [],
  "authMode": "saludsa-oauth",
  "omit": [
    "Archivos",
    "Liquidaciones",
    "ResumenLiquidacion"
  ],
  "fallbackTool": "consultar_detalle_sobre_bd"
}';

IF EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Code = @c3)
    UPDATE dbo.OPAITool
       SET Name = @c3, Description = @d3, InputSchema = @e3,
           BindingType = N'InternalApi', BindingConfig = @b3, IsActive = 1
     WHERE Code = @c3;
ELSE
    INSERT dbo.OPAITool (Code, Name, Description, InputSchema, BindingType, BindingConfig,
                         Strict, IsActive, VersionNumber)
    VALUES (@c3, @c3, @d3, @e3, N'InternalApi', @b3, 0, 1, 1);

INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT g.Code, @c3,
       ISNULL((SELECT MAX(mt.[Order]) FROM dbo.OPAIModelTool mt WHERE mt.ModelCode = g.Code), 0) + 1, 1
  FROM dbo.Agent g
 WHERE g.Code = N'AGENTE_CHAT_CLIENTE'
   AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt
                    WHERE mt.ModelCode = g.Code AND mt.ToolCode = @c3);

/* ---- consultar_liquidacion_sobre ---- */
DECLARE @c4 nvarchar(100)   = N'consultar_liquidacion_sobre';
DECLARE @d4 nvarchar(1000)  = N'QUE LE VAN A PAGAR de un sobre: el resumen de liquidacion y su detalle, si ya se liquido. Si el sobre todavia no se liquido vuelve vacio, y eso NO es un fallo: significa que aun esta en tramite, y asi hay que decirselo. Necesita el idSobre de consultar_mis_reembolsos. No trae documentos.';
DECLARE @e4 nvarchar(max)   = N'{
  "type": "object",
  "properties": {
    "idSobre": {
      "type": "string",
      "description": "IdSobre de consultar_mis_reembolsos."
    },
    "codigoContrato": {
      "type": "string",
      "description": "Contrato del afiliado."
    },
    "numeroPersonaBeneficiario": {
      "type": "string",
      "description": "Persona del beneficiario."
    }
  },
  "required": [
    "idSobre"
  ]
}';
DECLARE @b4 nvarchar(max)   = N'{
  "baseUrl": "{api-liquidacion}",
  "method": "GET",
  "path": "/api/ReembolsoElectronico/ObtenerDetalleSobre",
  "paramMap": {
    "idSobre": "idSobre",
    "codigoContrato": "codigoContrato",
    "numeroPersonaBeneficiario": "numeroPersonaBeneficiario"
  },
  "bodyMap": [],
  "authMode": "saludsa-oauth",
  "pick": [
    "NumeroSobre",
    "ValorPresentado",
    "ValorPresentadoTexto",
    "ResumenLiquidacion",
    "Liquidaciones",
    "TextoExplicacion"
  ],
  "fallbackTool": "consultar_liquidacion_sobre_bd"
}';

IF EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Code = @c4)
    UPDATE dbo.OPAITool
       SET Name = @c4, Description = @d4, InputSchema = @e4,
           BindingType = N'InternalApi', BindingConfig = @b4, IsActive = 1
     WHERE Code = @c4;
ELSE
    INSERT dbo.OPAITool (Code, Name, Description, InputSchema, BindingType, BindingConfig,
                         Strict, IsActive, VersionNumber)
    VALUES (@c4, @c4, @d4, @e4, N'InternalApi', @b4, 0, 1, 1);

INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT g.Code, @c4,
       ISNULL((SELECT MAX(mt.[Order]) FROM dbo.OPAIModelTool mt WHERE mt.ModelCode = g.Code), 0) + 1, 1
  FROM dbo.Agent g
 WHERE g.Code = N'AGENTE_CHAT_CLIENTE'
   AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt
                    WHERE mt.ModelCode = g.Code AND mt.ToolCode = @c4);

/* ---- consultar_documentos_sobre ---- */
DECLARE @c5 nvarchar(100)   = N'consultar_documentos_sobre';
DECLARE @d5 nvarchar(1000)  = N'QUE PAPELES TIENE UN SOBRE: la lista de archivos de soporte que subio el afiliado. OJO: es la mas pesada de las tres, porque el servicio devuelve el contenido de cada fichero. Usala SOLO si preguntan por los documentos -que subi, llego mi factura-, nunca por rutina. Para el estado usa consultar_detalle_sobre.';
DECLARE @e5 nvarchar(max)   = N'{
  "type": "object",
  "properties": {
    "idSobre": {
      "type": "string",
      "description": "IdSobre de consultar_mis_reembolsos."
    },
    "codigoContrato": {
      "type": "string",
      "description": "Contrato del afiliado."
    },
    "numeroPersonaBeneficiario": {
      "type": "string",
      "description": "Persona del beneficiario."
    }
  },
  "required": [
    "idSobre"
  ]
}';
DECLARE @b5 nvarchar(max)   = N'{
  "baseUrl": "{api-liquidacion}",
  "method": "GET",
  "path": "/api/ReembolsoElectronico/ObtenerDetalleSobre",
  "paramMap": {
    "idSobre": "idSobre",
    "codigoContrato": "codigoContrato",
    "numeroPersonaBeneficiario": "numeroPersonaBeneficiario"
  },
  "bodyMap": [],
  "authMode": "saludsa-oauth",
  "pick": [
    "NumeroSobre",
    "Archivos",
    "CantidadFacturaElectronicaPdf"
  ]
}';

IF EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Code = @c5)
    UPDATE dbo.OPAITool
       SET Name = @c5, Description = @d5, InputSchema = @e5,
           BindingType = N'InternalApi', BindingConfig = @b5, IsActive = 1
     WHERE Code = @c5;
ELSE
    INSERT dbo.OPAITool (Code, Name, Description, InputSchema, BindingType, BindingConfig,
                         Strict, IsActive, VersionNumber)
    VALUES (@c5, @c5, @d5, @e5, N'InternalApi', @b5, 0, 1, 1);

INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT g.Code, @c5,
       ISNULL((SELECT MAX(mt.[Order]) FROM dbo.OPAIModelTool mt WHERE mt.ModelCode = g.Code), 0) + 1, 1
  FROM dbo.Agent g
 WHERE g.Code = N'AGENTE_CHAT_CLIENTE'
   AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt
                    WHERE mt.ModelCode = g.Code AND mt.ToolCode = @c5);

/* ---- consultar_ticket_sobre ---- */
DECLARE @c6 nvarchar(100)   = N'consultar_ticket_sobre';
DECLARE @d6 nvarchar(1000)  = N'EL TICKET DE ATENCION DE UN SOBRE y en que punto va: numero de ticket, estado en el broker, cuando se recibio, su ultimo movimiento y si vencio el plazo. Sale de la tabla que une el sobre con Zendesk, y esta llena en el 98,5% de los sobres. Usala cuando pregunten por que se demora o donde esta su tramite. Con el numero se puede consultar despues el detalle de la atencion.';
DECLARE @e6 nvarchar(max)   = N'{
  "type": "object",
  "properties": {
    "numeroSobre": {
      "type": "string",
      "description": "El numero con NA- delante, como NA-2612807."
    },
    "contrato": {
      "type": "string",
      "description": "Codigo de contrato del afiliado. Mandalo siempre: ata el sobre a su duenno."
    },
    "persona": {
      "type": "string",
      "description": "Numero de persona del beneficiario."
    }
  },
  "required": []
}';
DECLARE @b6 nvarchar(max)   = N'{
  "connection": "SaludMessageBroker",
  "maxRows": 5,
  "query": "SELECT TOP 5 s.NumeroSobre, Ticket = NULLIF(s.NumeroTicketZendesk, 0), EstadoEnElBroker = s.Estado, Recibido = CONVERT(varchar(16), s.FechaCreacion, 120), UltimoMovimiento = CONVERT(varchar(16), s.FechaActualizacion, 120), PlazoHasta = CONVERT(varchar(16), s.FechaSla, 120), VencioElPlazo = CASE WHEN s.FechaSla IS NULL THEN NULL WHEN s.FechaSla < GETDATE() THEN ''SI'' ELSE ''no'' END, NotaInterna = NULLIF(LTRIM(RTRIM(CONVERT(varchar(400), s.NotaInternaZendesk))), ''''), QueSignifica = CASE WHEN s.NumeroTicketZendesk IS NULL THEN ''Todavia no se creo el ticket de atencion de este sobre.'' ELSE ''Con este numero de ticket se puede ver el detalle de la atencion.'' END FROM dbo.Sobre s WITH (NOLOCK) WHERE (LEN(ISNULL(@numeroSobre,'''')) > 0 OR LEN(ISNULL(@contrato,'''')) > 0) AND (@numeroSobre IS NULL OR LEN(@numeroSobre) = 0 OR s.NumeroSobre = @numeroSobre) AND (@contrato IS NULL OR LEN(@contrato) = 0 OR s.CodigoContrato = TRY_CAST(@contrato AS int)) AND (@persona IS NULL OR LEN(@persona) = 0 OR s.NumeroPersonaBeneficiario = TRY_CAST(@persona AS int)) ORDER BY s.FechaCreacion DESC"
}';

IF EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Code = @c6)
    UPDATE dbo.OPAITool
       SET Name = @c6, Description = @d6, InputSchema = @e6,
           BindingType = N'Sql', BindingConfig = @b6, IsActive = 1
     WHERE Code = @c6;
ELSE
    INSERT dbo.OPAITool (Code, Name, Description, InputSchema, BindingType, BindingConfig,
                         Strict, IsActive, VersionNumber)
    VALUES (@c6, @c6, @d6, @e6, N'Sql', @b6, 0, 1, 1);

INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT g.Code, @c6,
       ISNULL((SELECT MAX(mt.[Order]) FROM dbo.OPAIModelTool mt WHERE mt.ModelCode = g.Code), 0) + 1, 1
  FROM dbo.Agent g
 WHERE g.Code = N'AGENTE_CHAT_CLIENTE'
   AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt
                    WHERE mt.ModelCode = g.Code AND mt.ToolCode = @c6);

SELECT t.Code, t.BindingType, t.IsActive,
       Json = ISJSON(CONVERT(nvarchar(max), t.BindingConfig)),
       EnElChat = CASE WHEN EXISTS (SELECT 1 FROM dbo.OPAIModelTool m
                                     WHERE m.ToolCode = t.Code AND m.IsEnabled = 1
                                       AND m.ModelCode = 'AGENTE_CHAT_CLIENTE')
                       THEN 'SI' ELSE 'no' END,
       Respaldo = JSON_VALUE(CONVERT(nvarchar(max), t.BindingConfig), '$.fallbackTool')
  FROM dbo.OPAITool t
 WHERE t.Code IN (N'consultar_coberturas_plan_prestador', N'consultar_mis_reembolsos',
                  N'consultar_detalle_sobre', N'consultar_liquidacion_sobre',
                  N'consultar_documentos_sobre', N'consultar_ticket_sobre')
 ORDER BY t.Code;
