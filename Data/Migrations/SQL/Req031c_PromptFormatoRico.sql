/* =============================================================================
   REQ-031c - El formato, tomando como guia el prompt del auditor
   -----------------------------------------------------------------------------
   Nestor paso el prompt de otro agente -uno de auditoria de documentos, que NO
   esta en esta instancia: los cinco agentes de aqui no lo tienen- como guia de
   como quiere el HTML. De ahi se toman tres cosas y se descarta una.

   Se toma: tablas de verdad, dos tablas relacionadas LADO A LADO, tablas
   compactas, e iconos con moderacion.

   Se descarta el style= en linea con display:flex del original. El filtro de
   etiquetas quita cualquier atributo que no sea una clase conocida, y eso no se
   toca: dejar que un modelo escriba CSS libre en lo que ve el afiliado es
   abrirle la maqueta entera. En su lugar tiene md-columnas / md-columna, que
   dan el mismo reparto sin poder inventarse nada.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @marca nvarchar(100) = N'## Dos tablas que se comparan';
DECLARE @salto nvarchar(10)  = CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10);

DECLARE @seccion nvarchar(max) = N'## Dos tablas que se comparan van LADO A LADO

Cuando dos tablas se leen juntas -el detalle y su resumen, los prestadores y sus
precios, lo cubierto y lo no cubierto- ponlas en paralelo. Una debajo de la otra
obliga a recordar la primera mientras se lee la segunda, que es justo lo que la
comparacion venia a evitar.

<div class="md-columnas">
  <div class="md-columna">
    <div class="md-h">Lo que cubre su plan</div>
    …tabla…
  </div>
  <div class="md-columna">
    <div class="md-h">Lo que pagaria usted</div>
    …tabla…
  </div>
</div>

Solo DOS por fila, y solo si de verdad se comparan. Tres tablas en paralelo no
caben y salen ilegibles. En un movil se apilan solas.

Y una regla que no cambia: **usa las CLASES, nunca style=**. El filtro quita
cualquier atributo que no sea una clase conocida, asi que un style= no llega a
verse. No es un capricho: lo que ve el afiliado no lo maqueta un modelo.

## Iconos: pocos y con oficio

Un icono delante de una fila ayuda a encontrarla. Una respuesta salpicada de
iconos parece un anuncio y le resta seriedad a lo que se le esta diciendo, que
es dinero y salud.

- Como mucho uno por seccion, y solo si aporta.
- Nunca en mitad de una frase.
- Nunca para adornar una cifra: la cifra ya va en negrita.
- El estado NO se dice con un icono suelto: para eso estan los distintivos
  -"En su red", "No autorizada"-, que llevan la palabra dentro y se entienden
  sin ver el color.';

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
       CASE WHEN CHARINDEX(N'md-columnas', SystemPrompt) > 0 THEN 'si' ELSE 'NO' END AS ConoceLasColumnas
  FROM dbo.Agent
 WHERE Code = N'AGENTE_CHAT_CLIENTE';
