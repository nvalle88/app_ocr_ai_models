/* =============================================================================
   REQ-033c - Dos tools decian hacer lo mismo, y gano la equivocada
   -----------------------------------------------------------------------------
   Con buscar_sucursales_cerca ya publicada y enlazada, Nestor probo su propia
   frase -"estoy buscando una farmacia en Quito por la Carolina"- y el agente
   llamo a la tool VIEJA:

       buscar_prestador_convenio {"tipo":"farmacia","ciudad":"Quito","cerca":"Carolina"}
       -> 1 fila: FARCOMED, "Sucursales: 194"

   Y contesto "se trata de la cadena Fybeca (Farcomed)". Correcto como dato,
   inutil como respuesta: preguntaron cual les queda cerca, no de que cadena es.

   No es cosa del modelo. Su descripcion decia, literalmente, "o CERCA de un
   sector", y tiene un parametro `cerca`. Dos herramientas anunciando el mismo
   trabajo: gana la que ya conocia. Comprobado ademas que no hay cache -el
   agente y sus tools se leen de la BD en cada peticion, ChatClienteController
   linea 305-, asi que la nueva SI estaba disponible y aun asi no la eligio.

   La solucion no es insistir en el prompt: es que cada tool diga lo que hace y
   lo que NO hace. `cerca` se queda -hay llamadas que lo usan como filtro de
   texto y quitarlo las romperia-, pero deja de anunciarse como busqueda por
   cercania, que nunca fue: es un LIKE, no calcula distancias.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @desc nvarchar(1000) = N'Busca PRESTADORES en los convenios: por RUC, nombre, numero de convenio, CIUDAD, TIPO (hospital, farmacia, laboratorio, medico) o ESPECIALIDAD -o el sintoma-. Da UNA FILA POR PRESTADOR, con sus sucursales resumidas en un texto, y dice si el convenio esta VIGENTE y si se aplica el porcentaje CON convenio. NO la uses para una ZONA: si preguntan que farmacia les queda cerca de la Carolina, eso es buscar_sucursales_cerca, que da una fila por sucursal con la distancia en km.';
DECLARE @cerca nvarchar(1000) = N'Filtra prestadores cuya direccion o sector contenga este texto. OJO: es un filtro de texto, NO una busqueda por cercania -no calcula distancias-. Para ''cerca de la Carolina'' usa buscar_sucursales_cerca.';

UPDATE dbo.OPAITool
   SET Description = @desc
 WHERE Code = N'buscar_prestador_convenio';

/* El parametro tambien se anunciaba mal: el modelo lo lee igual que la
   descripcion de la tool y de ahi sacaba que servia para "cerca de". */
UPDATE dbo.OPAITool
   SET InputSchema = JSON_MODIFY(CONVERT(nvarchar(max), InputSchema),
                                 '$.properties.cerca.description', @cerca)
 WHERE Code = N'buscar_prestador_convenio'
   AND ISJSON(CONVERT(nvarchar(max), InputSchema)) = 1;

SELECT Code,
       Largo = LEN(Description),
       DiceCerca = CASE WHEN Description LIKE N'%CERCA de un sector%' THEN 'SI (mal)' ELSE 'no' END,
       Remite = CASE WHEN Description LIKE N'%buscar_sucursales_cerca%' THEN 'SI' ELSE 'NO' END
  FROM dbo.OPAITool
 WHERE Code IN (N'buscar_prestador_convenio', N'buscar_sucursales_cerca')
 ORDER BY Code;
