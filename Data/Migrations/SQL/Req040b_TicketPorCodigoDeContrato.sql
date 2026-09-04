/* =============================================================================
   REQ-040b - El ticket no salia por confundir NUMERO con CODIGO de contrato
   -----------------------------------------------------------------------------
   Nestor: "pq falla, busca alguno que tenga ticket de seguimiento". Fallaba, y
   los sobres SI tenian ticket.

   Medido en bdd_MessageBroker, sin filtrar por contrato:

       NA-2612672  CodigoContrato=1350017  persona=5446025  ticket=55022
       NA-2612673  CodigoContrato=1350017  persona=5446025  ticket=55023
       NA-2612674  CodigoContrato=1350017  persona=5446025  ticket=55024
       NA-2612698  CodigoContrato=1350017  persona=5446025  ticket=55045

   Y el chat mandaba contrato=4102902, que es el NUMERO de contrato. Sobres con
   CodigoContrato=4102902: CERO. La persona si coincidia.

   Es la confusion de siempre en esta casa: el contrato tiene NUMERO -el que ve
   el afiliado, 4102902- y CODIGO -el interno, 1350017-. La resolucion de
   contrato devuelve los dos: "Numero":4102902 y "Codigo":1350017. El broker
   guarda el CODIGO.

   -- El arreglo, y por que asi ---------------------------------------------
   Se podria pedirle al modelo que mande el codigo, pero se le olvidara: son dos
   numeros que se parecen y significan cosas distintas. Asi que la consulta
   acepta CUALQUIERA de los dos en @contrato y compara contra CodigoContrato,
   y el amarre anti-IDOR pasa a apoyarse en @persona, que si coincide y es
   igual de especifica del afiliado.

   Se anade ademas ContratoQueUso a la salida: si el contrato que mandaron no
   filtro nada, que se vea, en vez de devolver filas y dejar creer que si.

   -- Y no se filtra a ciegas ------------------------------------------------
   Sigue haciendo falta al menos un identificador: numero de sobre, contrato o
   persona. Sin ninguno esto seria un listado de los sobres de todo el mundo.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @viejo nvarchar(max) = N'AND (@contrato IS NULL OR LEN(@contrato) = 0 OR s.CodigoContrato = TRY_CAST(@contrato AS int)) AND (@persona IS NULL OR LEN(@persona) = 0 OR s.NumeroPersonaBeneficiario = TRY_CAST(@persona AS int))';
DECLARE @nuevo nvarchar(max) = N'AND (@persona IS NULL OR LEN(@persona) = 0 OR s.NumeroPersonaBeneficiario = TRY_CAST(@persona AS int)) AND (@contrato IS NULL OR LEN(@contrato) = 0 OR s.CodigoContrato = TRY_CAST(@contrato AS int) OR LEN(ISNULL(@persona,'''')) > 0 OR LEN(ISNULL(@numeroSobre,'''')) > 0)';

UPDATE dbo.OPAITool
   SET BindingConfig = REPLACE(CONVERT(nvarchar(max), BindingConfig), @viejo, @nuevo)
 WHERE Code = N'consultar_ticket_sobre'
   AND CONVERT(nvarchar(max), BindingConfig) LIKE N'%' + @viejo + N'%';

/* Y que la fila diga con que contrato se encontro, para no esconder el desajuste. */
DECLARE @v2 nvarchar(200) = N'SELECT TOP 5 s.NumeroSobre, ';
DECLARE @n2 nvarchar(400) = N'SELECT TOP 5 s.NumeroSobre, ContratoDelSobre = s.CodigoContrato, PersonaDelSobre = s.NumeroPersonaBeneficiario, ';

UPDATE dbo.OPAITool
   SET BindingConfig = REPLACE(CONVERT(nvarchar(max), BindingConfig), @v2, @n2)
 WHERE Code = N'consultar_ticket_sobre'
   AND CONVERT(nvarchar(max), BindingConfig) NOT LIKE N'%ContratoDelSobre%';

/* La ficha del parametro tambien lo advierte: el modelo la lee. */
UPDATE dbo.OPAITool
   SET InputSchema = JSON_MODIFY(CONVERT(nvarchar(max), InputSchema),
        '$.properties.contrato.description',
        N'Contrato del afiliado. OJO: el broker guarda el CODIGO de contrato (1350017), no el NUMERO que ve el afiliado (4102902). Si tienes los dos, manda el codigo. Si solo tienes el numero, manda tambien la persona: con ella basta para acotar al afiliado.')
 WHERE Code = N'consultar_ticket_sobre'
   AND ISJSON(CONVERT(nvarchar(max), InputSchema)) = 1;

SELECT Code,
       Json = ISJSON(CONVERT(nvarchar(max), BindingConfig)),
       Arreglada = CASE WHEN CONVERT(nvarchar(max), BindingConfig) LIKE N'%ContratoDelSobre%'
                        THEN 'SI' ELSE 'NO' END
  FROM dbo.OPAITool WHERE Code = N'consultar_ticket_sobre';
