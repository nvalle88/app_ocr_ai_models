/* =============================================================================
   REQ-028b - El chat del afiliado
   -----------------------------------------------------------------------------
   Una ficha mas, al lado de Auditor y Cliente, donde el afiliado pregunta con
   sus palabras y se le contesta con SUS datos:

       "hasta cuando puedo presentar esta factura"
       "cuanto me falta del deducible"
       "en que va mi reembolso"
       "me cubren si me pasa algo de viaje"
       "por que me descontaron de los honorarios del medico"

   -- De donde salen las respuestas -------------------------------------------
   condiciones_del_plan ............ que dice el contrato, con sus palabras
   consultar_deducible_contrato .... cuanto le falta del deducible
   consultar_sobre_bd .............. en que va un sobre concreto
   historial_reembolsos_cliente_bd . sus reembolsos anteriores
   consultar_coberturas_plan ....... las coberturas de su plan
   codigo_liquidacion_y_cobertura .. que cubre el plan para una prestacion
   factura_ya_pagada_bd ............ si una factura ya entro

   -- Lo que NO se le da -----------------------------------------------------
   Nada de buscar por nombre ni por cedula ajena: buscar_sobres_cliente_bd y
   resolver_contrato_por_cedula quedan FUERA. En un chat de autoservicio, una
   herramienta que busca por nombre es una herramienta para leer los datos de
   otra persona, y basta con que el afiliado escriba un nombre para que el
   agente la llame. El contrato del que habla el chat es el de la pantalla, y se
   le pasa ya resuelto.

   Tampoco preexistencias por cedula: que el chat pueda recitar los diagnosticos
   de alguien a peticion de texto libre es un riesgo que no compensa. Si hace
   falta, esta el auditor.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @agente nvarchar(100) = N'AGENTE_CHAT_CLIENTE';
DECLARE @prompt nvarchar(max) = N'Eres quien atiende a un afiliado de Salud S.A. en un chat de autoservicio.
Le hablas a EL, no a un auditor: de usted, en frases cortas, sin jerga y sin codigos.

## Lo que tienes delante
Al principio del mensaje viene su contrato ya resuelto: plan, producto, region,
persona, deducible cubierto, carencias y si tiene preexistencias registradas. NO
lo vuelvas a consultar: ya lo tienes.

## Como respondes
- **Con SUS datos, o no respondes.** Si la pregunta necesita un dato que no
  tienes, usa la herramienta que lo trae. Si no hay herramienta para eso, dilo:
  "eso no lo puedo ver desde aqui" y di a quien preguntarle. Nunca inventes una
  cifra, una fecha ni un porcentaje.
- **Sin codigos.** Nunca escribas A003, Lr04, 504001 ni el nombre de una
  herramienta. Al afiliado no le dicen nada. Di "laboratorio clinico", no "A003".
- **Cita el contrato cuando responde la pregunta.** condiciones_del_plan te
  devuelve el texto tal como salio impreso: usalo, entre comillas si hace falta.
  Y fijate en el campo Alcance: una condicion GENERAL aplica a todos los planes,
  una PARTICULAR es de su plan. No las confundas.
- **Los porcentajes salen del plan, no de ti.** codigo_liquidacion_y_cobertura
  los lee de la tabla. Si te devuelve una alerta de ambiguedad o de filtro
  equivocado, NO cantes un numero: di que lo estan confirmando.
- **Lo que ya se pago, se dice sin nombrar a nadie.** Si una factura ya consta
  presentada, dilo, pero jamas digas en que contrato ni de quien: puede ser de
  otra persona.
- **Una respuesta corta y una salida.** Termina con lo que puede hacer: que
  documento le falta y quien lo firma, a que numero llamar, o que ya no tiene
  que hacer nada.

## Lo que NO haces
- No prometes importes. Lo que le devuelven lo fija la liquidacion; tu puedes
  decir que porcentaje cubre su plan, que es otra cosa y es verdad.
- No hablas de otra persona. Si pregunta por alguien que no es el ni su
  dependiente, dile que solo puedes ver su contrato.
- No das consejo medico. Explicas terminos, no recomiendas tratamientos.';

IF EXISTS (SELECT 1 FROM dbo.Agent WHERE Code = @agente)
    UPDATE dbo.Agent
       SET Name = N'Chat del afiliado', SystemPrompt = @prompt,
           ConfigCode = N'CLAUDE_FOUNDRY', MaxTokens = 4000, ThinkingMode = N'off',
           Description = N'Responde al afiliado con sus propios datos. Nada de busquedas por nombre.',
           IsActive = 1, ModifiedDate = SYSUTCDATETIME()
     WHERE Code = @agente;
ELSE
    INSERT dbo.Agent (Code, ConfigCode, Name, VersionNumber, Description, CreatedDate,
                      IsActive, SystemPrompt, MaxTokens, ThinkingMode)
    VALUES (@agente, N'CLAUDE_FOUNDRY', N'Chat del afiliado', 1,
            N'Responde al afiliado con sus propios datos. Nada de busquedas por nombre.',
            SYSUTCDATETIME(), 1, @prompt, 4000, N'off');

/* Las herramientas: solo las que hablan del contrato QUE YA ESTA en la pantalla. */
DECLARE @tools TABLE (Code nvarchar(100), Orden int);
INSERT @tools VALUES
    (N'condiciones_del_plan',            1),
    (N'consultar_deducible_contrato',    2),
    (N'consultar_sobre_bd',              3),
    (N'consultar_detalle_sobre_bd',      4),
    (N'historial_reembolsos_cliente_bd', 5),
    (N'consultar_coberturas_plan',       6),
    (N'codigo_liquidacion_y_cobertura',  7),
    (N'factura_ya_pagada_bd',            8);

INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT @agente, t.Code, t.Orden, 1
  FROM @tools t
 WHERE EXISTS (SELECT 1 FROM dbo.OPAITool o WHERE o.Code = t.Code AND o.IsActive = 1)
   AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt
                    WHERE mt.ModelCode = @agente AND mt.ToolCode = t.Code);

SELECT mt.ToolCode, mt.[Order], mt.IsEnabled
  FROM dbo.OPAIModelTool mt WHERE mt.ModelCode = @agente ORDER BY mt.[Order];
