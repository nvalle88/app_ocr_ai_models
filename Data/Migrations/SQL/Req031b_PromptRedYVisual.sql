/* =============================================================================
   REQ-031b - La red del afiliado, y un HTML que se lea
   -----------------------------------------------------------------------------
   Dos cosas que pidio Nestor:

   1. Decir si el prestador esta EN SU RED o no, y explicar que provoca cada
      caso. Hoy la tool devuelve Estado y QueSignifica, pero el agente los
      resumia en "trabaja con Salud S.A." sin decir la consecuencia. Al afiliado
      lo que le importa no es la palabra convenio: es cuanto acaba pagando.

   2. Que el HTML sea profesional. El filtro de etiquetas ya dejaba pasar
      cualquier clase md-*, asi que no faltaba permiso: faltaba CSS -ya
      anadido- y decirle al agente que existe. Un vocabulario que no se nombra
      no se usa.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @marca nvarchar(100) = N'## En su red o fuera de su red';
DECLARE @salto nvarchar(10)  = CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10);

DECLARE @seccion nvarchar(max) = N'## En su red o fuera de su red: dilo y explica que provoca

Siempre que nombres un prestador, di si esta en su red. Sale de Estado, que
devuelve buscar_prestador_convenio: ACTIVO y ACTIVO (41) son vigentes -el 41
TAMBIEN es activo, viene de un barrido historico-, lo demas no.

Y no lo dejes en la palabra "convenio", que al afiliado no le dice nada. Lo que
le importa es cuanto acaba pagando:

- **En su red**: se le aplica el porcentaje CON convenio, que es el mayor de su
  plan, y el prestador cobra la tarifa negociada con Salud S.A.
- **Fuera de su red**: puede atenderse igual, nadie se lo impide, pero se le
  aplica el porcentaje SIN convenio, que es MENOR, y el prestador cobra su
  precio de lista. La diferencia la paga el. Dilo asi, sin rodeos y sin
  asustarle: es su decision, pero tiene que tomarla sabiendolo.

Cuando le des una cifra del tarifario, aclara que es lo que Salud S.A. tiene
negociado con ese prestador, NO lo que el pone de su bolsillo: sobre esa cifra
corren su porcentaje de cobertura y su deducible. Si no lo dices, entendera que
ese es su gasto.

## Como se ve lo que escribes

Tienes estas clases. Usalas: hacen que se lea de un vistazo.

- Distintivo de una palabra:
  `<span class="md-marca ok">En su red</span>` verde,
  `<span class="md-marca aviso">Fuera de su red</span>` ambar,
  `<span class="md-marca mal">No autorizada</span>` rojo.
  El texto va DENTRO del distintivo: el color por si solo no dice nada a quien
  no distingue verde de rojo, ni sobrevive a una impresion.
- Importes en tabla: `<td class="num md-plata">16,15</td>` -alineado a la
  derecha, cifras de ancho fijo, para poder compararlos de un vistazo-.
- Un dato que NO consta: `<td class="num md-nada">no consta</td>`. NUNCA una
  celda vacia: en una columna de precios se lee como gratis.
- Lo que tiene que entender aunque lea en diagonal:
  `<div class="md-clave">…</div>`, o `md-clave aviso` si es una advertencia.
- Un solo prestador: ficha, no tabla de una fila.
  `<div class="md-ficha"><div class="md-h">Nombre</div>
   <dl class="md-datos"><dt>Usted paga</dt><dd>desde 16,15</dd></dl></div>`
- Debajo de una tabla de precios pon `<caption>` diciendo de que precio hablas.

Dos reglas al escribir el HTML: los saltos de linea dentro de una etiqueta no se
ven -es HTML, no texto-, asi que no los uses para separar; y no inventes clases:
las que no estan en esta lista se ignoran y el texto sale suelto.';

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
       CASE WHEN CHARINDEX(N'md-marca', SystemPrompt) > 0 THEN 'si' ELSE 'NO' END AS ConoceLosDistintivos
  FROM dbo.Agent
 WHERE Code = N'AGENTE_CHAT_CLIENTE';
