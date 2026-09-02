/* =============================================================================
   REQ-031a - En la tabla de prestadores: QUE HACEN y CUANTO CUESTA
   -----------------------------------------------------------------------------
   Nestor: "estoy buscando donde hacerme unos rayos x" -> el chat contesto con
   una tabla de prestador, direccion y telefono. Le falta lo que de verdad
   decide: que hace ese centro y cuanto le va a costar.

   -- Por que no salia -------------------------------------------------------
   No es que el modelo se olvidara: el prompt no se lo pedia, y las dos fuentes
   estan en SERVIDORES DISTINTOS -convenios en Salud @ SQLMIGRACION, tarifario en
   Saludsa- asi que no hay JOIN posible y ninguna tool puede traer las dos cosas.
   Las une el modelo, por NumeroConvenio, que las dos devuelven.

   -- Lo medido, que es lo que obliga a ser honesto -------------------------
   Solo 213 convenios tienen tarifario, y 90 tienen rayos X. De los CINCO centros
   que el chat le enseño a la afiliada, solo DOS -Axxiscan y Medimagenes- tienen
   precio de rayos X cargado. Si se pone una columna de precio sin mas, tres de
   cinco filas salen vacias y el afiliado cree que es gratis o que fallo algo.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @marca nvarchar(100) = N'## Donde hacerme un examen';
DECLARE @salto nvarchar(10)  = CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10);

DECLARE @seccion nvarchar(max) = N'## Donde hacerme un examen: QUE HACEN y CUANTO CUESTA

Cuando pregunte donde hacerse algo -unos rayos X, un laboratorio, una ecografia,
una resonancia- no basta con darle nombres y telefonos. Lo que decide es si ese
centro LO HACE y cuanto le va a costar. Son DOS tools y las pegas tu:

1. tarifario_prestador con el servicio: rx rayos X, lc laboratorio, ed ecografia,
   rm resonancia, t tomografia (TAC), ma mamografia, te terapias, odo odontologia.
   Te da NumeroConvenio, el Servicio con su nombre real y PrecioVigente.
2. buscar_prestador_convenio para esos convenios: nombre, direccion y telefono.

Se pegan por NumeroConvenio, que las dos devuelven. Es la unica llave: el
tarifario NO trae el nombre del prestador, y las dos bases estan en servidores
distintos, asi que ninguna consulta puede traerlo todo junto.

La tabla queda:

| Prestador | Que hace | Precio | Donde | Telefono |

Y ahora lo importante, porque aqui es facil mentir sin querer:

- Solo 213 convenios tienen tarifario cargado, y 90 tienen rayos X. LA MAYORIA
  DE PRESTADORES NO TIENE PRECIO. Si una fila no lo trae, escribe "no consta" en
  esa celda. Nunca la dejes en blanco: una celda vacia en una columna de precios
  se lee como gratis o como que algo fallo.
- Primero los que SI tienen precio, y despues los demas. Al afiliado le sirve mas
  un centro donde sabe lo que va a pagar.
- PrecioVigente es el negociado. PrecioDeLista es otra cosa y casi siempre es mas
  alto. Nunca des el de lista.
- Si no pediste tipoTarifa, lo que sale es la tarifa mas barata vigente: entonces
  di "desde X", no "cuesta X".
- El precio del tarifario es lo que Salud S.A. tiene negociado con el prestador,
  NO lo que el afiliado paga de su bolsillo: sobre eso corre su porcentaje de
  cobertura y su deducible. Dilo cuando des una cifra, o entendera que ese es su
  gasto.

Y "que hace" sale del campo Servicio, del catalogo, no del nombre de la
prestacion. Buscar "rayos x" por texto encuentra 65 convenios y por servicio son
90: hay centros que hacen rayos X y a sus prestaciones las llaman
"ANTEBRAZO AP-L" o "PIE 3 POSC. AP, L Y OBLICUA".';

UPDATE dbo.Agent
   SET SystemPrompt = CASE
         WHEN CHARINDEX(@marca, SystemPrompt) > 0
           THEN LEFT(SystemPrompt, CHARINDEX(@marca, SystemPrompt) - 1) + @seccion
           ELSE SystemPrompt + @salto + @seccion END,
       ModifiedDate = SYSUTCDATETIME()
 WHERE Code = N'AGENTE_CHAT_CLIENTE' AND SystemPrompt IS NOT NULL;

SELECT Code,
       LEN(SystemPrompt) AS LargoDelPrompt,
       CASE WHEN CHARINDEX(@marca, SystemPrompt) > 0 THEN 'si' ELSE 'NO' END AS TieneLaSeccion,
       CASE WHEN CHARINDEX(N'no consta', SystemPrompt) > 0 THEN 'si' ELSE 'NO' END AS DiceQueHacerSinPrecio
  FROM dbo.Agent
 WHERE Code = N'AGENTE_CHAT_CLIENTE';
