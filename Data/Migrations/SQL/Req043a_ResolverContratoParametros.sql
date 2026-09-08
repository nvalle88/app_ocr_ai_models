/* =============================================================================
   REQ-043a - resolver_contrato_por_cedula: los parametros que pide el servicio
   -----------------------------------------------------------------------------
   Nestor probo el endpoint a mano y mostro los parametros correctos:

     /api/contrato/ObtenerContratoPorDocumento
        ?tipoDocumento=C&numeroDocumento=1757100373&codigoProducto=True&canalAcceso=APP-WEB

   Devuelve los 4 contratos del afiliado -COR, EXE, TRK, TRK-, no solo los
   individuales. (Yo habia dicho "solo IND": era falso, salido de dos errores
   MIOS de prueba -leer la clave JSON como "Data" cuando es "Datos", y pasarle
   al diagnostico cedula= en vez de los parametros mapeados-. El endpoint
   siempre estuvo bien.)

   La tool ya mandaba tipoDocumento y numeroDocumento. Faltaban los otros dos,
   que el front del portal manda siempre:

     codigoProducto = True    -> no es un producto, es un flag del endpoint
     canalAcceso    = APP-WEB -> el canal con el que devuelve TODOS los contratos

   Van como enum de un solo valor + default + required, para que el modelo los
   mande siempre, igual que ya hace tipoDocumento (enum C/P).
   ============================================================================= */

IF DB_NAME() NOT IN ('db-nexus-test', 'db-nexus-aud')
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es para db-nexus-test o db-nexus-aud. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @binding nvarchar(max) = N'{
  "baseUrl": "{api-contrato}",
  "method": "GET",
  "path": "/api/contrato/ObtenerContratoPorDocumento",
  "paramMap": {
    "tipoDocumento": "tipoDocumento",
    "numeroDocumento": "numeroDocumento",
    "codigoProducto": "codigoProducto",
    "canalAcceso": "canalAcceso"
  },
  "bodyMap": [],
  "authMode": "saludsa-oauth"
}';

DECLARE @schema nvarchar(max) = N'{
  "type": "object",
  "properties": {
    "numeroDocumento": { "type": "string", "description": "Cedula del afiliado a 10 digitos CON el cero inicial (ej. 0911002475), o numero de pasaporte. La API rechaza la cedula de 9 digitos por formato." },
    "tipoDocumento": { "type": "string", "enum": ["C","P"], "default": "C", "description": "C = cedula, P = pasaporte. La API SOLO acepta estas dos letras." },
    "codigoProducto": { "type": "string", "enum": ["True"], "default": "True", "description": "Manda siempre True: es como lo pide el servicio de contratos (igual que el front del portal). No es un producto, es un flag del endpoint." },
    "canalAcceso": { "type": "string", "enum": ["APP-WEB"], "default": "APP-WEB", "description": "Manda siempre APP-WEB: es el canal con el que el servicio devuelve TODOS los contratos (individuales, corporativos, telemedicina). Sin el, en algunos afiliados devuelve vacio." }
  },
  "required": ["numeroDocumento","tipoDocumento","codigoProducto","canalAcceso"]
}';

UPDATE dbo.OPAITool
   SET BindingConfig = @binding, InputSchema = @schema
 WHERE Code = N'resolver_contrato_por_cedula';

SELECT Code,
       Json = ISJSON(CONVERT(nvarchar(max), BindingConfig)),
       MandaCanal = CASE WHEN CONVERT(nvarchar(max), BindingConfig) LIKE N'%canalAcceso%' THEN 'SI' ELSE 'NO' END,
       MandaCodProd = CASE WHEN CONVERT(nvarchar(max), BindingConfig) LIKE N'%codigoProducto%' THEN 'SI' ELSE 'NO' END
  FROM dbo.OPAITool WHERE Code = N'resolver_contrato_por_cedula';
