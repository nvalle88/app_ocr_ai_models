/* =============================================================================
   REQ-041b - Dos formas nuevas para contar un tramite
   -----------------------------------------------------------------------------
   Nestor: "cada tool debe responder segun lo que retorna y ser super
   profesional, es informacion al cliente, con colores html profesionales, que
   sea intuitivo y util".

   El problema no era el color: era que TODO salia en tabla. Una tabla de cinco
   filas diciendo "Ingresado" no contesta "por que se demora", y tres cifras
   enterradas en columnas no dejan ver lo unico que importa -la diferencia entre
   lo que presento y lo que le reconocen-.

   Se anaden dos formas al vocabulario, con su CSS ya puesto:

     md-linea   la historia del tramite, con el punto donde SE PARO marcado
     md-cifras  tres numeros grandes, y el que duele pintado segun lo que es

   No hacia falta tocar el renderizador: ul, li, div y span ya estaban
   permitidos y las clases md-* tambien. Lo que faltaba era decirle al modelo
   que existen y CUANDO usarlas.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @ancla nvarchar(100) = N'## Una sola pasada';
DECLARE @seccion nvarchar(max) = N'## Dos formas para contar un tramite

### La historia: donde se paro

Cuando pregunten **en que va** algo -un sobre, una autorizacion-, la respuesta no
es una tabla: es una linea de tiempo, y lo que se busca con el ojo es **donde se
detuvo**. Marca ese paso con `md-aqui` y ninguno mas:

```
<ul class="md-linea">
  <li><span class="md-cuando">26/08</span> Recibido</li>
  <li><span class="md-cuando">27/08</span> Documentos validados</li>
  <li class="md-aqui"><span class="md-cuando">28/08</span> En revision medica</li>
</ul>
```

Debajo, en una linea: que falta o de quien se espera. Si lleva mucho parado,
dilo con el numero -*"ocho dias en este punto"*-: es lo que de verdad
preguntaron.

### Las cifras: lo que presento y lo que le reconocen

Cuando haya dinero de por medio, tres numeros y **la diferencia explicada**. La
diferencia es lo que la persona quiere entender, asi que va con su tono: `ok` si
le reconocen todo, `aviso` si hay algo a su cargo, `mal` si le negaron lo
principal.

```
<div class="md-cifras">
  <div><span class="md-rotulo">Presento</span><span class="md-valor">US$ 382,00</span></div>
  <div class="ok"><span class="md-rotulo">Le reconocen</span><span class="md-valor">US$ 300,00</span></div>
  <div class="aviso"><span class="md-rotulo">A su cargo</span><span class="md-valor">US$ 82,00</span>
    <span class="md-detalle">Deducible pendiente del ano</span></div>
</div>
```

**Siempre el motivo en `md-detalle`.** Un numero en rojo sin explicacion asusta y
no informa; con el motivo, la persona sabe si puede hacer algo.

### Cuando NO usarlas

- **Una lista de cosas comparables** -sus reembolsos, farmacias cerca, sus
  autorizaciones- va en TABLA. La linea de tiempo es para UN tramite.
- **Un solo dato** no necesita tarjeta: se dice en la frase y ya.
- Nunca las dos a la vez por rutina. Primero contesta; la forma se elige por lo
  que hay que contar, no por adornar.

';

UPDATE dbo.Agent
   SET SystemPrompt = REPLACE(CONVERT(nvarchar(max), SystemPrompt), @ancla, @seccion + @ancla)
 WHERE Code = N'AGENTE_CHAT_CLIENTE'
   AND CONVERT(nvarchar(max), SystemPrompt) LIKE N'%' + @ancla + N'%'
   AND CONVERT(nvarchar(max), SystemPrompt) NOT LIKE N'%md-linea%';

SELECT Code,
       Tiene = CASE WHEN CONVERT(nvarchar(max), SystemPrompt) LIKE N'%md-linea%'
                    THEN 'SI' ELSE 'NO' END,
       Largo = LEN(CONVERT(nvarchar(max), SystemPrompt))
  FROM dbo.Agent WHERE Code = N'AGENTE_CHAT_CLIENTE';
