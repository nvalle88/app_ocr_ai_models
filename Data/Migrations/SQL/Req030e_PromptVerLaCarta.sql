/* =============================================================================
   REQ-030e - La carta se VE, no se baja
   -----------------------------------------------------------------------------
   El enlace decia 'Descargar' porque el endpoint devolvia el PDF como adjunto.
   Ahora sale inline y se abre en un visor al lado de la conversacion: el
   afiliado la mira, la contrasta con lo que acaba de leer y sigue escribiendo,
   en vez de irse a su carpeta de descargas y tener que rehacer el camino para
   volver a preguntar. Descargarla sigue estando, en el propio visor.

   La etiqueta tiene que decir lo que pasa al pulsar. 'Descargar' prometia un
   fichero en la carpeta, y lo que ocurre es otra cosa.

   Se cambian dos frases SUELTAS, cada una dentro de su linea: un literal de
   varias lineas tendria que coincidir tambien en los saltos -LF o CRLF-, y un
   REPLACE que no encuentra nada no falla, se queda callado.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

UPDATE dbo.Agent
   SET SystemPrompt = REPLACE(SystemPrompt,
                              N'[Descargar](/Studio/ChatCliente/Carta?id=NNN)',
                              N'[Ver la carta](/Studio/ChatCliente/Carta?id=NNN)'),
       ModifiedDate = SYSUTCDATETIME()
 WHERE Code = N'AGENTE_CHAT_CLIENTE' AND SystemPrompt IS NOT NULL;

UPDATE dbo.Agent
   SET SystemPrompt = REPLACE(SystemPrompt,
                              N'el enlace ya esta, dalo.',
                              N'el enlace ya esta, dalo. Se abre al lado de la conversacion, sin salir del chat, y desde ahi puede bajarla si la necesita.'),
       ModifiedDate = SYSUTCDATETIME()
 WHERE Code = N'AGENTE_CHAT_CLIENTE'
   AND SystemPrompt IS NOT NULL
   AND CHARINDEX(N'Se abre al lado de la conversacion', SystemPrompt) = 0;

SELECT Code,
       LEN(SystemPrompt) AS LargoDelPrompt,
       CASE WHEN CHARINDEX(N'[Ver la carta]', SystemPrompt) > 0 THEN 'si' ELSE 'NO' END AS DiceVer,
       CASE WHEN CHARINDEX(N'[Descargar](/Studio', SystemPrompt) > 0
            THEN 'AUN DICE DESCARGAR' ELSE 'no' END                                     AS EtiquetaVieja,
       CASE WHEN CHARINDEX(N'Se abre al lado', SystemPrompt) > 0 THEN 'si' ELSE 'NO' END AS ExplicaElVisor
  FROM dbo.Agent
 WHERE Code = N'AGENTE_CHAT_CLIENTE';
