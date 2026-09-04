/* =============================================================================
   REQ-034c - factura_ya_pagada_bd no podia decir que si, nunca
   -----------------------------------------------------------------------------
   Su WHERE era:

       WHERE d.NroFacturaPrestador = @numeroFactura
         AND (d.ClaveAcceso = @claveAcceso OR d.NumeroAutorizacion = @claveAcceso)

   Las dos condiciones unidas con AND y sin guarda de nulo. Cuando el agente
   manda SOLO el numero de factura -que es el caso normal, y el unico dato que
   trae una factura en papel-, @claveAcceso llega NULL, NULL = NULL da
   DESCONOCIDO, y la consulta entera se cae. Cero filas. Siempre.

   Medido, con dos facturas cuyo reclamo esta en EstadoReclamo = 10 (pagado) en
   el mismo servidor al que mira la app:

       001-002-000002600 -> OK, 0 filas
       005-002-000003972 -> OK, 0 filas

   Y comprobado que el dato SI esta: la misma union hecha a mano
   -Lr04DetalleReclamo con Lr02Reclamos por NumeroReclamo, EstadoReclamo = 10-
   devuelve esas dos facturas.

   -- Lo que le costaba al afiliado ------------------------------------------
   La tool contesta "esta factura ya se pago?". Al devolver siempre 0, contesta
   siempre NO. Una factura ya reembolsada se vuelve a presentar y se paga dos
   veces, y las dos consumen el TECHO ANUAL y el deducible del afiliado: se le
   come cobertura que nunca recibio. Cuando auditoria lo encuentre meses
   despues, el debito le llega a el.

   -- El arreglo -------------------------------------------------------------
   Cada filtro se vuelve opcional por separado, como en el resto del catalogo, y
   se anade una guarda para que no barra la tabla sin ningun filtro: hace falta
   al menos uno de los dos.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @viejo nvarchar(400) = N'WHERE d.NroFacturaPrestador = @numeroFactura AND (d.ClaveAcceso = @claveAcceso OR d.NumeroAutorizacion = @claveAcceso)';
DECLARE @nuevo nvarchar(900) = N'WHERE (LEN(ISNULL(@numeroFactura,'''')) > 0 OR LEN(ISNULL(@claveAcceso,'''')) > 0) AND (@numeroFactura IS NULL OR LEN(@numeroFactura) = 0 OR d.NroFacturaPrestador = @numeroFactura) AND (@claveAcceso IS NULL OR LEN(@claveAcceso) = 0 OR d.ClaveAcceso = @claveAcceso OR d.NumeroAutorizacion = @claveAcceso)';

UPDATE dbo.OPAITool
   SET BindingConfig = REPLACE(CONVERT(nvarchar(max), BindingConfig), @viejo, @nuevo)
 WHERE Code = N'factura_ya_pagada_bd'
   AND CONVERT(nvarchar(max), BindingConfig) LIKE N'%' + @viejo + N'%';

SELECT Code,
       Json = ISJSON(CONVERT(nvarchar(max), BindingConfig)),
       Arreglada = CASE WHEN CONVERT(nvarchar(max), BindingConfig) LIKE N'%@numeroFactura IS NULL%'
                        THEN 'SI' ELSE 'NO' END
  FROM dbo.OPAITool
 WHERE Code = N'factura_ya_pagada_bd';
