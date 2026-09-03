/* =============================================================================
   REQ-032e - La regla del telefono, en TODOS los agentes
   -----------------------------------------------------------------------------
   El chat del afiliado se invento un telefono -"1700 PLAN PLAN (1700 752675)"-
   y la regla se puso solo en su prompt (Req030c/d). Los demas agentes tienen
   prompts propios y la misma puerta abierta: el auditor escribe informes que
   lee gente, y el clasificador redacta lo que se le enseña al cliente.

   Un agente que no tiene un dato y lo compone no es un caso raro: es lo que
   hacen cuando se les pide algo que no se les entrega. La regla va donde pueda
   pasar, no solo donde ya paso.

   Solo se toca a los agentes que TIENEN prompt y aun no la tienen, y se anade
   al final: no se reescribe lo que ya dice cada uno.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @marca nvarchar(100) = N'## Datos de contacto: solo los que tengas';
DECLARE @salto nvarchar(10)  = CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10);

DECLARE @seccion nvarchar(max) = N'## Datos de contacto: solo los que tengas

Un telefono, una direccion, un correo, una web, un horario o el nombre de una
oficina se escriben SOLO si vienen en los datos que te dieron. Si no vienen, no
se escriben: no los deduzcas, no los compongas y no los recuerdes de otro sitio.

Paso de verdad en este mismo sistema: un agente cerro una respuesta con "puede
llamar al 1700 PLAN PLAN (1700 752675)". Ese numero no estaba en ningun prompt
ni en ninguna respuesta de herramienta. Un telefono equivocado cuesta una llamada
perdida y la confianza en todo lo demas que dijiste.

El unico telefono que puedes dar sin que te lo pasen es el de Servicio al Cliente
de Salud S.A.: **6020920**. Cualquier otro, solo si viene en los datos.';

UPDATE dbo.Agent
   SET SystemPrompt = SystemPrompt + @salto + @seccion,
       ModifiedDate = SYSUTCDATETIME()
 WHERE SystemPrompt IS NOT NULL
   AND LEN(SystemPrompt) > 400                      -- los de una linea no son prompts de verdad
   AND Code <> N'AGENTE_CHAT_CLIENTE'               -- ya la tiene, mas completa
   AND CHARINDEX(@marca, SystemPrompt) = 0
   AND CHARINDEX(N'6020920', SystemPrompt) = 0;

SELECT Code,
       LEN(SystemPrompt) AS Largo,
       CASE WHEN CHARINDEX(N'6020920', SystemPrompt) > 0 THEN 'si' ELSE 'NO' END AS TieneLaRegla
  FROM dbo.Agent
 WHERE SystemPrompt IS NOT NULL
 ORDER BY Code;
