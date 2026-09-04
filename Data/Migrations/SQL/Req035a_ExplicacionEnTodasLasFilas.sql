/* =============================================================================
   REQ-035a - La columna de explicacion no puede quedar en blanco
   -----------------------------------------------------------------------------
   Nestor, viendo la tabla de autorizaciones ya funcionando: "no dijimos que era
   una parte de explicacion tambien???". Tenia razon: en su tabla, las filas
   AUTORIZADAS salian con un guion en la columna "Por que".

       5132803  No autorizada  Clinica Adventista  Su medico tratante no es afiliado...
       5132660  Autorizada     Clam Medical        -
       5132659  Autorizada     Clam Medical        -

   Se le explica al afiliado por que le NIEGAN algo, pero cuando se lo aprueban
   se le deja a medias. Y ahi es justo donde estan las condiciones que le van a
   costar dinero: que incluye el paquete, que se le cobra aparte.

   -- No hacia falta tocar la consulta --------------------------------------
   Comprobado antes de escribir SQL de mas: la tool YA devuelve
   ObservacionImportante, y viene con el texto RELLENADO, no con la plantilla.
   Ejemplo real del contrato 4102902, autorizacion 392705:

       "Paquete: Histeroscopia Quirurgica N-5
        Incluye: Insumos, suministros, medicamentos y equipos necesarios para el
        procedimiento
        HONORARIOS M..."

   Y en otra: "Nota: Valores por copago, fee, diferencias de nivel, no
   cubiertos, excesos, se cancelaran en su totalidad al momento del alta".

   Eso es exactamente lo que el afiliado necesita saber ANTES de entrar al
   quirofano, y estaba llegando al modelo sin que lo usara. El dato estaba; lo
   que faltaba era la instruccion.

   -- Y cuando de verdad no hay nada ----------------------------------------
   Se dice que no hay nada anotado, no un guion. Un guion parece un fallo de la
   tabla; "sin observaciones" es informacion. Y se aclara lo que sigue siendo
   cierto igual: el copago y el deducible del plan se aplican aunque no haya
   observaciones. Callarselo seria dejarle creer que no paga nada.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @ancla nvarchar(200) = N'## Los datos de contacto: el 6020920, y nada mas';
DECLARE @seccion nvarchar(max) = N'## En la tabla de autorizaciones, la columna "Por que" NUNCA va vacia

Si a una fila le pones un guion, el afiliado lee que la tabla esta rota. Y en
las AUTORIZADAS es justo donde estan las condiciones que le van a costar dinero.

El orden para llenarla, con lo que ya te devuelve la tool:

1. Si NO se autorizo -> `PorQueNoSeAutorizo`, el motivo tal cual.
2. Si SI se autorizo y hay `ObservacionImportante` -> ESO va en la columna. Viene
   con el texto ya armado, no con plantillas. Resumelo en una linea util:

       "Paquete: Histeroscopia Quirurgica N-5. Incluye insumos, medicamentos y
        equipos. Los honorarios medicos van aparte."

   Si la observacion avisa de valores a su cargo -copago, fee, diferencias de
   nivel, excesos-, ESO es lo primero que va, porque es lo que le va a costar
   dinero.
3. Si esta autorizada y no hay observacion -> escribe "Autorizada, sin
   observaciones anotadas" y NO pongas un guion.

Y una cosa que no puedes dejar creer: que no haya observaciones **no** significa
que no pague nada. El copago y el deducible de su plan se aplican igual. Si te
pregunta cuanto le tocara pagar, usa las herramientas del copago y del
deducible; no lo deduzcas de esta tabla.

';

UPDATE dbo.Agent
   SET SystemPrompt = REPLACE(CONVERT(nvarchar(max), SystemPrompt), @ancla, @seccion + @ancla)
 WHERE Code = N'AGENTE_CHAT_CLIENTE'
   AND CONVERT(nvarchar(max), SystemPrompt) LIKE N'%' + @ancla + N'%'
   AND CONVERT(nvarchar(max), SystemPrompt) NOT LIKE N'%NUNCA va vacia%';

SELECT Code,
       Tiene = CASE WHEN CONVERT(nvarchar(max), SystemPrompt) LIKE N'%NUNCA va vacia%'
                    THEN 'SI' ELSE 'NO' END,
       Largo = LEN(CONVERT(nvarchar(max), SystemPrompt))
  FROM dbo.Agent WHERE Code = N'AGENTE_CHAT_CLIENTE';
