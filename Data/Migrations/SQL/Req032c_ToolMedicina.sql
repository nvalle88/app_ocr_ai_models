/* =============================================================================
   REQ-032c - Cobertura de medicamentos: el hueco mas grande, medido
   -----------------------------------------------------------------------------
   Medido en Zendesk: de los 6.037 tickets de "que cobertura quiere conocer",
   1.248 son "medicamento generico / comercial" y 196 "medicamento de
   especialidad". 1.444 llamadas en tres meses -el 24%, el mayor motivo concreto
   de toda la operacion- y el chat no tenia NADA para contestarlas.

   El catalogo es CreditoFarmacia.CFMedicina: 6.711 medicinas, todas activas y
   todas con codigo de beneficio, 1.758 principios activos, 5.248 de MARCA y
   1.461 GENERICO. Trae justo lo que preguntan: si es generico o de marca, si es
   cronico, y en cuantas farmacias con convenio se despacha por credito.

   -- La trampa que habria costado dinero al afiliado ----------------------
   Este catalogo es el del CREDITO DE FARMACIA, no el de lo que cubre el plan.
   Medido: semaglutida -el principio de Ozempic- tiene CERO presentaciones aqui,
   y sin embargo es de los medicamentos de mayor consumo de la casa: va por
   reembolso, no por credito. Metformina tiene 108 y atorvastatina 104.

   Contestar "no consta, no se lo cubren" seria mandar a alguien a pagar de su
   bolsillo algo que si le reembolsan. Por eso la descripcion de la tool y el
   prompt lo dicen con todas las letras: no estar aqui significa que no sale por
   credito en farmacia, NO que no este cubierto.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @code nvarchar(100)    = N'buscar_medicina';
DECLARE @desc nvarchar(1000)   = N'Busca un medicamento en el catalogo de CREDITO DE FARMACIA de Saludsa: si consta, si es GENERICO o de MARCA, si es de tratamiento cronico o continuo, su codigo de beneficio y en cuantas farmacias con convenio se puede despachar por credito. OJO: que NO conste aqui no significa que no este cubierto -puede ir por reembolso-, solo que no sale por credito.';
DECLARE @schema nvarchar(max)  = N'{
  "type": "object",
  "properties": {
    "nombre": {
      "type": "string",
      "description": "Como lo llama el afiliado: la marca -''Losartan Sante''- o el principio activo -''losartan''-. Busca en los dos campos. Con el nombre comercial de la caja suele bastar."
    },
    "principioActivo": {
      "type": "string",
      "description": "El principio activo, si se quiere acotar. Hay 1.758 distintos. Util cuando el afiliado pregunta si hay generico de una marca: mismo principio, TipoProducto GENERICO."
    },
    "codigoSaludsa": {
      "type": "string",
      "description": "Codigo de la medicina en Saludsa, si ya se conoce."
    },
    "tipoProducto": {
      "type": "string",
      "description": "GENERICO o MARCA -son esos dos valores exactos, no ''comercial''-. De 6.711 medicinas, 5.248 son MARCA y 1.461 GENERICO. Sin esto salen las dos, con el generico primero."
    }
  },
  "required": []
}';
DECLARE @binding nvarchar(max) = N'{
  "connection": "SaludsaCreditoFarmacia",
  "maxRows": 25,
  "query": "SELECT TOP 25 m.CodigoMedicinaSaludsa AS CodigoSaludsa, m.Descripcion AS Medicina, m.PrincipioActivo, /* Justo lo que mas preguntan: 1.248 llamadas a Dr Salud en tres meses son \"medicamento generico o comercial\". Aqui esta dicho. */ m.TipoProducto AS GenericoOComercial, m.TipoTratamiento, CASE WHEN m.EsContinuo = 1 THEN ''tratamiento continuo'' END AS Continuo, /* La medicina esta en el catalogo de Saludsa: eso significa que SI se contempla. Lo que cubre su plan de ella sale del beneficio, que va abajo, y se consulta aparte con el plan del afiliado. Estar en el catalogo NO es lo mismo que estar cubierta al 100%. */ CASE WHEN m.Activo = 1 THEN ''SI consta en el catalogo de Saludsa'' ELSE ''consta pero esta dada de baja'' END AS EstaEnElCatalogo, m.CodigoBeneficio AS BeneficioDelPlan, m.CodigoBeneficioNuevosPlanes AS BeneficioPlanesNuevos, CASE WHEN m.AplicaIva = 1 THEN ''con IVA'' ELSE ''sin IVA'' END AS Iva, /* En cuantas farmacias con convenio se puede pedir por credito. Si sale 0, la medicina existe pero no hay farmacia que la despache por credito: eso cambia lo que se le dice al afiliado. */ (SELECT COUNT(DISTINCT v.NumeroConvenio) FROM Saludsa.CreditoFarmacia.CFMedicinaPrestadorVademecum v WITH (NOLOCK) WHERE v.CodigoMedicinaSaludsa = m.CodigoMedicinaSaludsa AND v.Activo = 1) AS FarmaciasConCredito FROM Saludsa.CreditoFarmacia.CFMedicina m WITH (NOLOCK) WHERE (@nombre IS NULL OR LEN(@nombre) = 0 OR m.Descripcion LIKE ''%'' + @nombre + ''%'' OR m.PrincipioActivo LIKE ''%'' + @nombre + ''%'') AND (@principioActivo IS NULL OR LEN(@principioActivo) = 0 OR m.PrincipioActivo LIKE ''%'' + @principioActivo + ''%'') AND (@codigoSaludsa IS NULL OR LEN(@codigoSaludsa) = 0 OR m.CodigoMedicinaSaludsa = TRY_CAST(@codigoSaludsa AS int)) AND (@tipoProducto IS NULL OR LEN(@tipoProducto) = 0 OR m.TipoProducto = @tipoProducto) /* Sin ningun criterio no se devuelve el catalogo entero: 6.711 medicinas en una respuesta de chat no le sirven a nadie. */ AND (LEN(ISNULL(@nombre,'''')) > 0 OR LEN(ISNULL(@principioActivo,'''')) > 0 OR LEN(ISNULL(@codigoSaludsa,'''')) > 0) /* El generico primero: es mas barato para el afiliado y para Saludsa, y es lo que suele estar preguntando quien llama. */ ORDER BY CASE WHEN m.TipoProducto = ''GENERICO'' THEN 0 ELSE 1 END, m.PrincipioActivo, m.Descripcion"
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
