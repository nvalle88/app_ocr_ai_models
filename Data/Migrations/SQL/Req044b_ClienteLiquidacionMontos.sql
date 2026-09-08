/* =============================================================================
   REQ-044b - El cliente usa la gemela SQL para los montos de la liquidacion
   -----------------------------------------------------------------------------
   consultar_liquidacion_sobre (gateway) es el primario del cliente y solo cae al
   SQL si FALLA. Pero para NA-2735345 volvio SIN los montos y no como error, asi
   que la gemela no se disparaba y el afiliado se quedaba sin el desglose.

   Como los montos viven en el RECLAMO (Req044a arreglo la gemela para leer
   Lr02Reclamos), aqui se le habilita esa gemela AL CLIENTE y se le dice en el
   prompt que de ahi salen los valores. No se invierte el primario -el servicio
   sigue mandando para el estado/detalle- pero el modelo ya tiene con que sacar
   el dinero cuando el servicio no lo trae.
   ============================================================================= */

IF DB_NAME() NOT IN ('db-nexus-test', 'db-nexus-aud')
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Migracion para db-nexus-test o db-nexus-aud. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

/* 1) La gemela SQL, habilitada para el chat del cliente. */
INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT N'AGENTE_CHAT_CLIENTE', N'consultar_liquidacion_sobre_bd',
       ISNULL((SELECT MAX(mt.[Order]) FROM dbo.OPAIModelTool mt WHERE mt.ModelCode = N'AGENTE_CHAT_CLIENTE'), 0) + 1, 1
 WHERE NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt
                    WHERE mt.ModelCode = N'AGENTE_CHAT_CLIENTE' AND mt.ToolCode = N'consultar_liquidacion_sobre_bd');

/* 2) El prompt dice de donde salen los montos. */
DECLARE @ancla nvarchar(100) = N'## Una sola pasada';
DECLARE @seccion nvarchar(max) = N'## Los montos de la liquidacion salen del RECLAMO

Cuando el afiliado pregunte por su liquidacion -cuanto le reconocieron, cuanto le
pagaron, cuanto quedo a su cargo-, esos montos **NO estan en el sobre de entrada**.
El sobre solo guarda lo PRESENTADO. La liquidacion de verdad se hace en el
**reclamo**, y la trae `consultar_liquidacion_sobre_bd` (Lr02Reclamos por numero
de sobre): Presento, LeReconocen (cubierto), LePagaron, Deducible, Copago y
ACargoDelAfiliado (no cubierto).

`consultar_liquidacion_sobre` -la del servicio- sirve para el estado y el detalle
del sobre, pero **puede volver sin los montos**. Si te pasa eso -o si te piden el
desglose de valores- usa `consultar_liquidacion_sobre_bd`, que es la fuente del
dinero. Preséntalo con `md-cifras`: presentado, reconocido y a su cargo, con el
motivo si algo no se cubrio.

';

UPDATE dbo.Agent
   SET SystemPrompt = REPLACE(CONVERT(nvarchar(max), SystemPrompt), @ancla, @seccion + @ancla)
 WHERE Code = N'AGENTE_CHAT_CLIENTE'
   AND CONVERT(nvarchar(max), SystemPrompt) LIKE N'%' + @ancla + N'%'
   AND CONVERT(nvarchar(max), SystemPrompt) NOT LIKE N'%salen del RECLAMO%';

SELECT ToolHabilitada = (SELECT COUNT(*) FROM dbo.OPAIModelTool
                          WHERE ModelCode = N'AGENTE_CHAT_CLIENTE' AND ToolCode = N'consultar_liquidacion_sobre_bd' AND IsEnabled = 1),
       PromptPuesto = (SELECT CASE WHEN CONVERT(nvarchar(max), SystemPrompt) LIKE N'%salen del RECLAMO%' THEN 1 ELSE 0 END
                         FROM dbo.Agent WHERE Code = N'AGENTE_CHAT_CLIENTE');
