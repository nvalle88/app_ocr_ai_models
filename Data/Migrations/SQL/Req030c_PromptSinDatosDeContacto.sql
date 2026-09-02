/* =============================================================================
   REQ-030c - El chat dio un telefono que no tenia
   -----------------------------------------------------------------------------
   A la pregunta 'puedes buscar mis autorizaciones' el chat cerro con:

       'puede llamar al 1700 PLAN PLAN (1700 752675)'

   Ese numero no sale de ningun sitio. Comprobado:
     - ningun prompt de ningun agente contiene un telefono,
     - 0 de los motivos del catalogo MotivoNoCubierto mencionan 752675 ni 1700,
     - ninguno de los seis motivos mas usados -1.370.959, 602.186, 166.444,
       128.208, 38.453 y 34.882 usos- trae telefono en su texto,
     - en salud37 NINGUN motivo dice 'llamar' ni 'comunicarse'.

   Puede que el numero exista. Da igual: no lo leyo, lo compuso. Y lo que hoy
   sale bien por casualidad mañana sale mal.

   -- La causa: el propio prompt se lo pedia ---------------------------------
   La linea de cierre decia:

       'Termina con lo que puede hacer: que documento le falta y quien lo firma,
        A QUE NUMERO LLAMAR, o que ya no tiene que hacer nada.'

   Le exigia terminar diciendo a que numero llamar y no le daba ninguno. Y la
   regla de no inventar era enumerativa -'nunca inventes una cifra, una fecha ni
   un porcentaje'-, y un telefono no es ninguna de las tres: se colaba por el
   hueco de la lista. Pedir un dato que no se entrega es pedir que se invente.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

/* Se cambia SOLO ese trozo de linea, no el parrafo entero: un literal de varias
   lineas tendria que coincidir tambien en los saltos -LF o CRLF- y un REPLACE
   que no encuentra nada no falla, se queda callado. Una frase dentro de una
   linea coincide igual con cualquiera de los dos. */
DECLARE @viejo nvarchar(100) = N'a que numero llamar';
DECLARE @nuevo nvarchar(200) = N'a quien tiene que dirigirse -un telefono SOLO si lo trae una herramienta-';

DECLARE @marca nvarchar(100) = N'## NUNCA des un telefono';
DECLARE @seccion nvarchar(max) = N'

## NUNCA des un telefono, una direccion ni un correo que no tengas

Un telefono, una direccion, un correo, una web, un horario o el nombre de una
oficina se dan SOLO si vienen escritos en lo que devolvio una herramienta. Si no
vienen, no se dan. No los deduzcas, no los compongas, no los recuerdes de otro
sitio: aqui solo existe lo que esta en los datos de esta conversacion.

Paso de verdad: el chat cerro una respuesta con "puede llamar al 1700 PLAN PLAN
(1700 752675)". Ese numero no estaba en ninguna respuesta de ninguna herramienta.
A un afiliado un telefono equivocado le cuesta una llamada perdida, y la
siguiente vez ya no cree lo demas que le digas.

Si hace falta que llame y NO tienes el numero, esto es cierto y no inventa nada:

  "eso lo confirma Servicio al Cliente. El numero lo tiene en su carnet y en la
   pagina de Salud S.A."

Lo mismo con el telefono o la direccion de un prestador: buscar_prestador_convenio
devuelve Telefono y Direccion. Si vienen vacios, di que no constan. No pongas
otros.';

-- 1) La linea que le pedia un telefono que no tenia.
UPDATE dbo.Agent
   SET SystemPrompt = REPLACE(SystemPrompt, @viejo, @nuevo),
       ModifiedDate = SYSUTCDATETIME()
 WHERE Code = N'AGENTE_CHAT_CLIENTE'
   AND SystemPrompt IS NOT NULL
   AND CHARINDEX(@viejo, SystemPrompt) > 0;

-- 2) La regla, una sola vez.
UPDATE dbo.Agent
   SET SystemPrompt = SystemPrompt + @seccion,
       ModifiedDate = SYSUTCDATETIME()
 WHERE Code = N'AGENTE_CHAT_CLIENTE'
   AND SystemPrompt IS NOT NULL
   AND CHARINDEX(@marca, SystemPrompt) = 0;

SELECT Code,
       LEN(SystemPrompt) AS LargoDelPrompt,
       CASE WHEN CHARINDEX(@marca, SystemPrompt) > 0 THEN 'si' ELSE 'NO' END AS TieneLaRegla,
       CASE WHEN CHARINDEX(N'a que numero llamar', SystemPrompt) > 0
            THEN 'AUN LA PIDE' ELSE 'corregida' END AS LineaDeSalida
  FROM dbo.Agent
 WHERE Code = N'AGENTE_CHAT_CLIENTE';
