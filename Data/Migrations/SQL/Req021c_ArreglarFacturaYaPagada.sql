/* =============================================================================
   REQ-021c - factura_ya_pagada_bd: tres defectos, medidos
   -----------------------------------------------------------------------------
   La primera version de la tool (Req021a) funcionaba pero mentia de tres formas
   distintas. Los tres salieron de correrla contra produccion con la factura
   001-100-000000916 (clave 3003...0118) del caso 598ec576.

   1) PERDIA RECLAMOS. Filtraba solo por ClaveAcceso, y la clave de 49 digitos no
      siempre vive en esa columna. Medido sobre las 37 lineas de ese numero de
      factura: 26 la llevan en ClaveAcceso y 30 en NumeroAutorizacion.

        reclamo      contrato               ClaveAcceso   NumeroAutorizacion
        2133326387   549616  Costa IND           2                2
        2133325944   41215257 Sierra COR         0                2   <-- perdido

      El reclamo perdido es del MISMO afiliado, en su otra poliza, y esta pagado.
      Una tool antiduplicados que se deja fuera justo el duplicado no sirve de
      nada: habria dado via libre a un tercer cobro de la misma factura.

   2) DUPLICABA FILAS. Sin GROUP BY, un reclamo con dos lineas de la misma
      factura salia dos veces. El modelo lo lee como dos reclamos distintos y
      concluye lo que no es. Ahora una fila = un reclamo.

   3) SUMABA UN VALOR QUE NO SE SUMA. ValorFacturaPrestador se repite IGUAL en
      cada linea -es el total de la factura anotado en el reclamo, no el importe
      de la linea-, asi que SUM lo multiplicaba por el numero de lineas:

        2133325944  linea 1 -> 478.08   linea 2 -> 478.08   (SUM daba 956.16)
        2133326387  linea 1 -> 143.42   linea 2 -> 143.42   (SUM daba 286.84)

      Va con MAX. Lo mismo para los campos de la cabecera del reclamo, que se
      repiten por el JOIN.

   Lo que devuelve ahora, con los datos reales:

        2133326387  549616   Costa  IND  pagado 143.42  el 2026-06-09
        2133325944  41215257 Sierra COR  pagado 334.66  el 2026-06-09
                                                 -------
                                                  478.08 = el total de la factura

   Es decir: la factura YA se reembolso entera, repartida entre las dos polizas
   de la misma persona. El propio agente lo habia intuido y no pudo probarlo
   ("podria existir coordinacion de beneficios / excesos. Verificar en control
   humano"), porque no tenia con que mirarlo.

   OJO: OPAITool.Description es nvarchar(1000) pero max_length va en BYTES, o sea
   500 CARACTERES. Pasarse no avisa con un truncado silencioso: revienta el
   INSERT/UPDATE entero y la tool se queda en la version anterior.

   Idempotente y con guarda de base.
   ============================================================================= */

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    RAISERROR('Este script solo debe correr en db-nexus-test. Base actual: %s', 16, 1, @@SERVERNAME);
    RETURN;
END
GO

UPDATE dbo.OPAITool
   SET BindingConfig = N'{
  "connection": "SaludReclamos",
  "maxRows": 20,
  "query": "SELECT TOP 20 d.NumeroReclamo, d.NumeroAlcance, d.Region, d.CodigoProducto, d.ContratoNumero, d.PersonaNumero, COUNT(*) AS LineasDeEstaFactura, MAX(d.ValorFacturaPrestador) AS ValorFacturaPrestador, MAX(r.EstadoReclamo) AS EstadoReclamo, MAX(r.FechaPresentacionReclamo) AS FechaPresentacion, MAX(r.FechaPagoReclamo) AS FechaPago, MAX(r.MontoPagado) AS MontoPagadoDelReclamo, MAX(CASE WHEN r.EstadoReclamo = 10 AND r.FechaAnulacion IS NULL THEN 1 ELSE 0 END) AS YaPagado FROM Salud.dbo.Lr04DetalleReclamo d WITH (NOLOCK) LEFT JOIN Salud.dbo.Lr02Reclamos r WITH (NOLOCK) ON r.NumeroReclamo = d.NumeroReclamo AND r.NumeroAlcance = d.NumeroAlcance WHERE d.NroFacturaPrestador = @numeroFactura AND (d.ClaveAcceso = @claveAcceso OR d.NumeroAutorizacion = @claveAcceso) AND (@contratoExcluir IS NULL OR LEN(@contratoExcluir) = 0 OR d.ContratoNumero <> @contratoExcluir) GROUP BY d.NumeroReclamo, d.NumeroAlcance, d.Region, d.CodigoProducto, d.ContratoNumero, d.PersonaNumero ORDER BY MAX(r.FechaPagoReclamo) DESC"
}',
       Description = N'Dice si esta factura ya se reclamo y si ya se pago, contra los reclamos reales de produccion. La busca por la clave del SRI en ClaveAcceso Y en NumeroAutorizacion: no siempre esta en la misma columna y mirar una sola pierde reclamos. Una fila = un reclamo; YaPagado=1 es pagado y no anulado. Mira PersonaNumero y ContratoNumero: puede haberla cobrado en OTRA poliza suya. Sin filas no prueba nada: los reclamos viejos no guardan la clave.',
       VersionNumber = 2
 WHERE Name = 'factura_ya_pagada_bd';
GO

/* ---------------------------------------------------------------------------
   Verificacion
   --------------------------------------------------------------------------- */
SELECT Name, VersionNumber, IsActive,
       CASE WHEN BindingConfig LIKE '%NumeroAutorizacion = @claveAcceso%'
            THEN 'busca en las dos columnas' ELSE 'FALTA el OR' END AS Cobertura,
       CASE WHEN BindingConfig LIKE '%GROUP BY%' THEN 'una fila por reclamo'
            ELSE 'FALTA el GROUP BY' END AS Agrupacion,
       CASE WHEN BindingConfig LIKE '%SUM(d.ValorFacturaPrestador)%'
            THEN 'SUMA UN VALOR QUE NO SE SUMA' ELSE 'valor con MAX' END AS Valor
  FROM dbo.OPAITool
 WHERE Name = 'factura_ya_pagada_bd';
GO
