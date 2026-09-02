/* =============================================================================
   REQ-030d - El telefono de Servicio al Cliente: 6020920
   -----------------------------------------------------------------------------
   Req030c le prohibio dar telefonos porque se habia inventado uno, y lo dejo sin
   ninguno: la salida era "el numero lo tiene en su carnet y en la pagina". Eso
   es honesto pero pobre — al afiliado se le puede dar el numero.

   Nestor dio el 6020920, y ademas esta en los propios textos de Saludsa.
   Comprobado en salud34:
     - 4 motivos del catalogo MotivoNoCubierto lo dan ("Para mas informacion
       llamar al 6020920"), entre ellos el 2556 y el 2557, que son de los mas
       usados,
     - 141.593 autorizaciones de los ultimos 90 dias ya lo devuelven dentro del
       texto de su motivo, o sea que Saludsa ya se lo esta dando a la gente,
     - no hay OTRO telefono compitiendo: ningun texto de contacto del catalogo
       trae uno distinto.

   Se comprueba de donde sale antes de escribirlo, porque poner un telefono en el
   prompt sin mirarlo seria exactamente lo que hizo mal el chat.

   -- Por que se reescribe la seccion entera ---------------------------------
   La seccion de Req030c es la ultima del prompt, asi que se corta por su titulo
   y se vuelve a escribir completa. Parchear frases sueltas dentro de un texto de
   varias lineas obliga a acertar tambien con los saltos -LF o CRLF-, y un
   REPLACE que no encuentra nada no falla: se queda callado y uno cree que
   aplico. Cortar y reescribir sale igual corriendola dos veces.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @marca   nvarchar(100) = N'## NUNCA des un telefono';
DECLARE @marca2  nvarchar(100) = N'## Los datos de contacto';
DECLARE @salto   nvarchar(10)  = CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10);

DECLARE @seccion nvarchar(max) = N'## Los datos de contacto: el 6020920, y nada mas

El unico telefono que puedes dar es el de Servicio al Cliente de Salud S.A.:

    6020920

Ese esta comprobado: lo dan 4 motivos del catalogo y 141.593 autorizaciones de
los ultimos 90 dias ya lo devuelven en su texto. Puedes darlo sin que ninguna
herramienta te lo repita.

Cualquier OTRO dato de contacto -otro telefono, una direccion, un correo, una
web, un horario o el nombre de una oficina- se da SOLO si viene escrito en lo que
devolvio una herramienta. Si no viene, no se da: no lo deduzcas, no lo compongas
y no lo recuerdes de otro sitio.

Paso de verdad: el chat cerro una respuesta con "puede llamar al 1700 PLAN PLAN
(1700 752675)". Ese numero no estaba en ningun prompt ni en ninguna respuesta de
herramienta. A un afiliado un telefono equivocado le cuesta una llamada perdida,
y la siguiente vez ya no cree lo demas que le digas.

Y el 6020920 es de Salud S.A., NO de un prestador. Si le hablas de una clinica o
de un medico, el telefono y la direccion salen de buscar_prestador_convenio, que
devuelve Telefono y Direccion. Si vienen vacios, di que no constan. Nunca pongas
el 6020920 como si fuera el numero del prestador.';

UPDATE dbo.Agent
   SET SystemPrompt =
         CASE
           -- ya estaba una version de esta seccion: se corta y se reescribe
           WHEN CHARINDEX(@marca2, SystemPrompt) > 0
             THEN LEFT(SystemPrompt, CHARINDEX(@marca2, SystemPrompt) - 1) + @seccion
           -- estaba la de Req030c: se sustituye por esta
           WHEN CHARINDEX(@marca,  SystemPrompt) > 0
             THEN LEFT(SystemPrompt, CHARINDEX(@marca,  SystemPrompt) - 1) + @seccion
           ELSE SystemPrompt + @salto + @seccion
         END,
       ModifiedDate = SYSUTCDATETIME()
 WHERE Code = N'AGENTE_CHAT_CLIENTE'
   AND SystemPrompt IS NOT NULL;

SELECT Code,
       LEN(SystemPrompt) AS LargoDelPrompt,
       CASE WHEN CHARINDEX(N'6020920', SystemPrompt) > 0 THEN 'si' ELSE 'NO' END AS TieneElNumero,
       CASE WHEN CHARINDEX(@marca, SystemPrompt) > 0
            THEN 'DUPLICADA' ELSE 'no' END                                       AS SeccionVieja,
       CASE WHEN CHARINDEX(N'a que numero llamar', SystemPrompt) > 0
            THEN 'AUN LA PIDE' ELSE 'corregida' END                              AS LineaDeSalida
  FROM dbo.Agent
 WHERE Code = N'AGENTE_CHAT_CLIENTE';
