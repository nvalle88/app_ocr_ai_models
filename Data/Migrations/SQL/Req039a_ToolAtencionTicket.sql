/* =============================================================================
   REQ-039a - La atencion del ticket, contada al afiliado
   -----------------------------------------------------------------------------
   Cierra la cadena del sobre: consultar_ticket_sobre da el numero, y esta lee la
   atencion y la explica. Nestor: "buscar el ticket en zendesk pq vas hasta el
   bdd_message broker para buscar el id de ticket y consultar por zendesk e
   interpretar la respuesta o explicarle al cliente".

   -- Donde estaban las credenciales ----------------------------------------
   No en un fichero: en la tabla de parametros, como hace api-comunicacion
   (ProxyZendesk.cs -> DbConfig.ObtenerValorParametro). Se leen de
   Saludsa.Administracion.ParametroServicioWeb en arranque y se cachean. No se
   copian a appsettings a proposito: ahi se pueden rotar sin desplegar, y una
   copia crea una segunda verdad que se desincroniza en silencio.

   -- Tres trampas, las tres comprobadas ------------------------------------
   1. HAY DOS INSTANCIAS de Zendesk. Los tickets de reembolso viven en
      servicioexperience1562940791; la otra (…1692030147) es de los demas
      servicios. El MISMO token contra la instancia equivocada devuelve 404, que
      se lee como "ese ticket no existe" cuando existe. Medido con el ticket
      55124 del sobre NA-2612807: 404 en una, 200 en la otra.

   2. LOS NOMBRES VIENEN EMPAREJADOS: TokenZenDesk<X> con UriZenDesk<X>.
      Mezclarlos da 404 o 401.

   3. ZendeskUserName/ZendeskAPIKey NO SIRVE para leer. mktdigital@saludsa.com.ec
      autentica pero devuelve 403 Forbidden: en Zendesk el permiso lo da el
      correo del usuario, no la clave. Es la trampa que ya habia mordido antes.

   Comprobado, ticket 55124: "Requerimiento Aplicacion Mobile - Reembolso",
   estado new, creado 2026-09-02.

   -- Lo que NO devuelve ----------------------------------------------------
   Solo comentarios PUBLICOS, y los cuatro ultimos. El ticket entero trae notas
   internas, correos del personal y campos de gestion: nada de eso es del
   afiliado y ademas ahoga al modelo. Y el estado va traducido -QueSignificaElEstado-
   porque "pending" no le dice nada a nadie: lo que le dice algo es "estan
   esperando un papel suyo".
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @c nvarchar(100)  = N'consultar_atencion_ticket';
DECLARE @d nvarchar(1000) = N'EN QUE VA LA ATENCION DE UN TICKET: estado, fechas y los comentarios PUBLICOS, para explicarle al afiliado por que se demora su reembolso o que le estan pidiendo. El numero de ticket lo da consultar_ticket_sobre. Devuelve QueSignificaElEstado ya traducido. NO trae notas internas ni correos del personal, a proposito. Si el estado es pending, los comentarios suelen decir que falta: eso es lo que hay que contarle.';
DECLARE @e nvarchar(max)  = N'{
  "type": "object",
  "properties": {
    "ticket": {
      "type": "string",
      "description": "Numero de ticket, tal como lo devolvio consultar_ticket_sobre. Es un entero."
    }
  },
  "required": [
    "ticket"
  ]
}';
DECLARE @b nvarchar(max)  = N'{
  "connection": "ServicioExperienceReembolsoElectronico"
}';

IF EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Code = @c)
    UPDATE dbo.OPAITool
       SET Name = @c, Description = @d, InputSchema = @e,
           BindingType = N'Zendesk', BindingConfig = @b, IsActive = 1
     WHERE Code = @c;
ELSE
    INSERT dbo.OPAITool (Code, Name, Description, InputSchema, BindingType, BindingConfig,
                         Strict, IsActive, VersionNumber)
    VALUES (@c, @c, @d, @e, N'Zendesk', @b, 0, 1, 1);

INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT g.Code, @c,
       ISNULL((SELECT MAX(mt.[Order]) FROM dbo.OPAIModelTool mt WHERE mt.ModelCode = g.Code), 0) + 1, 1
  FROM dbo.Agent g
 WHERE g.Code = N'AGENTE_CHAT_CLIENTE'
   AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt
                    WHERE mt.ModelCode = g.Code AND mt.ToolCode = @c);

SELECT Code, BindingType, IsActive,
       EnElChat = CASE WHEN EXISTS (SELECT 1 FROM dbo.OPAIModelTool m
                                     WHERE m.ToolCode = Code AND m.IsEnabled = 1
                                       AND m.ModelCode = 'AGENTE_CHAT_CLIENTE')
                       THEN 'SI' ELSE 'no' END
  FROM dbo.OPAITool WHERE Code = @c;
