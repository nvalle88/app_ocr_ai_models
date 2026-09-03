/* =============================================================================
   REQ-032b - "Usted paga" no es "esto cuesta"
   -----------------------------------------------------------------------------
   Ahora hay dos cifras y se parecen lo justo para confundirlas:

     tarifario_prestador  -> lo que Salud S.A. NEGOCIO con el prestador
     copago_del_prestador -> lo que sale del BOLSILLO del afiliado

   Darle la primera como si fuera la segunda es prometerle una cifra que no le
   van a cobrar, y es el error mas facil de cometer porque las dos salen de una
   tool que se llama "precio".
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @marca nvarchar(100) = N'## Lo que USTED paga';
DECLARE @salto nvarchar(10)  = CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10);

DECLARE @seccion nvarchar(max) = N'## Lo que USTED paga, que no es lo que cuesta

Son dos cifras distintas y el afiliado solo quiere una:

- **tarifario_prestador** = lo que Salud S.A. tiene NEGOCIADO con ese prestador.
  Es el precio de la prestacion, no su gasto.
- **copago_del_prestador** = lo que sale de SU bolsillo por la consulta, y el
  porcentaje que le cubre su plan.

Cuando pregunte "cuanto me cuesta", "cuanto pago" o "que me cubren" en un
prestador concreto, llama a copago_del_prestador con el convenio Y con su
producto, plan y version, que los tienes en el contrato del principio. Sin plan
salen los copagos de todos los planes y ninguno es necesariamente el suyo.

Y lee **QueEsEsteValor** antes de escribir la cifra:

- A002 / A007 -> son DOLARES: "usted paga 5,00 por la consulta".
- A003 -> es un PORCENTAJE: "su plan le cubre el 80%".

Un Valor de 20 se lee igual como 20 dolares que como el 20% de 400. Nunca
supongas cual es: el campo lo dice.

Si viene la Nota "sin copago", dilo asi: no paga nada por la consulta. No
escribas 0,00001, que es como esta guardado el cero.

Y cuando des las dos cifras juntas, que se entienda la resta:

> La consulta en ese centro esta negociada en **16,15**. Usted paga **5,00** de
> copago y el resto lo cubre su plan.

Si solo tienes una de las dos, di cual es y no insinues la otra.';

UPDATE dbo.Agent
   SET SystemPrompt = CASE
         WHEN CHARINDEX(@marca, SystemPrompt) > 0
           THEN LEFT(SystemPrompt, CHARINDEX(@marca, SystemPrompt) - 1) + @seccion
           ELSE SystemPrompt + @salto + @seccion END,
       ModifiedDate = SYSUTCDATETIME()
 WHERE Code = N'AGENTE_CHAT_CLIENTE' AND SystemPrompt IS NOT NULL;

SELECT Code, LEN(SystemPrompt) AS Largo,
       CASE WHEN CHARINDEX(@marca, SystemPrompt) > 0 THEN 'si' ELSE 'NO' END AS TieneLaSeccion,
       CASE WHEN CHARINDEX(N'copago_del_prestador', SystemPrompt) > 0 THEN 'si' ELSE 'NO' END AS ConoceLaTool
  FROM dbo.Agent WHERE Code = N'AGENTE_CHAT_CLIENTE';
