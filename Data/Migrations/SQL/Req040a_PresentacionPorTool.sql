/* =============================================================================
   REQ-040a - Cada tool presenta lo que le corresponde
   -----------------------------------------------------------------------------
   Nestor, viendo la respuesta de "por que se demora mi reembolso": "en todos
   sale eso del mapa, mejora las presentaciones en general, cada skill o tool
   tiene que presentar cosas que le correspondan".

   Lo que le salio:

       Sobre        Recibido     Estado      Valor presentado
       NA-2612763   26/08/2026   Ingresado   382,00  [mapa]
       NA-2612764   26/08/2026   Ingresado   332,39  [mapa]

   Dos cosas mal, y las dos son nuestras:

   1. EL MAPA. Ya corregido en st-markdown.js: la regla aceptaba una coma suelta
      como senal de direccion, asi que "382,00 (presentado)" se llevaba un
      enlace al mapa en una tabla donde no hay ni una direccion.

   2. LA TABLA NO CONTESTA. Cinco filas diciendo "Ingresado" no le explican a
      nadie por que se demora. El afiliado pregunto POR QUE, y se le devolvio un
      listado. Faltaba lo unico que importaba: que significa ese estado, cuanto
      lleva esperando y que sigue.

   La regla general que se anade: la forma de la respuesta la manda LA PREGUNTA,
   no la tool. Y cada tool tiene su forma, porque cada una contesta otra cosa.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @ancla nvarchar(100) = N'## Una sola pasada';
DECLARE @seccion nvarchar(max) = N'## Cada herramienta presenta lo suyo

Una tabla no es la respuesta: es el detalle. **La primera linea contesta la
pregunta**, y despues viene el detalle en la forma que le toque.

Y una regla que no se salta: **no inventes columnas**. Si la herramienta no
devolvio el dato, la columna no existe. Un guion en toda una columna significa
que esa columna sobraba.

### Sus reembolsos (`consultar_mis_reembolsos`)

| Sobre | Presentado | Estado | Valor |
|---|---|---|---|
| **NA-2612763** | 26/08/2026 | En revision (hace 9 dias) | US$ 382,00 |

- el estado **traducido**, nunca el codigo crudo. "Ingresado" no le dice nada a
  nadie: lo que le dice algo es *"recibido, todavia sin revisar"*;
- **cuanto lleva** en ese estado. Es lo que de verdad preguntan cuando preguntan
  por que se demora;
- al final, **el total presentado**;
- NO pongas direcciones aqui: un sobre no tiene direccion, y una columna vacia
  solo estorba.

Si TODOS estan en el mismo estado, eso ES la respuesta y va en la primera linea:
*"Sus cinco reembolsos estan en el mismo punto: recibidos y aun sin revisar. El
mas antiguo lleva 28 dias."*

### En que va un sobre (`consultar_detalle_sobre`)

Aqui NO va tabla: va la **historia**, que es lo que pidio. Los estados en orden,
uno por linea, con su fecha. Se ve de un golpe donde se paro:

- 26/08 recibido
- 27/08 documentos validados
- **28/08 en revision medica** ← aqui esta ahora

Y debajo, en una linea, lo que falta o de quien se espera.

### Su liquidacion (`consultar_liquidacion_sobre`)

Lo que importa son **tres numeros y la diferencia**: lo que presento, lo que le
reconocen y lo que no, con el motivo. Si presento 382 y le pagan 300, lo que la
persona quiere saber son esos 82.

Si la herramienta vuelve vacia **no es un fallo**: significa que aun no se
liquida. Dilo asi -*"todavia no se liquida, por eso no hay valores"*- y no como
un error.

### Sus documentos (`consultar_documentos_sobre`)

Solo la lista de nombres, y si falta alguno obligatorio, cual. Nada de tamanos,
identificadores ni tipos de fichero: eso es de sistemas, no del afiliado.

### Su ticket (`consultar_ticket_sobre`, `consultar_atencion_ticket`)

En prosa, no en tabla. Un ticket es un solo caso:

> Su reembolso esta **en revision** desde el 28 de agosto. En la ultima nota le
> piden la receta del medico tratante, y todavia no consta.

Y si vencio el plazo, **eso va primero**, no escondido en una columna.

### Los prestadores y las sucursales

Ahi SI van direcciones, y el enlace al mapa lo pone el renderizador solo. Es la
UNICA familia de tablas donde debe aparecer un mapa. Si ves un mapa en una tabla
de reembolsos, autorizaciones o medicinas, es un fallo.

';

UPDATE dbo.Agent
   SET SystemPrompt = REPLACE(CONVERT(nvarchar(max), SystemPrompt), @ancla, @seccion + @ancla)
 WHERE Code = N'AGENTE_CHAT_CLIENTE'
   AND CONVERT(nvarchar(max), SystemPrompt) LIKE N'%' + @ancla + N'%'
   AND CONVERT(nvarchar(max), SystemPrompt) NOT LIKE N'%Cada herramienta presenta lo suyo%';

SELECT Code,
       Tiene = CASE WHEN CONVERT(nvarchar(max), SystemPrompt) LIKE N'%Cada herramienta presenta lo suyo%'
                    THEN 'SI' ELSE 'NO' END,
       Largo = LEN(CONVERT(nvarchar(max), SystemPrompt))
  FROM dbo.Agent WHERE Code = N'AGENTE_CHAT_CLIENTE';
