/* =============================================================================
   REQ-020a — El segundo rol: el AFILIADO que presenta su reembolso
   -----------------------------------------------------------------------------
   Hasta ahora el Studio tenia un solo punto de vista: el auditor de Saludsa que
   recibe un sobre ya armado y lo revisa. Falta el lado de enfrente: el cliente
   que entra, pone su cedula, elige uno de sus contratos, adjunta lo que tiene y
   quiere saber YA que le cubren, cuanto, y que le falta por subir.

   Es la misma maquinaria (mismo OCR, misma tipificacion, mismas herramientas
   contra la base) contada en otro idioma. La diferencia no es tecnica, es de
   redaccion: al auditor se le habla de CON_MED-FACTURA y de umbrales; al cliente
   hay que decirle "esto si entra, te cubren el 80%, y te falta la receta".

   Lo que crea este script:
     1. Process PORTAL_CLIENTE      — los casos que abre el propio afiliado
     2. Agent   AGENTE_PORTAL_CLIENTE — el que traduce el expediente a lenguaje
                                        de cliente, con termino contractual
     3. Prompt  SKILL_EXPLICAR_AL_CLIENTE
     4. Las herramientas que ese agente puede usar (mismas de siempre: el
        agente NO inventa coberturas, las consulta)
     5. Tabla SolicitudCliente      — la sesion del afiliado: cedula, contrato
                                      elegido, caso y si ya confirmo los datos

   Idempotente y con guarda de base.
   ============================================================================= */

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    RAISERROR('Este script solo debe correr en db-nexus-test. Base actual: %s', 16, 1, @@SERVERNAME);
    RETURN;
END
GO

/* ---------------------------------------------------------------------------
   1) El proceso del portal
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Process WHERE Code = 'PORTAL_CLIENTE')
BEGIN
    INSERT INTO dbo.Process (Code, Name, Description, VersionNumber, IsActive)
    VALUES ('PORTAL_CLIENTE',
            'Solicitud del afiliado',
            'Reembolso presentado por el propio afiliado desde el portal: elige contrato, adjunta y recibe la explicacion de cobertura.',
            1, 1);
END
GO

/* ---------------------------------------------------------------------------
   2) El prompt: explicar al cliente, no al auditor
   --------------------------------------------------------------------------- */
DECLARE @promptCliente nvarchar(max) = N'
Eres quien le explica a un afiliado de Salud S.A. que va a pasar con el reembolso
que acaba de presentar. No eres un auditor y no decides: la liquidacion la hace
Saludsa. Tu trabajo es que la persona ENTIENDA su situacion antes de esperar
quince dias sin saber nada.

A quien le hablas
-----------------
A alguien que no sabe que es un deducible, ni un copago, ni una carencia, y que
acaba de gastarse su dinero en una consulta o en medicinas. Esta preocupado.
Hablale de usted, en frases cortas, sin siglas y sin jerga de seguros. Cuando
tengas que usar un termino del contrato, USALO y explicalo en la misma frase:
no lo escondas, porque es lo que va a leer despues en su liquidacion.

Los cinco terminos que hay que saber explicar
---------------------------------------------
 · DEDUCIBLE: la parte que le toca cubrir a usted antes de que el seguro empiece
   a pagar. Se acumula por ano de contrato, no por reclamo.
 · COPAGO: el porcentaje que queda a su cargo despues del deducible. Si le
   cubren el 80%, el copago es el 20% restante.
 · CARENCIA: el tiempo de espera desde que entro al plan hasta que un beneficio
   se puede usar. Antes de que se cumpla, ese gasto no se cubre aunque el plan
   lo contemple.
 · PREEXISTENCIA: una condicion que ya tenia antes de entrar al plan. Segun como
   este registrada puede estar excluida o tener condiciones especiales.
 · EXCLUSION: algo que el plan directamente no cubre, en ningun momento.

Que tienes que devolver
-----------------------
Un JSON, sin texto alrededor y sin vallas de codigo, con esta forma:

{
  "saludo": "una frase, calida y concreta, con su nombre de pila",
  "resumenUnaLinea": "en una linea, que va a pasar con lo que presento",
  "totalPresentado": numero,
  "totalEstimadoCubierto": numero o null,
  "confirmar": [
    { "campo": "Prestador", "valor": "...", "docId": n, "textoEvidencia": "..." }
  ],
  "cubierto": [
    { "concepto": "...", "valor": numero, "porcentaje": numero o null,
      "porQue": "por que SI entra, en cristiano",
      "termino": "COPAGO|DEDUCIBLE|null", "docId": n, "textoEvidencia": "..." }
  ],
  "noCubierto": [
    { "concepto": "...", "valor": numero,
      "porQue": "por que NO, explicando el termino del contrato",
      "termino": "DEDUCIBLE|CARENCIA|PREEXISTENCIA|EXCLUSION|TOPE|MORA|null",
      "queHacer": "que puede hacer, si puede hacer algo, o null",
      "docId": n, "textoEvidencia": "..." }
  ],
  "faltantes": [
    { "documento": "...", "porQue": "para que sirve ese papel",
      "comoConseguirlo": "donde se pide", "bloquea": true|false }
  ],
  "avisos": [ "..." ],
  "siguientePaso": "que tiene que hacer ahora"
}

Reglas duras
------------
 1. NO inventes coberturas ni porcentajes. Solo puedes afirmar lo que venga de
    las herramientas o del contexto que se te entrega. Si no lo sabes, di que
    Saludsa lo confirmara al liquidar y deja el numero en null. Es preferible
    "todavia no puedo decirle el porcentaje exacto" a un numero inventado.
 2. Cada afirmacion sobre un documento lleva su docId y el textoEvidencia: la
    frase literal del documento donde se ve. Es lo que permite al afiliado
    pinchar y ver el trozo de su propia factura. Sin evidencia no se afirma.
 3. Lo que SI se cubre se cuenta primero y se cuenta bien: es la buena noticia
    y es lo que la persona necesita leer. Se concreto con el monto.
 4. Lo que NO se cubre nunca se despacha con "no aplica" ni con un codigo. Di
    exactamente cual es la regla, por que se aplica a su caso, y si tiene arreglo
    (por ejemplo: "falta la receta; si la adjunta, este gasto vuelve a entrar").
 5. Si detectas mora, impedimento o un contrato inactivo, dilo con claridad y
    sin dramatismo: es lo primero que bloquea todo lo demas.
 6. El valor que el afiliado tecleo puede no coincidir con la factura. Eso es un
    AVISO, no un rechazo: se le dice cual es el valor que consta en el documento.
 7. Nunca escribas el nombre del paciente en textoEvidencia.
 8. Nada de disculpas ni de relleno. Frases cortas.
';

IF NOT EXISTS (SELECT 1 FROM dbo.OPAIPrompt WHERE Code = 'SKILL_EXPLICAR_AL_CLIENTE')
BEGIN
    INSERT INTO dbo.OPAIPrompt (Code, Content, VersionNumber, CreatedDate, ModifiedDate, IsActive)
    VALUES ('SKILL_EXPLICAR_AL_CLIENTE', @promptCliente, 1, GETDATE(), GETDATE(), 1);
END
ELSE
BEGIN
    UPDATE dbo.OPAIPrompt
       SET Content = @promptCliente,
           ModifiedDate = GETDATE(),
           VersionNumber = VersionNumber + 1
     WHERE Code = 'SKILL_EXPLICAR_AL_CLIENTE';
END
GO

/* ---------------------------------------------------------------------------
   3) El agente del portal
   --------------------------------------------------------------------------- */
DECLARE @sysCliente nvarchar(max) = N'Eres el asistente del portal de afiliados de Salud S.A.
Acompanas a una persona que acaba de presentar su reembolso y le explicas, en su
idioma, que le van a cubrir, que no, y por que. No decides la liquidacion: la
decide Saludsa. Consulta SIEMPRE las herramientas antes de afirmar una cobertura;
si una herramienta no responde, dilo en vez de suponer.';

IF NOT EXISTS (SELECT 1 FROM dbo.Agent WHERE Code = 'AGENTE_PORTAL_CLIENTE')
BEGIN
    INSERT INTO dbo.Agent (Code, ConfigCode, Name, VersionNumber, Description,
                           CreatedDate, ModifiedDate, IsActive,
                           ModelId, SystemPrompt, MaxTokens, ThinkingMode, ToolChoice)
    VALUES ('AGENTE_PORTAL_CLIENTE', 'CLAUDE_FOUNDRY',
            'Asistente del afiliado (portal)', 1,
            'Traduce el expediente del sobre a lenguaje de cliente: que se cubre, en que porcentaje, que falta y por que, con el termino contractual explicado.',
            GETDATE(), GETDATE(), 1,
            'claude-opus-4-8', @sysCliente, 6000, 'adaptive', 'auto');
END
ELSE
BEGIN
    UPDATE dbo.Agent
       SET SystemPrompt = @sysCliente, ModifiedDate = GETDATE(), IsActive = 1
     WHERE Code = 'AGENTE_PORTAL_CLIENTE';
END
GO

/* ---------------------------------------------------------------------------
   4) El prompt apilado del agente
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.OPAIModelPrompt
               WHERE ModelCode = 'AGENTE_PORTAL_CLIENTE' AND PromptCode = 'SKILL_EXPLICAR_AL_CLIENTE')
BEGIN
    INSERT INTO dbo.OPAIModelPrompt (ModelCode, PromptCode, [Order], IsDefault)
    VALUES ('AGENTE_PORTAL_CLIENTE', 'SKILL_EXPLICAR_AL_CLIENTE', 1, 1);
END
GO

/* ---------------------------------------------------------------------------
   5) Las herramientas que puede usar
      El guardian D2 exige el vinculo explicito: sin esta tabla el ejecutor
      deniega la llamada, que es justo lo que se quiere (el agente del cliente
      no puede alcanzar herramientas que no le corresponden).
   --------------------------------------------------------------------------- */
;WITH permitidas AS (
    SELECT * FROM (VALUES
        ('resolver_contrato_por_cedula',        1),
        ('consultar_coberturas_plan',           2),
        ('consultar_deducible_contrato',        3),
        ('consultar_preexistencias_por_cedula', 4),
        ('consultar_coberturas_convenio',       5),
        ('consultar_beneficio_convenio',        6),
        ('buscar_medicina_prestador_vademecum', 7),
        ('validar_medicina_vademecum',          8),
        ('historial_reembolsos_cliente_bd',     9)
    ) AS t(ToolCode, Orden)
)
INSERT INTO dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT 'AGENTE_PORTAL_CLIENTE', p.ToolCode, p.Orden, 1
  FROM permitidas p
  JOIN dbo.OPAITool t ON t.Code = p.ToolCode        -- solo las que existen de verdad
 WHERE NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool m
                    WHERE m.ModelCode = 'AGENTE_PORTAL_CLIENTE' AND m.ToolCode = p.ToolCode);
GO

/* ---------------------------------------------------------------------------
   6) La sesion del afiliado
      Guarda a que contrato dijo pertenecer y si confirmo lo que se leyo de sus
      documentos. La confirmacion importa: es el momento en que el afiliado se
      hace responsable de que el prestador y el valor son los suyos.
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.SolicitudCliente', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SolicitudCliente
    (
        Id                  int IDENTITY(1,1) NOT NULL
                            CONSTRAINT PK_SolicitudCliente PRIMARY KEY,
        CaseCode            uniqueidentifier NOT NULL,
        Cedula              varchar(20)      NOT NULL,
        NumeroContrato      varchar(20)      NULL,
        CodigoProducto      varchar(20)      NULL,
        CodigoRegion        varchar(20)      NULL,
        CodigoPlan          varchar(40)      NULL,
        NombrePlan          varchar(200)     NULL,
        NombreTitular       varchar(200)     NULL,
        NumeroPersona       int              NULL,
        /* La foto del contrato tal como la devolvio la API al elegirlo: si el
           dato cambia despues, se sigue sabiendo con que se decidio. */
        ContratoJson        nvarchar(max)    NULL,
        /* Lo que el afiliado dice que gasto; puede no coincidir con la factura
           y eso es un aviso, no un rechazo. */
        ValorPresentado     decimal(18,2)    NULL,
        Estado              varchar(30)      NOT NULL
                            CONSTRAINT DF_SolicitudCliente_Estado DEFAULT 'BORRADOR',
        DatosConfirmados    bit              NOT NULL
                            CONSTRAINT DF_SolicitudCliente_Conf DEFAULT 0,
        FechaConfirmacion   datetime2        NULL,
        /* La ultima explicacion generada, para no re-llamar al modelo al volver. */
        ExplicacionJson     nvarchar(max)    NULL,
        CreatedDate         datetime2        NOT NULL
                            CONSTRAINT DF_SolicitudCliente_Fecha DEFAULT SYSUTCDATETIME(),
        ModifiedDate        datetime2        NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_SolicitudCliente_CaseCode' AND object_id = OBJECT_ID('dbo.SolicitudCliente'))
BEGIN
    CREATE UNIQUE INDEX IX_SolicitudCliente_CaseCode
        ON dbo.SolicitudCliente (CaseCode);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_SolicitudCliente_Cedula' AND object_id = OBJECT_ID('dbo.SolicitudCliente'))
BEGIN
    CREATE INDEX IX_SolicitudCliente_Cedula
        ON dbo.SolicitudCliente (Cedula, CreatedDate DESC);
END
GO

/* ---------------------------------------------------------------------------
   Verificacion
   --------------------------------------------------------------------------- */
SELECT 'proceso PORTAL_CLIENTE' AS Pieza,
       CASE WHEN EXISTS (SELECT 1 FROM dbo.Process WHERE Code='PORTAL_CLIENTE') THEN 'OK' ELSE 'FALTA' END AS Estado
UNION ALL SELECT 'agente AGENTE_PORTAL_CLIENTE',
       CASE WHEN EXISTS (SELECT 1 FROM dbo.Agent WHERE Code='AGENTE_PORTAL_CLIENTE' AND IsActive=1) THEN 'OK' ELSE 'FALTA' END
UNION ALL SELECT 'prompt SKILL_EXPLICAR_AL_CLIENTE',
       CASE WHEN EXISTS (SELECT 1 FROM dbo.OPAIPrompt WHERE Code='SKILL_EXPLICAR_AL_CLIENTE') THEN 'OK' ELSE 'FALTA' END
UNION ALL SELECT 'herramientas vinculadas',
       CONVERT(varchar(10), (SELECT COUNT(*) FROM dbo.OPAIModelTool WHERE ModelCode='AGENTE_PORTAL_CLIENTE'))
UNION ALL SELECT 'tabla SolicitudCliente',
       CASE WHEN OBJECT_ID('dbo.SolicitudCliente','U') IS NOT NULL THEN 'OK' ELSE 'FALTA' END;
GO
