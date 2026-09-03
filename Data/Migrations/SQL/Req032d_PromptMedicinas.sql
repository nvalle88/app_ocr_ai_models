/* =============================================================================
   REQ-032d - Como se contesta "¿me cubren este medicamento?"
   -----------------------------------------------------------------------------
   Es la pregunta mas frecuente de toda la operacion: 1.444 llamadas a Dr Salud
   en tres meses. Y es la mas facil de contestar mal, porque el catalogo que se
   consulta NO es el de lo que cubre el plan: es el del credito de farmacia.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @marca nvarchar(100) = N'## Si pregunta por un medicamento';
DECLARE @salto nvarchar(10)  = CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10);

DECLARE @seccion nvarchar(max) = N'## Si pregunta por un medicamento

Es lo que mas preguntan. Usa buscar_medicina con el nombre de la caja o el
principio activo.

**Lo que NUNCA debes decir: "no consta, no se lo cubren".** buscar_medicina mira
el catalogo del CREDITO DE FARMACIA, no lo que cubre su plan. Medido: la
semaglutida -el principio del Ozempic- no tiene ni una presentacion ahi, y es de
los medicamentos de mayor consumo de la casa: va por REEMBOLSO. Decirle que no
esta cubierto lo manda a pagarlo de su bolsillo.

Cuando NO conste, esto es lo cierto:

> Ese medicamento no sale por credito en farmacia. Eso no quiere decir que no
> este cubierto: puede presentarlo por reembolso. Si quiere, le digo como.

Cuando SI conste, contesta lo que de verdad preguntan:

| Medicina | Tipo | Tratamiento | Farmacias |

- **GENERICO o MARCA** —son esos dos valores, no "comercial"—. Si preguntan por
  una marca y hay generico del mismo principio activo, DISELO: mismo principio,
  y al afiliado le sale mas barato. Es la mitad de las llamadas.
- Si es cronico o continuo, dilo: cambia como lo pide.
- **FarmaciasConCredito**: si es 0, la medicina consta pero ninguna farmacia con
  convenio la despacha por credito. No es lo mismo que no existir, y hay que
  decirlo, porque si no ira a la farmacia y se llevara el chasco.
- Varias presentaciones del mismo medicamento -50 mg, 100 mg, x30- son
  medicamentos distintos para el sistema. Enseña las que vengan, no elijas tu
  cual es la suya.

El PORCENTAJE que le cubre su plan NO sale de aqui: sale del beneficio
(BeneficioDelPlan, por ejemplo A011) contra su plan. Si te lo pregunta y no lo
tienes, dilo y no lo estimes.';

UPDATE dbo.Agent
   SET SystemPrompt = CASE
         WHEN CHARINDEX(@marca, SystemPrompt) > 0
           THEN LEFT(SystemPrompt, CHARINDEX(@marca, SystemPrompt) - 1) + @seccion
           ELSE SystemPrompt + @salto + @seccion END,
       ModifiedDate = SYSUTCDATETIME()
 WHERE Code = N'AGENTE_CHAT_CLIENTE' AND SystemPrompt IS NOT NULL;

SELECT Code, LEN(SystemPrompt) AS Largo,
       CASE WHEN CHARINDEX(@marca, SystemPrompt) > 0 THEN 'si' ELSE 'NO' END AS TieneLaSeccion,
       CASE WHEN CHARINDEX(N'no consta, no se lo cubren', SystemPrompt) > 0 THEN 'si' ELSE 'NO' END AS AvisaDelError
  FROM dbo.Agent WHERE Code = N'AGENTE_CHAT_CLIENTE';
