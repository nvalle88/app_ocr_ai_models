/* =============================================================================
   REQ-033b - El prompt aprende a usar buscar_sucursales_cerca
   -----------------------------------------------------------------------------
   La tool sola no basta: si el prompt no dice cuando usarla, el agente sigue
   tirando de buscar_prestador_convenio, que devuelve un prestador con todas sus
   sucursales pegadas en un texto cuando lo que se pregunto fue "cual me queda
   cerca".

   Tres cosas que esta seccion evita, y las tres estan medidas:

   1. HORARIOS. HorarioAtencion esta lleno en 49 de 5.418 sucursales -0,9%-. Un
      modelo al que no se le prohibe expresamente acaba escribiendo "abierto
      hasta las 20h" porque suena razonable. Se le prohibe.

   2. DECIR "CERCA" CUANDO NO LO SABE. La consulta devuelve ComoSeBusco. Si el
      sitio no se ubico, la lista es de TODA la ciudad; presentarla como cercana
      manda al afiliado a cruzar Quito. La seccion le obliga a leer esa columna
      antes de escribir la primera linea.

   3. ESCRIBIR ENLACES DE MAPA. El enlace lo pone el renderizador -st-markdown-
      al detectar que la celda es una direccion, precisamente para que el modelo
      no controle a donde apunta. Si ademas lo escribe el, salen dos.

   El texto se inserta ANTES de "## Una sola pasada", junto a la seccion de la
   tabla de prestadores, que es su vecina natural.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @ancla nvarchar(100) = N'## Una sola pasada';
DECLARE @seccion nvarchar(max) = N'## "Donde hay una farmacia por la Carolina": usa `buscar_sucursales_cerca`

Cuando pregunte por un sitio -una farmacia, un laboratorio, donde hacerse unos
rayos X, un centro medico- y mencione una ZONA, esa es la herramienta. No la de
prestadores: aquella devuelve un prestador con todas sus sucursales pegadas en
un texto, y lo que el afiliado quiere es **la de al lado de su casa**.

Mandale `ciudad` siempre, `cerca` con la referencia tal como la dijo el -"la
Carolina", "Kennedy", "el Bosque", "el Mall del Sol"- y `tipo` traducido:
"donde compro la medicina" es `farmacia`, "unos rayos X" es `imagen`.

La tabla, ordenada por distancia y con la mas cercana arriba:

| Farmacia | Direccion | A que distancia | Telefono |
|---|---|---|---|
| **Fybeca Jardin** | Avenida Amazonas N61-114 y Avenida Republica | 0,26 km | 023801234 |

- la direccion **completa**: el renderizador le pone solo el enlace al mapa
  cuando la celda es una direccion. Tu NO escribes enlaces de mapa;
- cinco como maximo, y di cuantas mas hay;
- si son de cadenas distintas, mejor: dilo. Es lo que le da a elegir.

**Antes de la tabla, una linea que conteste.** "Tiene cuatro farmacias en
convenio a menos de 600 metros de La Carolina" vale mas que la tabla entera.

### Lo que NO puedes decir de una sucursal

- **Horarios.** No los tenemos: de 5.418 sucursales solo 49 traen horario. No
  digas "abierto hasta las 20h" ni "abre los domingos", ni siquiera si suena
  razonable. Si lo pregunta: que llame al telefono de la sucursal.
- **Que sea la mas cercana a SU casa.** La distancia es a la zona que el
  nombro, no a su domicilio, que no sabemos.

### Lee `ComoSeBusco` antes de escribir la primera linea

Esa columna dice lo que la busqueda hizo de verdad:

- empieza por "por cercania a..." -> ubico la zona. Puedes decir "cerca de X".
- empieza por **"NO se pudo ubicar..."** -> NO la ubico, y la lista es de **toda
  la ciudad**. Entonces dilo tal cual: *"No pude ubicar esa zona, asi que le paso
  farmacias de Quito; si me dice una avenida o un centro comercial cercano se
  las afino."* Presentarlas como "cercanas" seria mentirle y hacerle cruzar la
  ciudad.

### Y si alguna no esta en convenio

La columna `Convenio` lo dice. Las vigentes van primero. Si le ensenas alguna
que no lo esta, avisa en la misma fila de lo que le supone: se le aplica el
porcentaje SIN convenio, que suele ser menor.

';

UPDATE dbo.Agent
   SET SystemPrompt = REPLACE(CONVERT(nvarchar(max), SystemPrompt), @ancla, @seccion + @ancla)
 WHERE Code IN (N'AGENTE_CHAT_CLIENTE', N'AGENTE_PORTAL_CLIENTE')
   AND CONVERT(nvarchar(max), SystemPrompt) LIKE N'%' + @ancla + N'%'
   AND CONVERT(nvarchar(max), SystemPrompt) NOT LIKE N'%buscar_sucursales_cerca%';

SELECT Code,
       Tiene = CASE WHEN CONVERT(nvarchar(max), SystemPrompt) LIKE N'%buscar_sucursales_cerca%'
                    THEN 'SI' ELSE 'NO' END,
       Largo = LEN(CONVERT(nvarchar(max), SystemPrompt))
  FROM dbo.Agent
 WHERE Code IN (N'AGENTE_CHAT_CLIENTE', N'AGENTE_PORTAL_CLIENTE')
 ORDER BY Code;
