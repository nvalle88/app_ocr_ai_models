/* =============================================================================
   REQ-022d - Buscar el doble pago en TODA la base, no solo fuera del contrato
   -----------------------------------------------------------------------------
   La tool tenia un parametro contratoExcluir "para que el caso no se cuente a si
   mismo". Sonaba razonable y estaba mal al reves: escondia justo el duplicado mas
   probable, que es el MISMO contrato presentando dos veces la misma factura.

   Medido sobre 001-100-000000916, clave 30032026...0118:

       contrato 549616    Costa/IND   8 lineas   estado 27   <- el que se presenta
       contrato 41215257  Sierra/COR  1 linea    estado 27   pagado 334,66

   Con contratoExcluir=549616 -que es lo que el agente pasaba- las OCHO lineas del
   contrato en curso desaparecian del resultado y solo se veia la del otro. El
   duplicado que importa era el invisible.

   La factura es UNICA. Si ya se pago, en cualquier contrato y a cualquier persona,
   no se vuelve a pagar. No hay nada que excluir.

   El parametro pasa a llamarse contratoActual y es INFORMATIVO: no filtra, solo
   sirve para marcar en DondeAparece si el hallazgo es en ese mismo contrato -lo
   mas grave- o en otro. Y si es en otro, se advierte de que NO se tomen de esa
   fila la region, el producto ni el plan: la llave del contrato es
   region+producto+contrato y sale de resolver_contrato_por_cedula, no de un
   reclamo ajeno.

   NOTA: esta migracion documenta el cambio. La tool ya quedo actualizada en
   db-nexus-test; volver a correrla es idempotente.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

/* Guarda: si alguien reintroduce el filtro, que se vea. */
IF EXISTS (SELECT 1 FROM dbo.OPAITool
            WHERE Code = 'factura_ya_pagada_bd'
              AND (BindingConfig LIKE '%contratoExcluir%' OR InputSchema LIKE '%contratoExcluir%'))
BEGIN
    RAISERROR('factura_ya_pagada_bd vuelve a filtrar por contrato: eso esconde el duplicado del propio contrato.', 16, 1);
    RETURN;
END

SELECT Code,
       CASE WHEN BindingConfig LIKE '%DondeAparece%' THEN 'marca donde aparece' ELSE 'FALTA la marca' END AS Marca,
       CASE WHEN InputSchema  LIKE '%contratoActual%' THEN 'contratoActual informativo' ELSE 'FALTA contratoActual' END AS Parametro
  FROM dbo.OPAITool WHERE Code = 'factura_ya_pagada_bd';
