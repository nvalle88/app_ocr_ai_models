/* =============================================================================
   REQ-042d - Las cuatro ramas del calculo que me habia saltado
   -----------------------------------------------------------------------------
   Nestor: "para los medicos se calcula por los valores punto, busca eso y hay
   otra forma si es de los nuevos planes, revisa bien el codigo".

   El valor punto estaba bien (REQ-042b). Lo demas no: ObtenerValoresConsultaMedica
   tiene CUATRO ramas antes de tocar la tabla, y yo habia leido el metodo por
   encima y me habia quedado con el caso general. Las cuatro cambian el importe.

   -- 1. Los planes nuevos no cobran por PER01 sino por INC01 ----------------
   El motor cambia de cobertura segun el plan: los de listadoPLanesNuevos y los
   productos corporativos van por INC01; el resto por PER01. Yo pedia PER01
   primero SIEMPRE, y para los planes nuevos eso es el porcentaje equivocado.

   Medido en Pr05Beneficios sobre A002 ambulatorio: 462 combinaciones de plan
   IND tienen las DOS coberturas, y en 16 el porcentaje NO coincide, con hasta
   40 puntos de diferencia:

       PRO150LITE-300 v31     PER01 90%   INC01 50%
       SKY30PLUSLITE-200 v32  PER01 80%   INC01 50%

   Comprobado tras el arreglo: SKY30PLUSLITE-200, prestador nivel 4 ->
   consulta US$ 32, cubren el 50% = US$ 16, PAGA US$ 16. Antes habria dicho
   80% y US$ 6.

   La regla no se escribe con una lista de nombres quemada, que se pudre. Se
   escribe: SI EL PLAN TIENE INC01, MANDA INC01. Reproduce el motor exacto y
   esta comprobado que no se pasa de largo: de los 128 codigos de plan IND con
   las dos coberturas, CERO estan fuera de las familias nuevas (65PLUS, PRO,
   SKY, STAR). Los viejos solo tienen PER01 y siguen igual.

   -- 2. XPR-SUP tiene su propia tarifa -------------------------------------
   Si el codigo de plan contiene XPR-SUP y el prestador llega al nivel del
   contrato, el valor del punto NO sale del arancel 2 sino del 25, que tiene
   una sola fila: nivel 8 = US$ 100. Yo solo consultaba el 2, donde el nivel 8
   vale 80. Veinte dolares de menos en cada consulta.

       XPR-SUP-3M-10K-C + Veris (nivel 8)  ->  US$ 100  tarifa especial
       XPR-SUP-3M-10K-C + Farmaenlace (3-7) -> US$  63  no llega al nivel 8,
                                                        cae al arancel general

   -- 3. SmartPlan se cobra por otro beneficio ------------------------------
   Un contrato COR de una empresa de PORTAL (Cl01Empresas.EmpresaPortal, 501 de
   33.966) no usa A002/99201 sino A007/99208. Los puntos son los mismos -1,00
   los dos- asi que el precio no cambia, pero el PORCENTAJE sale de otra fila:
   de 11.061 combinaciones con las dos, 670 difieren, hasta 80 puntos.
   Comprobado con el contrato 41478005: sale A007, 70%, paga US$ 15.

   -- 4. La region se fuerza, salvo en XPR-SUP ------------------------------
   El motor pone region = Sierra para todo lo que no sea corporativo... menos
   si el plan es XPR-SUP, que respeta la del contrato. Ahora la preferencia se
   invierte segun el caso, en vez de preferir siempre la region del contrato.

   -- Lo que sigue sin poder afinarse ---------------------------------------
   ProductosCorporativos y listadoPLanesNuevos son parametros que el API lee por
   DbConfig, y la tabla Administracion.Parametro esta VACIA en este ambiente, asi
   que no se pueden leer. Por eso la regla se deduce del dato -quien tiene INC01-
   en vez de copiar la lista. Si algun dia cargan un plan nuevo con las dos
   coberturas Y ademas quieren PER01, esta regla se equivocaria; hasta hoy no
   existe ni un caso asi.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @code nvarchar(100) = N'precio_consulta_medico';
DECLARE @desc nvarchar(1000) = N'EL PRECIO DE LA CONSULTA CON UN MEDICO y cuanto le queda por pagar al afiliado. Funciona con CUALQUIER prestador, tenga o no copago negociado, porque sale de la regla general del contrato: precio de consulta segun el NIVEL del prestador, menos el porcentaje que cubre su plan. Ya distingue los planes nuevos, la tarifa especial XPR-SUP y el SmartPlan. Usala siempre que pregunten cuanto cuesta ver a un medico. Si el prestador ademas tiene copago negociado -copago_del_prestador- ESE manda.';
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
      "description": "Producto del contrato: IND, POO, COR, ONC, XPR... Del contrato ya resuelto."
    },
    "codigoPlan": {
      "type": "string",
      "description": "Codigo del plan del afiliado, del contrato ya resuelto. Sin el no se sabe ni el nivel ni el porcentaje y no se puede calcular nada."
    },
    "versionPlan": {
      "type": "string",
      "description": "Version del plan, del contrato resuelto. Sin ella se toma la mas nueva, que no tiene por que ser la suya."
    },
    "numeroContrato": {
      "type": "string",
      "description": "NUMERO de contrato (el que ve el afiliado). Solo hace falta en productos COR: sirve para saber si su empresa es de portal, porque entonces la consulta se cobra por otro beneficio. En los demas productos da igual mandarlo o no."
    }
  },
  "required": [
    "numeroConvenio",
    "codigoProducto",
    "codigoPlan"
  ]
}';
DECLARE @binding nvarchar(max) = N'{"connection": "SaludReclamos", "maxRows": 20, "query": "WITH cfg AS ( SELECT EsXprSup = CASE WHEN @codigoPlan LIKE ''%XPR-SUP%'' THEN 1 ELSE 0 END, EsSmart = CASE WHEN @codigoProducto = ''COR'' AND EXISTS ( SELECT 1 FROM Salud.dbo.Cl04Contratos ct WITH (NOLOCK) JOIN Salud.dbo.Cl01Empresas em WITH (NOLOCK) ON em.EmpresaNumero = ct.EmpresaNumero WHERE ct.ContratoNumero = TRY_CAST(@numeroContrato AS int) AND ct.CodigoProducto = @codigoProducto AND em.EmpresaPortal = 1) THEN 1 ELSE 0 END ), ben AS (SELECT Codigo = CASE WHEN (SELECT EsSmart FROM cfg) = 1 THEN ''A007'' ELSE ''A002'' END, Proc_ = CASE WHEN (SELECT EsSmart FROM cfg) = 1 THEN 99208 ELSE 99201 END), plan_afiliado AS ( SELECT TOP 1 NivelCliente = p.NivelReferencia FROM Salud.dbo.Pr02Planes p WITH (NOLOCK) CROSS JOIN cfg WHERE p.CodigoPlan = @codigoPlan AND p.CodigoProducto = @codigoProducto AND (LEN(ISNULL(@versionPlan,'''')) = 0 OR p.VersionPlan = TRY_CAST(@versionPlan AS int)) ORDER BY CASE WHEN cfg.EsXprSup = 1 THEN CASE WHEN p.Region = @region THEN 0 WHEN p.Region = ''Sierra'' THEN 1 ELSE 2 END ELSE CASE WHEN p.Region = ''Sierra'' THEN 0 WHEN p.Region = @region THEN 1 ELSE 2 END END, p.VersionPlan DESC ), porcentaje AS ( SELECT TOP 1 Pct = b.PorcentajeConConvenio, Cob = b.CodigoCobertura, Ben = b.CodigoBeneficio FROM Salud.dbo.Pr05Beneficios b WITH (NOLOCK) CROSS JOIN cfg CROSS JOIN ben WHERE b.CodigoBeneficio = ben.Codigo AND b.CodigoCobertura IN (''PER01'',''INC01'') AND b.TipoCobertura IN (''Ambulatorio'',''Ambos'') AND b.CodigoProducto = @codigoProducto AND b.CodigoPlan = @codigoPlan AND (LEN(ISNULL(@versionPlan,'''')) = 0 OR b.VersionPlan = TRY_CAST(@versionPlan AS int)) ORDER BY CASE WHEN b.CodigoCobertura = ''INC01'' THEN 0 ELSE 1 END, CASE WHEN cfg.EsXprSup = 1 THEN CASE WHEN b.Region = @region THEN 0 WHEN b.Region = ''Sierra'' THEN 1 ELSE 2 END ELSE CASE WHEN b.Region = ''Sierra'' THEN 0 WHEN b.Region = @region THEN 1 ELSE 2 END END, b.VersionPlan DESC ), puntos AS ( SELECT TOP 1 Puntos = pr.PuntosSalud FROM Salud.dbo.Lr05Procedimientos pr WITH (NOLOCK) CROSS JOIN ben WHERE pr.CodigoSalud = ben.Proc_ ), medico AS ( SELECT c.NumeroConvenio, c.NombrePrestador, c.TipoPrestador, c.NivelPrestadorDesde, c.NivelPrestadorHasta, Vigente = CASE WHEN c.EstadoConvenio IN (1,41) THEN 1 ELSE 0 END FROM Salud.dbo.Co03Convenio c WITH (NOLOCK) WHERE c.NumeroConvenio IN (SELECT TRY_CAST(LTRIM(RTRIM(value)) AS int) FROM STRING_SPLIT(@numeroConvenio, '','')) ), nivel AS ( SELECT m.*, pa.NivelCliente, cfg.EsXprSup, NivelAplicado = CASE WHEN pa.NivelCliente BETWEEN m.NivelPrestadorDesde AND m.NivelPrestadorHasta THEN pa.NivelCliente WHEN pa.NivelCliente > m.NivelPrestadorHasta THEN m.NivelPrestadorHasta ELSE m.NivelPrestadorDesde END, Arancel = CASE WHEN cfg.EsXprSup = 1 AND m.NivelPrestadorHasta > 0 AND pa.NivelCliente > 0 AND m.NivelPrestadorHasta >= pa.NivelCliente THEN 25 ELSE 2 END FROM medico m CROSS JOIN plan_afiliado pa CROSS JOIN cfg ), punto AS ( SELECT CodigoGrupoArancel, Nivel, Valor FROM ( SELECT CodigoGrupoArancel, Nivel, Valor, rn = ROW_NUMBER() OVER (PARTITION BY CodigoGrupoArancel, Nivel ORDER BY FechaDesde DESC) FROM Salud.dbo.Tg14ValorPunto WITH (NOLOCK) WHERE CodigoGrupoArancel IN (2, 25) AND FechaDesde <= CAST(GETDATE() AS date) AND FechaHasta >= CAST(GETDATE() AS date)) z WHERE rn = 1 ), matriz AS ( SELECT n.*, po.Pct AS PctBase, po.Cob, po.Ben, ValorConsulta = CONVERT(decimal(10,2), pu.Puntos * vp.Valor), PctAplicado = CONVERT(decimal(6,2), CASE WHEN n.NivelAplicado > n.NivelCliente THEN po.Pct * POWER(CONVERT(float,0.8), n.NivelAplicado - n.NivelCliente) ELSE po.Pct END) FROM nivel n CROSS JOIN porcentaje po CROSS JOIN puntos pu LEFT JOIN punto vp ON vp.Nivel = n.NivelAplicado AND vp.CodigoGrupoArancel = n.Arancel ) SELECT TOP 20 Convenio = m.NumeroConvenio, Prestador = m.NombrePrestador, Tipo = m.TipoPrestador, ConvenioVigente = CASE WHEN m.Vigente = 1 THEN ''VIGENTE'' ELSE ''NO vigente'' END, PrecioDeLaConsulta = m.ValorConsulta, PorcentajeQueCubreSuPlan = m.PctAplicado, LeCubren = CONVERT(decimal(10,2), ROUND(m.ValorConsulta * m.PctAplicado / 100.0, 0)), UstedPaga = CONVERT(decimal(10,2), m.ValorConsulta - ROUND(m.ValorConsulta * m.PctAplicado / 100.0, 0)), NivelDelPrestador = m.NivelAplicado, NivelDeSuPlan = m.NivelCliente, Tarifa = CASE WHEN m.Arancel = 25 THEN ''tarifa especial XPR-SUP'' ELSE ''tarifa general'' END, TipoDeConsulta = CASE WHEN m.Ben = ''A007'' THEN ''consulta SmartPlan'' ELSE ''consulta medica'' END, PorQueEsteValor = CASE WHEN m.ValorConsulta IS NULL THEN ''no hay valor de consulta cargado para ese nivel: no se puede calcular, no lo inventes'' WHEN m.NivelAplicado > m.NivelCliente THEN ''este prestador es de nivel '' + CONVERT(varchar(4), m.NivelAplicado) + '' y su plan es de nivel '' + CONVERT(varchar(4), m.NivelCliente) + '': por eso le cubren el '' + CONVERT(varchar(10), m.PctAplicado) + '' por ciento en vez del '' + CONVERT(varchar(10), CONVERT(decimal(6,2), m.PctBase)) + '' por ciento habitual'' WHEN m.Vigente = 0 THEN ''este prestador NO tiene convenio vigente: pagaria mas de lo que dice aqui'' ELSE ''su plan cubre el '' + CONVERT(varchar(10), m.PctAplicado) + '' por ciento de la consulta'' END, OjoAntesDeContestar = ''Esta es la regla GENERAL del contrato. Si copago_del_prestador devolvio un copago negociado con este mismo prestador, manda ese y no este.'' FROM matriz m ORDER BY m.Vigente DESC, m.NombrePrestador"}';

UPDATE dbo.OPAITool
   SET Description = @desc, InputSchema = @schema, BindingConfig = @binding, IsActive = 1
 WHERE Code = @code;

SELECT t.Code, Json = ISJSON(CONVERT(nvarchar(max), t.BindingConfig)),
       Schema_ok = ISJSON(CONVERT(nvarchar(max), t.InputSchema)),
       PlanesNuevos = CASE WHEN CONVERT(nvarchar(max), t.BindingConfig) LIKE N'%INC01%THEN 0 ELSE 1%' THEN 'SI' ELSE 'NO' END,
       XprSup       = CASE WHEN CONVERT(nvarchar(max), t.BindingConfig) LIKE N'%XPR-SUP%' THEN 'SI' ELSE 'NO' END,
       SmartPlan    = CASE WHEN CONVERT(nvarchar(max), t.BindingConfig) LIKE N'%EmpresaPortal%' THEN 'SI' ELSE 'NO' END
  FROM dbo.OPAITool t WHERE t.Code = @code;
