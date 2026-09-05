/* =============================================================================
   REQ-042b - El precio de la consulta del medico, que faltaba entero
   -----------------------------------------------------------------------------
   Nestor: "sigue sin resolver el precio de consulta del medico".

   Y no estaba resuelto porque lo unico que habia -copago_del_prestador- solo
   sabe contestar por los prestadores que tienen un copago NEGOCIADO. Medido
   hoy: de 2.270 convenios con plan cargado, solo 860 tienen copago de consulta.
   Sobre el total de la red son muchisimos menos. Para un medico cualquiera esa
   tool devuelve cero filas, y de ahi la respuesta de siempre: no puedo darle
   el precio.

   -- Pero el precio SI existe, para todos ----------------------------------
   Es la regla general del contrato, la misma que aplica el motor al liquidar
   (CalcularValoresConsultaMedica en api-prestador). Tiene tres piezas y las
   tres estan en la base:

     el precio     Lr05Procedimientos 99201 da 1,00 punto, y Tg14ValorPunto
                   pone el valor del punto POR NIVEL del prestador:

                       nivel 1  US$ 14      nivel 6   US$ 50
                       nivel 2      21      nivel 7       63
                       nivel 3      26      nivel 8       80
                       nivel 4      32      nivel 9       80
                       nivel 5      41      nivel 10      98

     el nivel      Co03Convenio.NivelPrestadorDesde/Hasta dice de que nivel es
                   el prestador; Pr02Planes.NivelReferencia, de que nivel es el
                   plan. N5-C es nivel 5, y de ahi le viene el nombre.

     el porcentaje Pr05Beneficios.PorcentajeConConvenio para A002, cobertura
                   PER01 -enfermedad personal- y tipo Ambulatorio o Ambos.

   -- Y el castigo por subir de nivel ----------------------------------------
   Si el prestador es de nivel mas alto que el plan, la cobertura baja un 20%
   por cada nivel de diferencia. Ese 20 es el parametro PORCENTAJE-CASTIGO, y
   esta puesto en la base, no inventado aqui.

   -- Comprobado, dos casos reales ------------------------------------------
   Plan IND / N5-C v32, que es nivel 5:

     Dr. Coloma Pazmino, niveles 1-7   consulta US$ 41, le cubren el 80% =
                                       US$ 33, PAGA US$ 8
     Veris, nivel 8                    consulta US$ 80, pero el plan es nivel 5
                                       y van tres niveles de diferencia:
                                       80% x 0,8 x 0,8 x 0,8 = 40,96%,
                                       le cubren US$ 33 y PAGA US$ 47

   El segundo ademas ensena por que hace falta la cascada: en Veris hay copago
   negociado de US$ 5, y ESE es el que se cobra. La regla general es el suelo,
   no la ultima palabra.

   -- Un duplicado que devolvia el doble de filas ---------------------------
   Tg14ValorPunto tiene el nivel 8 DOS veces vigente -cargado en 2018 y otra vez
   en 2025-. Hoy las dos valen 80, asi que no cambia el importe, pero doblaba
   las filas y el dia que carguen valores distintos habria devuelto dos precios
   para la misma consulta. Se desempata por la carga mas reciente.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @code nvarchar(100) = N'precio_consulta_medico';
DECLARE @desc nvarchar(1000) = N'EL PRECIO DE LA CONSULTA CON UN MEDICO y cuanto le queda por pagar al afiliado. Funciona con CUALQUIER prestador, tenga o no copago negociado, porque sale de la regla general del contrato: precio de consulta segun el NIVEL del prestador, menos el porcentaje que cubre su plan. Usala siempre que pregunten cuanto cuesta o cuanto pagan por ver a un medico. Si el prestador ademas tiene copago negociado -copago_del_prestador- ESE manda.';
DECLARE @schema nvarchar(max) = N'{
  "type": "object",
  "properties": {
    "numeroConvenio": {
      "type": "string",
      "description": "OBLIGATORIO. Convenio del prestador, o varios separados por coma. Es la misma llave que devuelven buscar_prestador_convenio y buscar_sucursales_cerca."
    },
    "region": {
      "type": "string",
      "description": "Region del contrato del afiliado: Costa o Sierra. Del contrato ya resuelto."
    },
    "codigoProducto": {
      "type": "string",
      "description": "Producto del contrato: IND, POO, COR, ONC... Del contrato ya resuelto."
    },
    "codigoPlan": {
      "type": "string",
      "description": "Codigo del plan del afiliado, del contrato ya resuelto. Sin el no se sabe ni el nivel ni el porcentaje y no se puede calcular nada."
    },
    "versionPlan": {
      "type": "string",
      "description": "Version del plan, del contrato resuelto. Sin ella se toma la mas nueva, que no tiene por que ser la suya."
    }
  },
  "required": [
    "numeroConvenio",
    "codigoProducto",
    "codigoPlan"
  ]
}';
DECLARE @binding nvarchar(max) = N'{"connection": "SaludReclamos", "maxRows": 20, "query": "WITH plan_afiliado AS ( SELECT TOP 1 NivelCliente = p.NivelReferencia FROM Salud.dbo.Pr02Planes p WITH (NOLOCK) WHERE p.CodigoPlan = @codigoPlan AND p.CodigoProducto = @codigoProducto AND (LEN(ISNULL(@versionPlan,'''')) = 0 OR p.VersionPlan = TRY_CAST(@versionPlan AS int)) ORDER BY CASE WHEN p.Region = @region THEN 0 WHEN p.Region = ''Sierra'' THEN 1 ELSE 2 END, p.VersionPlan DESC ), porcentaje AS ( SELECT TOP 1 Pct = b.PorcentajeConConvenio, PctSin = b.PorcentajeSinConvenio, Cob = b.CodigoCobertura FROM Salud.dbo.Pr05Beneficios b WITH (NOLOCK) WHERE b.CodigoBeneficio = ''A002'' AND b.CodigoCobertura IN (''PER01'',''INC01'') AND b.TipoCobertura IN (''Ambulatorio'',''Ambos'') AND b.CodigoProducto = @codigoProducto AND b.CodigoPlan = @codigoPlan AND (LEN(ISNULL(@versionPlan,'''')) = 0 OR b.VersionPlan = TRY_CAST(@versionPlan AS int)) ORDER BY CASE WHEN b.CodigoCobertura = ''PER01'' THEN 0 ELSE 1 END, CASE WHEN b.Region = @region THEN 0 WHEN b.Region = ''Sierra'' THEN 1 ELSE 2 END, b.VersionPlan DESC ), puntos AS ( SELECT TOP 1 Puntos = pr.PuntosSalud FROM Salud.dbo.Lr05Procedimientos pr WITH (NOLOCK) WHERE pr.CodigoSalud = 99201 ), medico AS ( SELECT c.NumeroConvenio, c.NombrePrestador, c.TipoPrestador, c.NivelPrestadorDesde, c.NivelPrestadorHasta, Vigente = CASE WHEN c.EstadoConvenio IN (1,41) THEN 1 ELSE 0 END FROM Salud.dbo.Co03Convenio c WITH (NOLOCK) WHERE c.NumeroConvenio IN (SELECT TRY_CAST(LTRIM(RTRIM(value)) AS int) FROM STRING_SPLIT(@numeroConvenio, '','')) ), nivel AS ( SELECT m.*, pa.NivelCliente, NivelAplicado = CASE WHEN pa.NivelCliente BETWEEN m.NivelPrestadorDesde AND m.NivelPrestadorHasta THEN pa.NivelCliente WHEN pa.NivelCliente > m.NivelPrestadorHasta THEN m.NivelPrestadorHasta ELSE m.NivelPrestadorDesde END FROM medico m CROSS JOIN plan_afiliado pa ), punto AS ( SELECT Nivel, Valor FROM ( SELECT Nivel, Valor, rn = ROW_NUMBER() OVER (PARTITION BY Nivel ORDER BY FechaDesde DESC) FROM Salud.dbo.Tg14ValorPunto WITH (NOLOCK) WHERE CodigoGrupoArancel = 2 AND FechaDesde <= CAST(GETDATE() AS date) AND FechaHasta >= CAST(GETDATE() AS date)) z WHERE rn = 1 ), matriz AS ( SELECT n.*, po.Pct AS PctBase, po.Cob, ValorConsulta = CONVERT(decimal(10,2), pu.Puntos * vp.Valor), PctAplicado = CONVERT(decimal(6,2), CASE WHEN n.NivelAplicado > n.NivelCliente THEN po.Pct * POWER(CONVERT(float,0.8), n.NivelAplicado - n.NivelCliente) ELSE po.Pct END) FROM nivel n CROSS JOIN porcentaje po CROSS JOIN puntos pu LEFT JOIN punto vp ON vp.Nivel = n.NivelAplicado ) SELECT TOP 20 Convenio = m.NumeroConvenio, Prestador = m.NombrePrestador, Tipo = m.TipoPrestador, ConvenioVigente = CASE WHEN m.Vigente = 1 THEN ''VIGENTE'' ELSE ''NO vigente'' END, PrecioDeLaConsulta = m.ValorConsulta, PorcentajeQueCubreSuPlan = m.PctAplicado, LeCubren = CONVERT(decimal(10,2), ROUND(m.ValorConsulta * m.PctAplicado / 100.0, 0)), UstedPaga = CONVERT(decimal(10,2), m.ValorConsulta - ROUND(m.ValorConsulta * m.PctAplicado / 100.0, 0)), NivelDelPrestador = m.NivelAplicado, NivelDeSuPlan = m.NivelCliente, PorQueEsteValor = CASE WHEN m.ValorConsulta IS NULL THEN ''no hay valor de consulta cargado para ese nivel: no se puede calcular, no lo inventes'' WHEN m.NivelAplicado > m.NivelCliente THEN ''este prestador es de nivel '' + CONVERT(varchar(4), m.NivelAplicado) + '' y su plan es de nivel '' + CONVERT(varchar(4), m.NivelCliente) + '': por eso le cubren el '' + CONVERT(varchar(10), m.PctAplicado) + '' por ciento en vez del '' + CONVERT(varchar(10), CONVERT(decimal(6,2), m.PctBase)) + '' por ciento habitual'' WHEN m.Vigente = 0 THEN ''este prestador NO tiene convenio vigente: pagaria mas de lo que dice aqui'' ELSE ''su plan cubre el '' + CONVERT(varchar(10), m.PctAplicado) + '' por ciento de la consulta'' END, OjoAntesDeContestar = ''Esta es la regla GENERAL del contrato. Si copago_del_prestador devolvio un copago negociado con este mismo prestador, manda ese y no este.'' FROM matriz m ORDER BY m.Vigente DESC, m.NombrePrestador"}';

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
 WHERE g.Code IN (N'AGENTE_CHAT_CLIENTE', N'AGENTE_PORTAL_CLIENTE', N'AGENTE_CLAUDE')
   AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt
                    WHERE mt.ModelCode = g.Code AND mt.ToolCode = @code);

SELECT t.Code, Json = ISJSON(CONVERT(nvarchar(max), t.BindingConfig)),
       Schema_ok = ISJSON(CONVERT(nvarchar(max), t.InputSchema)),
       Agentes = (SELECT COUNT(*) FROM dbo.OPAIModelTool mt WHERE mt.ToolCode = t.Code)
  FROM dbo.OPAITool t WHERE t.Code = @code;
