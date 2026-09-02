/* =============================================================================
   REQ-030b - El chat no sabia como presentar una autorizacion
   -----------------------------------------------------------------------------
   El prompt tiene una seccion para prestadores y otra para el tarifario, pero
   NINGUNA para autorizaciones. Sin instruccion, el modelo improviso: a la
   pregunta 'puedes buscar mis autorizaciones' contesto con una tabla de tres
   columnas -fecha, prestador, estado-, sin el NUMERO, sin el motivo y sin la
   carta, y luego ofrecio 'si quiere le digo por que se nego' y 'si quiere le
   genero la carta'. Ofrecer despues lo que ya tenia delante.

   Las tres cosas venian o pueden venir en la misma respuesta de la tool:

     - el NUMERO, que se llena el 100%,
     - el MOTIVO, que ahora se traduce del catalogo MotivoNoCubierto (Req030a),
     - la CARTA, que ahora es un enlace que descarga el PDF de verdad.

   Se anade al final y solo una vez: la migracion mira si ya esta antes de
   escribir, para que correrla dos veces no duplique la seccion.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @marca nvarchar(100) = N'## Cuando le des sus autorizaciones';
DECLARE @seccion nvarchar(max) = N'

## Cuando le des sus autorizaciones, ponlas en TABLA

consultar_autorizaciones devuelve una fila por autorizacion. Ponlas asi:

| N.o | Fecha | Estado | Prestador | Carta |

- El NUMERO va PRIMERO y no se omite nunca. Es lo que le piden por telefono y lo
  que lleva al prestador: una tabla de autorizaciones sin el numero no le sirve
  de nada. Se llena siempre, asi que si no lo pones es que te lo saltaste.
- Estado en sus palabras: "Autorizada" o "No autorizada". QueSignifica te lo
  explica; no copies el texto tal cual.
- Si Prestador viene vacio ES que no consta -un tercio no lo trae-: pon un guion.
  No lo adivines por el procedimiento ni lo dejes en blanco sin decirlo.
- Las 8 mas recientes como maximo, y di cuantas mas hay.

La CARTA: cada fila trae EnlaceDeLaCarta. En la columna Carta pon un enlace
markdown con ese valor EXACTO, sin tocar la ruta:

    [Descargar](/Studio/ChatCliente/Carta?id=NNN)

No ofrezcas "si quiere se la genero": el enlace ya esta, dalo. Y si una fila no
trae EnlaceDeLaCarta, deja un guion: no inventes una ruta.

DEBAJO de la tabla, y solo para las NO autorizadas, escribe por que:

  - N.o 6094944: <texto de PorQueNoSeAutorizo>

Va debajo y no en una columna porque esos motivos son de hasta 200 caracteres y
dentro de la tabla no se leen. El texto viene del catalogo y ya esta redactado:
puedes acortarlo, pero no lo inventes ni lo suavices. Si PorQueNoSeAutorizo
viene vacio, di que el motivo no consta -no te lo imagines a partir del estado-.';

UPDATE dbo.Agent
   SET SystemPrompt = SystemPrompt + @seccion,
       ModifiedDate = SYSUTCDATETIME()
 WHERE Code = N'AGENTE_CHAT_CLIENTE'
   AND SystemPrompt IS NOT NULL
   AND CHARINDEX(@marca, SystemPrompt) = 0;

SELECT Code,
       LEN(SystemPrompt)                        AS LargoDelPrompt,
       CASE WHEN CHARINDEX(@marca, SystemPrompt) > 0
            THEN 'si' ELSE 'NO' END             AS TieneLaSeccion
  FROM dbo.Agent
 WHERE Code = N'AGENTE_CHAT_CLIENTE';
