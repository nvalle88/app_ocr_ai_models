/* =============================================================================
   REQ-021 - Las tres tools que faltaban para juzgar una factura
   -----------------------------------------------------------------------------
   El caso 598ec576 lo dejo desnudo: el afiliado presento la factura
   001-100-000000916 del Dr. Munoz y el agente levanto una "ALERTA CRITICA DE
   DUPLICIDAD ... aparece en 6 casos". Esos seis casos eran PRUEBAS INTERNAS del
   propio equipo: lo unico que sabia mirar era buscar_factura_repetida_bd, que
   recorre los documentos que el propio Nexus ha tipificado. Un espejo.

   Mientras tanto, la respuesta de verdad estaba en produccion y nadie la
   consultaba: esa factura YA se habia reclamado y pagado.

       Lr04DetalleReclamo -> reclamo 2133326387, contrato 549616, persona 5195852
       Lr02Reclamos       -> EstadoReclamo 10, FechaPagoReclamo 2026-06-09,
                             MontoPagado 143.42, sin anulacion

   -- Por que la busqueda NO puede ir por numero de factura ---------------------
   Medido: 001-100-000000916 aparece en 37 lineas de reclamo, repartidas en 17
   CLAVES DE ACCESO DISTINTAS y en contratos que no tienen nada que ver
   (Sierra 102, 70, 32 y 290 dolares; Costa 91 y 722,25). El numero es un
   secuencial que cada prestador lleva por su cuenta: dos medicos distintos
   emiten su factura 916 sin saber el uno del otro.

   La identidad de una factura en Ecuador es la CLAVE DE ACCESO de 49 digitos:
   lleva dentro la fecha, el RUC del emisor y el secuencial. Es la unica llave
   que no confunde a un afiliado con otro. Buscar por numero habria negado
   reembolsos legitimos con la excusa de un duplicado que no existe.

   Pero ClaveAcceso NO esta indexada (los indices de Lr04 son PkLr04 e
   IdxNroFactNumConvenio por NroFacturaPrestador). Asi que se entra por el
   numero -que si usa indice- y se FILTRA por la clave. Rapido y correcto.

   -- El orden importa ---------------------------------------------------------
   1. La factura tiene que existir en el repositorio de comprobantes. Si no
      esta, se trae del SRI y se guarda (cargar_factura_desde_sri).
   2. Con la factura autentica en la mano, se mira si ya se pago
      (factura_ya_pagada_bd).
   Al reves no sirve: sin el comprobante real solo se compara contra un OCR, y
   el propio agente lo escribio en el caso: "Sin XML no se contrasta OCR vs SRI".

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

/* ---------------------------------------------------------------------------
   1) La factura autentica: la que guarda el repositorio de comprobantes.
      Responde "existe y que dice el comprobante", NO "se pago".
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Name = 'obtener_factura_repositorio')
BEGIN
    INSERT INTO dbo.OPAITool (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
    VALUES (
        'obtener_factura_repositorio',
        'obtener_factura_repositorio',
        'Trae la factura AUTENTICA desde el repositorio de comprobantes electronicos de Saludsa, por su clave de acceso del SRI (49 digitos) o su numero de autorizacion. Es la fuente de verdad frente al OCR, que es una lectura de una foto: aqui vienen emisor, fecha, subtotales e impuestos tal como los declaro el prestador. Si devuelve vacio, la factura NO esta en el repositorio todavia: entonces usa cargar_factura_desde_sri. Esta tool NO dice si el gasto se pago.',
        N'{
  "type": "object",
  "properties": {
    "claveAcceso": { "type": "string", "description": "Clave de acceso del SRI, 49 digitos. Es la llave unica de la factura." },
    "numeroAutorizacion": { "type": "string", "description": "Numero de autorizacion del SRI. Alternativa a la clave de acceso." }
  },
  "required": []
}',
        0,
        'InternalApi',
        N'{
  "baseUrl": "{api-repositorio}",
  "method": "GET",
  "path": "/api/Documento/ObtenerFactura",
  "paramMap": { "claveAcceso": "claveAcceso", "numeroAutorizacion": "numeroAutorizacion" },
  "bodyMap": [],
  "authMode": "saludsa-oauth"
}',
        1, 1, SYSUTCDATETIME());
END
GO

/* ---------------------------------------------------------------------------
   2) Si no esta en el repositorio: traerla del SRI y guardarla.

   OJO: esta tool ESCRIBE (persiste el comprobante). Es lo unico del catalogo
   que no es de solo lectura, y por eso la descripcion no deja lugar a dudas:
   se usa cuando la factura NO aparece, no "por si acaso".
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Name = 'cargar_factura_desde_sri')
BEGIN
    INSERT INTO dbo.OPAITool (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
    VALUES (
        'cargar_factura_desde_sri',
        'cargar_factura_desde_sri',
        'Busca la factura en el SRI por su clave de acceso y la GUARDA en el repositorio de comprobantes de Saludsa. Usala SOLO cuando obtener_factura_repositorio no la encuentre: sirve para que el comprobante autentico quede disponible y se pueda contrastar contra lo que leyo el OCR. Si el SRI no la reconoce, la factura no esta autorizada, y eso si es un motivo real para no reembolsar.',
        N'{
  "type": "object",
  "properties": {
    "claveAcceso": { "type": "string", "description": "Clave de acceso del SRI, 49 digitos, tomada de la factura." }
  },
  "required": ["claveAcceso"]
}',
        0,
        'InternalApi',
        N'{
  "baseUrl": "{api-repositorio}",
  "method": "POST",
  "path": "/api/Sri",
  "paramMap": { "claveAcceso": "claveAcceso" },
  "bodyMap": [],
  "authMode": "saludsa-oauth"
}',
        1, 1, SYSUTCDATETIME());
END
GO

/* ---------------------------------------------------------------------------
   3) Ya se pago? Contra los reclamos REALES de produccion.

   El SQL no lleva ni una comilla a proposito: va dentro de un JSON que a su vez
   va dentro de un literal de SQL Server, y a tres niveles de escapado un
   apostrofe se convierte en una tarde perdida. Por eso el filtro opcional usa
   LEN() en vez de comparar contra cadena vacia.
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Name = 'factura_ya_pagada_bd')
BEGIN
    INSERT INTO dbo.OPAITool (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
    VALUES (
        'factura_ya_pagada_bd',
        'factura_ya_pagada_bd',
        'Dice si ESTA factura ya se presento en un reclamo y si ya se pago, contra los reclamos reales de produccion. Se identifica por la CLAVE DE ACCESO del SRI: el numero de factura NO vale solo, cada prestador lleva su secuencial y el mismo numero sale en decenas de reclamos ajenos. Devuelve reclamo, contrato, persona, estado y lo pagado; YaPagado=1 significa pagado y no anulado. Sin filas = no consta reclamada, pero los reclamos antiguos no guardan clave de acceso: la ausencia no es prueba absoluta.',
        N'{
  "type": "object",
  "properties": {
    "numeroFactura": { "type": "string", "description": "Numero de factura con el formato del prestador, p.ej. 001-100-000000916. Obligatorio: es lo que permite usar el indice." },
    "claveAcceso": { "type": "string", "description": "Clave de acceso del SRI, 49 digitos. Es lo que identifica la factura de verdad." },
    "contratoExcluir": { "type": "string", "description": "Contrato en curso, para que no se cuente a si mismo. Opcional." }
  },
  "required": ["numeroFactura", "claveAcceso"]
}',
        0,
        'Sql',
        N'{
  "connection": "SaludReclamos",
  "maxRows": 20,
  "query": "SELECT TOP 20 d.NumeroReclamo, d.NumeroAlcance, d.Region, d.CodigoProducto, d.ContratoNumero, d.PersonaNumero, d.NroFacturaPrestador, d.ValorFacturaPrestador, r.EstadoReclamo, r.FechaPresentacionReclamo, r.FechaPagoReclamo, r.MontoPagado, r.FechaAnulacion, CASE WHEN r.EstadoReclamo = 10 AND r.FechaAnulacion IS NULL THEN 1 ELSE 0 END AS YaPagado FROM Salud.dbo.Lr04DetalleReclamo d WITH (NOLOCK) LEFT JOIN Salud.dbo.Lr02Reclamos r WITH (NOLOCK) ON r.NumeroReclamo = d.NumeroReclamo AND r.NumeroAlcance = d.NumeroAlcance WHERE d.NroFacturaPrestador = @numeroFactura AND d.ClaveAcceso = @claveAcceso AND (@contratoExcluir IS NULL OR LEN(@contratoExcluir) = 0 OR d.ContratoNumero <> @contratoExcluir) ORDER BY r.FechaPagoReclamo DESC"
}',
        1, 1, SYSUTCDATETIME());
END
GO

/* ---------------------------------------------------------------------------
   Verificacion. Name DEBE ser igual a Code: si divergen, el motor devuelve
   BadRequest y el paso del agente vuelve en un segundo sin haber hecho nada.
   --------------------------------------------------------------------------- */
SELECT Name, BindingType, IsActive,
       CASE WHEN Name = Code THEN 'ok' ELSE 'DESCUADRE Name<>Code' END AS Coherencia
  FROM dbo.OPAITool
 WHERE Name IN ('obtener_factura_repositorio', 'cargar_factura_desde_sri', 'factura_ya_pagada_bd')
 ORDER BY Name;
GO
