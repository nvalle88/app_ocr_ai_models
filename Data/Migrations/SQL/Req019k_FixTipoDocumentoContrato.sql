/* =============================================================================
   REQ-019k — Arreglo del enum de tipoDocumento en resolver_contrato_por_cedula
   -----------------------------------------------------------------------------
   DEFECTO
     El InputSchema declaraba  tipoDocumento: "CEDULA (default), RUC o PASAPORTE"
     y la API /api/contrato/ObtenerContratoPorDocumento solo acepta:

         C = cedula        P = pasaporte

     Con "CEDULA" la API responde  {"Estado":"Error",
     "Mensajes":["No existen datos de: contratos"]} — un mensaje que NO delata
     que el problema es el formato del parametro, asi que parecia que el
     afiliado no tenia contrato.

   IMPACTO
     Esa llamada es la UNICA que devuelve CodigoPlan, y las tres tools de
     cobertura (consultar_coberturas_plan, consultar_deducibles_coberturas_plan,
     consultar_coberturas_plan_prestador) lo exigen. Resultado: toda la rama de
     coberturas quedaba muerta y la resolucion cerraba con
     "cobertura del plan no verificable por API".

   COMPROBADO (caso NA-2612602, cedula 0911002475, API de pruebas)
     tipoDocumento=CEDULA -> Estado=Error "No existen datos de: contratos"
     tipoDocumento=C      -> Estado=OK    Numero 549616, CodigoPlan "N4-D-C",
                                          NombrePlan "Ideal 4d Costa", Nivel 4,
                                          CoberturaMaxima 45000.00, Activo

   OJO CON LA CEDULA
     Esta API la exige CON el cero inicial, a 10 digitos: "911002475" devuelve
     "Error de formato en el campo: numeroDocumento como cedula no cumple...".
     Es lo CONTRARIO de las tablas de Saludsa, que la guardan sin el cero: no
     "corregir" esto quitandoselo. El ejecutor tambien lo rellena por su cuenta
     (InternalApiToolExecutor.NormalizarParametroSaludsa) como red de seguridad.

   Idempotente y con guarda de base.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    RAISERROR('Este script solo debe correr en db-nexus-test. Base actual: %s', 16, 1, @@SERVERNAME);
    RETURN;
END
GO

DECLARE @schema NVARCHAR(MAX) = N'{
  "type": "object",
  "properties": {
    "numeroDocumento": {
      "type": "string",
      "description": "Cedula del afiliado a 10 digitos CON el cero inicial (ej. 0911002475), o numero de pasaporte. La API rechaza la cedula de 9 digitos por formato."
    },
    "tipoDocumento": {
      "type": "string",
      "enum": ["C", "P"],
      "default": "C",
      "description": "C = cedula, P = pasaporte. La API SOLO acepta estas dos letras; enviar CEDULA o PASAPORTE completos devuelve \"No existen datos de: contratos\"."
    }
  },
  "required": ["numeroDocumento", "tipoDocumento"]
}';

UPDATE OPAITool
SET InputSchema  = @schema,
    Description  = N'Resuelve el contrato del afiliado por su documento (ObtenerContratoPorDocumento). '
                 + N'Devuelve region, producto, numero de contrato y CodigoPlan/NombrePlan/Nivel/CoberturaMaxima. '
                 + N'El CodigoPlan que devuelve es el que exigen las tools de coberturas y deducibles del plan, '
                 + N'asi que esta llamada va PRIMERO en la cadena. tipoDocumento debe ser C (cedula) o P (pasaporte).',
    VersionNumber = VersionNumber + 1
WHERE Code = 'resolver_contrato_por_cedula'
  AND (InputSchema <> @schema OR InputSchema IS NULL);
GO

-- Verificacion
SELECT Code,
       VersionNumber,
       -- CHARINDEX y no LIKE: en LIKE el corchete es comodin (clase de caracteres)
       -- y '["C", "P"]' nunca casaria, dando un falso PENDIENTE.
       CASE WHEN CHARINDEX('"enum": ["C", "P"]', InputSchema) > 0 THEN 'OK enum C/P'
            ELSE 'PENDIENTE' END AS Estado
FROM OPAITool
WHERE Code = 'resolver_contrato_por_cedula';
GO
