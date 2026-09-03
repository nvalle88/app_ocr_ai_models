/* =============================================================================
   REQ-032a - Lo que PAGA el afiliado, no lo que cuesta
   -----------------------------------------------------------------------------
   Nestor: "cuando buscan los doctores debes buscar el valor que debe pagar el
   cliente". El tarifario dice lo que Salud S.A. tiene NEGOCIADO con el
   prestador; el afiliado quiere saber lo que sale de SU bolsillo. No es lo
   mismo y confundirlos es prometerle una cifra que no le van a cobrar.

   -- De donde sale, medido ------------------------------------------------
   NO de la matriz de puntos (CalcularValoresConsultaMedica), que ni siquiera
   recibe convenio: es la matriz general del contrato. Sale de
   BeneficioConvenio.Valor unido por ConvenioPlan -convenio x producto x plan x
   version-, que es lo que alimenta el copago de la cita en api-prestador.

   Y la distincion que evita el error caro, medida sobre 72.937.845 registros:

     A002 = copago de consulta, SIEMPRE en dolares. EsPorcentaje=0 en los
            8.001.811 registros, sin una sola excepcion.
     A003 = cobertura, SIEMPRE porcentaje. EsPorcentaje=1 en los 8.241.610,
            valores de 0 a 100.

   Sin eso, un Valor de 20 se lee igual como 20 dolares que como el 20% de 400.

   Validado contra Veris (11715): el plan N5-C paga 5,00 por consulta; en los
   planes RTM llega a 29,70. Coincide con los copagos reales de citas.

   -- Un centinela que habria mentido --------------------------------------
   Hay valores de 0,00001. No es un precio: es como se guarda el "sin copago".
   Se traduce, porque enseñarlo tal cual seria decirle que paga una cienmilesima
   de dolar.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @code nvarchar(100)    = N'copago_del_prestador';
DECLARE @desc nvarchar(1000)   = N'LO QUE PAGA EL AFILIADO por atenderse con un prestador: el copago de la consulta en dolares (A002/A007) y el porcentaje que le cubre su plan (A003), por convenio x producto x plan x version. OJO: A002 es SIEMPRE dolares y A003 SIEMPRE porcentaje; lee QueEsEsteValor antes de dar una cifra. Esto NO es el precio del tarifario: es lo que sale de su bolsillo.';
DECLARE @schema nvarchar(max)  = N'{
  "type": "object",
  "properties": {
    "numeroConvenio": {
      "type": "string",
      "description": "OBLIGATORIO. Convenio del prestador, o VARIOS separados por coma. Sin el esto barreria 72,9 millones de filas, y ademas no tendria sentido: el copago es de UN prestador. Es la misma llave que devuelve buscar_prestador_convenio."
    },
    "codigoProducto": {
      "type": "string",
      "description": "Producto del contrato -IND, RTM...-. Sale del contrato que viene al principio del mensaje."
    },
    "codigoPlan": {
      "type": "string",
      "description": "Codigo del plan del afiliado, del contrato ya resuelto. MANDALO: sin el salen los copagos de todos los planes y ninguno es necesariamente el suyo."
    },
    "version": {
      "type": "string",
      "description": "Version del plan, del contrato resuelto. Sin ella salen varias versiones y la primera es la mas nueva, que no tiene por que ser la del afiliado."
    },
    "codigoBeneficio": {
      "type": "string",
      "description": "A002 o A007 = consulta medica. A003 = porcentaje de cobertura. Sin esto salen los tres, que juntos contestan la pregunta entera: cuanto paga y cuanto le cubren."
    }
  },
  "required": [
    "numeroConvenio"
  ]
}';
DECLARE @binding nvarchar(max) = N'{
  "connection": "SaludPrestadores",
  "maxRows": 20,
  "query": "SELECT TOP 20 cp.NumeroConvenio, cp.CodigoProducto, cp.CodigoPlan, cp.VersionPlan, bc.CodigoBeneficio, /* A002 es el COPAGO de la consulta y va SIEMPRE en dolares: medido sobre los 8.001.811 registros del beneficio, EsPorcentaje=0 en todos, sin una sola excepcion. A003 es al reves: cobertura, SIEMPRE porcentaje, en sus 8.241.610 registros. Sin decirlo, un Valor de 20 puede leerse como 20 dolares o como el 20% de 400, y al afiliado se le da una cifra que no es la suya. */ CASE WHEN bc.EsPorcentaje = 1 THEN ''porcentaje de cobertura'' ELSE ''lo que paga el afiliado, en dolares'' END AS QueEsEsteValor, bc.Valor, /* Un Valor de 0,00001 no es un precio: es como se guarda el \"sin copago\". Enseñarlo tal cual seria decirle al afiliado que paga una cienmilesima de dolar. Se dice lo que significa. */ CASE WHEN bc.EsPorcentaje = 1 THEN NULL WHEN bc.Valor < 0.01 THEN 0 ELSE bc.Valor END AS UstedPaga, CASE WHEN bc.EsPorcentaje = 0 AND bc.Valor < 0.01 THEN ''sin copago: no paga nada por la consulta'' END AS Nota, CASE WHEN bc.EsPorcentaje = 1 THEN bc.Valor ELSE NULL END AS PorcentajeCubierto, bc.CodigoTipoGestionAtencion AS TipoDeAtencion, CONVERT(varchar(10), cp.FechaInicioVigencia, 120) AS VigenteDesde, CONVERT(varchar(10), cp.FechaFinVigencia, 120) AS VigenteHasta, CASE WHEN cp.EsPromocion = 1 THEN cp.NombrePromocion END AS Promocion FROM dbo.ConvenioPlan cp WITH (NOLOCK) JOIN dbo.BeneficioConvenio bc WITH (NOLOCK) ON bc.IdConvenioPlan = cp.IdConvenioPlan /* Solo lo vigente HOY: un copago que caduco ayer no es lo que le van a cobrar. FechaFinVigencia NULL = sin fecha de fin, que tambien vale. */ WHERE bc.EstadoActivo = 1 AND (cp.FechaFinVigencia IS NULL OR cp.FechaFinVigencia >= CAST(GETDATE() AS date)) /* El convenio es obligatorio: sin el, esto barre 72.937.845 filas de beneficios. Y ademas no tendria sentido: el copago es de un prestador. */ AND cp.NumeroConvenio IN (SELECT TRY_CAST(LTRIM(RTRIM(value)) AS int) FROM STRING_SPLIT(@numeroConvenio, '','')) AND (@codigoProducto IS NULL OR LEN(@codigoProducto) = 0 OR cp.CodigoProducto = @codigoProducto) AND (@codigoPlan IS NULL OR LEN(@codigoPlan) = 0 OR cp.CodigoPlan = @codigoPlan) AND (@version IS NULL OR LEN(@version) = 0 OR cp.VersionPlan = TRY_CAST(@version AS int)) /* Sin beneficio pedido salen los dos que importan: A002 -lo que paga- y A003 -lo que cubre-. Juntos contestan la pregunta entera. */ AND (bc.CodigoBeneficio = @codigoBeneficio OR (LEN(ISNULL(@codigoBeneficio,'''')) = 0 AND bc.CodigoBeneficio IN (''A002'',''A007'',''A003''))) ORDER BY cp.NumeroConvenio, bc.CodigoBeneficio, cp.VersionPlan DESC"
}';

IF EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Code = @code)
    UPDATE dbo.OPAITool
       SET Name = @code, Description = @desc, InputSchema = @schema,
           BindingType = N'Sql', BindingConfig = @binding, IsActive = 1
     WHERE Code = @code;
ELSE
    INSERT dbo.OPAITool (Code, Name, Description, InputSchema, BindingType, BindingConfig,
                         Strict, IsActive, VersionNumber)
    VALUES (@code, @code, @desc, @schema, N'Sql', @binding, 0, 1, 1);

INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT g.Code, @code,
       ISNULL((SELECT MAX(mt.[Order]) FROM dbo.OPAIModelTool mt WHERE mt.ModelCode = g.Code), 0) + 1, 1
  FROM dbo.Agent g
 WHERE g.Code IN (N'AGENTE_CHAT_CLIENTE', N'AGENTE_AUDITOR_MEDICINA', N'AGENTE_CLAUDE')
   AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt
                    WHERE mt.ModelCode = g.Code AND mt.ToolCode = @code);

SELECT mt.ModelCode, mt.ToolCode FROM dbo.OPAIModelTool mt WHERE mt.ToolCode = @code ORDER BY mt.ModelCode;
