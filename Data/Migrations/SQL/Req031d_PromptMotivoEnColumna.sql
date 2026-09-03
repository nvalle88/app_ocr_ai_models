/* =============================================================================
   REQ-031d - El motivo, en su COLUMNA
   -----------------------------------------------------------------------------
   Nestor: "en esta tabla de autorizaciones no esta la columna de explicacion".
   Enseño cinco "No autorizada" seguidas sin una sola razon.

   Dos cosas, y solo una era del prompt.

   1. El agente NO se salto la instruccion: esas cinco autorizaciones tenian
      CodigoMotivoNoCubierto en NULL. No habia nada que escribir. Medido: de las
      373 negativas de 90 dias, 141 -el 37,8%- no traen codigo.

      Pero el motivo SI estaba, en otro campo: ObservacionImportante. "Su medico
      tratante Aguirre Moreno Jose Javier no es afiliado a Saludsa", "su medico
      tratante es afiliado nivel 7 - 7 y su plan es nivel 3". Eso es exactamente
      lo que el afiliado necesita saber. La tool ahora lo usa como tercera
      fuente y la cobertura sube del 62,2% al 78,0%. Va en Req030a.

   2. Lo del prompt: el motivo iba DEBAJO de la tabla, y cuando no habia se
      omitia la seccion entera. Desde fuera eso se lee como si el chat se lo
      hubiera olvidado. Ahora es una COLUMNA, que es lo que se pidio, y cuando
      no consta LO DICE en la celda: un hueco no es una respuesta.

      El 22% restante no tiene motivo en ninguna parte. Ahi se dice que no
      consta y que puede pedirlo a Servicio al Cliente, que es la verdad.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @marca nvarchar(100) = N'## Cuando le des sus autorizaciones';
DECLARE @fin   nvarchar(100) = N'## Los datos de contacto';
DECLARE @salto nvarchar(10)  = CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10);

DECLARE @seccion nvarchar(max) = N'## Cuando le des sus autorizaciones, ponlas en TABLA

consultar_autorizaciones devuelve una fila por autorizacion. Ponlas asi:

| N.o | Fecha | Estado | Prestador | Por que | Carta |

- El NUMERO va PRIMERO y no se omite nunca. Es lo que le piden por telefono y lo
  que lleva al prestador. Se llena siempre.
- Estado con distintivo: <span class="md-marca ok">Autorizada</span> o
  <span class="md-marca mal">No autorizada</span>.
- **POR QUE es una columna, y NO se deja en blanco NUNCA.** Sale de
  PorQueNoSeAutorizo. Si viene vacio escribe
  <span class="md-nada">no consta</span>, no un hueco: cinco negativas seguidas
  sin una sola razon parece que te lo saltaste, y al afiliado le deja igual que
  antes de preguntar.
  El texto viene ya redactado —del catalogo de motivos o de la observacion de la
  autorizacion—: acortalo a lo que quepa, pero no lo inventes ni lo suavices. Si
  es largo, pon lo esencial en la celda y el texto entero debajo de la tabla.
- Si Prestador viene vacio ES que no consta -un tercio no lo trae-: pon un guion.
- Autorizada no lleva motivo: guion, no "no consta".
- Las 8 mas recientes como maximo, y di cuantas mas hay.

La CARTA: cada fila trae EnlaceDeLaCarta. En la columna Carta pon un enlace
markdown con ese valor EXACTO, sin tocar la ruta:

    [Ver la carta](/Studio/ChatCliente/Carta?id=NNN)

No ofrezcas "si quiere se la genero": el enlace ya esta, dalo. Se abre al lado de
la conversacion, sin salir del chat, y desde ahi puede bajarla. Si una fila no
trae EnlaceDeLaCarta, deja un guion: no inventes una ruta.

Y cuando NINGUNA de las negativas traiga motivo, dilo con todas las letras:
"en el sistema no consta el motivo de estas negativas; puede pedirlo a Servicio
al Cliente en el 6020920". Eso es cierto y le da una salida. Callarse, no.';

UPDATE dbo.Agent
   SET SystemPrompt =
         CASE
           WHEN CHARINDEX(@marca, SystemPrompt) > 0
                AND CHARINDEX(@fin, SystemPrompt) > CHARINDEX(@marca, SystemPrompt)
             THEN LEFT(SystemPrompt, CHARINDEX(@marca, SystemPrompt) - 1)
                  + @seccion + @salto
                  + SUBSTRING(SystemPrompt, CHARINDEX(@fin, SystemPrompt), LEN(SystemPrompt))
           WHEN CHARINDEX(@marca, SystemPrompt) > 0
             THEN LEFT(SystemPrompt, CHARINDEX(@marca, SystemPrompt) - 1) + @seccion
           ELSE SystemPrompt + @salto + @seccion
         END,
       ModifiedDate = SYSUTCDATETIME()
 WHERE Code = N'AGENTE_CHAT_CLIENTE' AND SystemPrompt IS NOT NULL;

SELECT Code,
       LEN(SystemPrompt) AS LargoDelPrompt,
       CASE WHEN CHARINDEX(N'POR QUE es una columna', SystemPrompt) > 0 THEN 'si' ELSE 'NO' END AS MotivoEnColumna,
       CASE WHEN CHARINDEX(N'## Los datos de contacto', SystemPrompt) > 0 THEN 'si' ELSE 'PERDIDA' END AS SiguenLasDemas
  FROM dbo.Agent
 WHERE Code = N'AGENTE_CHAT_CLIENTE';
