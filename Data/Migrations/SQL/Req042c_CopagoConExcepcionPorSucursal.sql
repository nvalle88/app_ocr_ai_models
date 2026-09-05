/* =============================================================================
   REQ-042c - El copago negociado ignoraba la excepcion por sucursal
   -----------------------------------------------------------------------------
   Segunda mitad de "sigue sin resolver el precio de consulta del medico". La
   primera -que para casi ningun medico habia precio- se arregla en REQ-042b con
   precio_consulta_medico. Esta es la otra: cuando SI habia precio, podia estar
   equivocado.

   -- Lo que faltaba --------------------------------------------------------
   copago_del_prestador leia BeneficioConvenio y nada mas. Pero api-prestador,
   antes de ese valor, mira ConvenioPlanExcepcion, que lleva el copago por
   CodigoSucursal. Esa tabla no se estaba consultando.

   Medido hoy en bdd_prestadores: 56.736 filas de excepcion, TODAS con
   CodigoSucursal, 7.932 de ellas del beneficio de consulta A002 en 38
   convenios. Y donde cambia el importe, cambia de verdad: 5.112 casos con
   diferencia de un dolar o mas contra el valor base, en 17 convenios, con un
   maximo de 85 dolares.

   El caso que lo demuestra, Veris (11715), plan IND / PRO150K-1500-WALLET v29:

       atencion normal                      US$  5,00   <- lo unico que se decia
       modalidad Para Mi (PMF)              US$ 90,00
       mientras no cubra el deducible (DNS) US$  0,00

   Se le estaba diciendo cinco dolares a quien iba a pagar noventa.

   -- Los dos codigos, que no son lo mismo ----------------------------------
   CodigoSucursal solo tiene dos valores, y encima significan cosas distintas:
   PMF es la modalidad Para Mi -Veris y Para Mi son el MISMO convenio y se
   distinguen por aqui- y DNS no es una sucursal sino "deducible no superado".
   Comparten columna. Por eso la salida no dice "sucursal X" sino en que CASO
   aplica cada valor.

   -- Y ya no se elige una fila al azar -------------------------------------
   BeneficioConvenio tiene 4.664 casos con el mismo beneficio repetido dentro
   del mismo plan. api-prestador hace FirstOrDefault sobre un SELECT sin ORDER
   BY, que es la causa conocida de que "sale un valor y despues otro". Aqui se
   desempata siempre igual: version mas alta, y a igualdad, id mas alto.

   -- Lo que esta tool NO hace ----------------------------------------------
   No dice el precio cuando no hay nada negociado: para eso esta
   precio_consulta_medico. Vacia aqui significa "no hay copago negociado", no
   "no se sabe", y el prompt ya sabe seguir por el otro lado.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @desc nvarchar(1000) = N'LO QUE PAGA EL AFILIADO por atenderse con un prestador que tiene copago NEGOCIADO: el copago de consulta en dolares (A002/A007) y el porcentaje que cubre el plan (A003). Devuelve TODOS los casos, porque el mismo prestador puede tener varios: la atencion normal, la modalidad Para Mi y el tramo sin deducible cubierto. Lee EnQueCaso antes de dar una cifra. Si vuelve VACIA no es un fallo: ese prestador no tiene copago negociado y entonces manda precio_consulta_medico.';
DECLARE @binding nvarchar(max) = N'{"connection": "SaludPrestadores", "maxRows": 40, "query": "WITH pedidos AS ( SELECT Convenio = TRY_CAST(LTRIM(RTRIM(value)) AS int) FROM STRING_SPLIT(@numeroConvenio, '','') WHERE TRY_CAST(LTRIM(RTRIM(value)) AS int) IS NOT NULL ), ver AS (SELECT V = TRY_CAST(COALESCE(NULLIF(@version,''''), NULLIF(@versionPlan,'''')) AS int)), base AS ( SELECT cp.NumeroConvenio, bc.CodigoBeneficio, bc.Valor, bc.EsPorcentaje, rn = ROW_NUMBER() OVER (PARTITION BY cp.NumeroConvenio, bc.CodigoBeneficio ORDER BY cp.VersionPlan DESC, cp.IdConvenioPlan DESC) FROM dbo.ConvenioPlan cp WITH (NOLOCK) JOIN dbo.BeneficioConvenio bc WITH (NOLOCK) ON bc.IdConvenioPlan = cp.IdConvenioPlan CROSS JOIN ver WHERE bc.EstadoActivo = 1 AND (cp.FechaFinVigencia IS NULL OR cp.FechaFinVigencia >= CAST(GETDATE() AS date)) AND cp.NumeroConvenio IN (SELECT Convenio FROM pedidos) AND (@codigoProducto IS NULL OR LEN(@codigoProducto) = 0 OR cp.CodigoProducto = @codigoProducto) AND (@codigoPlan IS NULL OR LEN(@codigoPlan) = 0 OR cp.CodigoPlan = @codigoPlan) AND (ver.V IS NULL OR cp.VersionPlan = ver.V) AND (bc.CodigoBeneficio = @codigoBeneficio OR (LEN(ISNULL(@codigoBeneficio,'''')) = 0 AND bc.CodigoBeneficio IN (''A002'',''A007'',''A003''))) ), exc AS ( SELECT e.NumeroConvenio, e.CodigoBeneficio, e.Valor, e.EsPorcentaje, e.CodigoSucursal, rn = ROW_NUMBER() OVER (PARTITION BY e.NumeroConvenio, e.CodigoBeneficio, e.CodigoSucursal ORDER BY e.VersionPlan DESC, e.Id DESC) FROM dbo.ConvenioPlanExcepcion e WITH (NOLOCK) CROSS JOIN ver WHERE e.EstadoActivo = 1 AND (e.FechaFinVigencia IS NULL OR e.FechaFinVigencia >= CAST(GETDATE() AS date)) AND e.NumeroConvenio IN (SELECT Convenio FROM pedidos) AND (@codigoProducto IS NULL OR LEN(@codigoProducto) = 0 OR e.CodigoProducto = @codigoProducto) AND (@codigoPlan IS NULL OR LEN(@codigoPlan) = 0 OR e.CodigoPlan = @codigoPlan) AND (ver.V IS NULL OR e.VersionPlan = ver.V) AND (e.CodigoBeneficio = @codigoBeneficio OR (LEN(ISNULL(@codigoBeneficio,'''')) = 0 AND e.CodigoBeneficio IN (''A002'',''A007'',''A003''))) ), todo AS ( SELECT Convenio = b.NumeroConvenio, Beneficio = b.CodigoBeneficio, Valor = b.Valor, EsPct = b.EsPorcentaje, Caso = CONVERT(varchar(10), NULL), Orden = 1 FROM base b WHERE b.rn = 1 UNION ALL SELECT e.NumeroConvenio, e.CodigoBeneficio, e.Valor, e.EsPorcentaje, e.CodigoSucursal, 0 FROM exc e WHERE e.rn = 1 ) SELECT TOP 40 Convenio, Beneficio, QueEsEsteValor = CASE WHEN EsPct = 1 THEN ''porcentaje de cobertura'' ELSE ''lo que paga el afiliado, en dolares'' END, UstedPaga = CASE WHEN EsPct = 1 THEN NULL WHEN Valor < 0.01 THEN 0 ELSE CONVERT(decimal(10,2), Valor) END, PorcentajeCubierto = CASE WHEN EsPct = 1 THEN CONVERT(decimal(6,2), Valor) END, EnQueCaso = CASE WHEN Caso IS NULL THEN ''en la atencion normal, en cualquier sucursal'' WHEN Caso = ''PMF'' THEN ''SOLO en la modalidad Para Mi -la consulta virtual o la del local-'' WHEN Caso = ''DNS'' THEN ''SOLO mientras no haya cubierto su deducible del ano'' ELSE ''SOLO en el caso '' + Caso END, Nota = CASE WHEN EsPct = 0 AND Valor < 0.01 THEN ''sin copago: no paga nada por la consulta'' END FROM todo ORDER BY Convenio, Beneficio, Orden DESC, Caso"}';

UPDATE dbo.OPAITool
   SET Description = @desc, BindingConfig = @binding, IsActive = 1
 WHERE Code = N'copago_del_prestador';

/* Y el prompt: la cascada de los tres escalones. */
DECLARE @ancla nvarchar(100) = N'## Una sola pasada';
DECLARE @seccion nvarchar(max) = N'## Cuanto cuesta ver a un medico

Es la pregunta mas frecuente y hasta ahora se contestaba mal, asi que va con
detalle. **El precio se arma en tres escalones y gana siempre el mas especifico
que exista:**

1. **`copago_del_prestador`** — lo NEGOCIADO con ese prestador. Si hay, manda.
   Ojo: puede devolver VARIAS filas para el mismo sitio, y no son alternativas
   sino casos distintos. Lee `EnQueCaso`. En Veris, por ejemplo, conviven
   **US$ 5,00** en la atencion normal, **US$ 90,00** en la modalidad Para Mi y
   **US$ 0,00** mientras no haya cubierto el deducible. Dar el primero que
   salga es equivocarse en 85 dolares.
2. **`precio_consulta_medico`** — la regla general del contrato, que existe
   para CUALQUIER prestador. Precio de la consulta segun el nivel del
   prestador, menos el porcentaje del plan.
3. Si ninguna de las dos contesta, **dilo**. Nunca una cifra a ojo: el afiliado
   se presenta con ella en el consultorio.

**Pidelas a las dos a la vez** y luego elige: la primera si trajo algo, la
segunda si no. Que `copago_del_prestador` vuelva vacia es lo normal —de 2.270
convenios con plan cargado solo 860 tienen copago negociado—, no es un error y
no se menciona.

### Como se dice

Tres cifras y la que importa destacada, con `md-cifras`: **lo que cuesta la
consulta**, **lo que cubre su plan** y **lo que pone usted**. Y siempre el
porque debajo, sobre todo cuando el numero sorprende:

> La consulta con el Dr. Coloma cuesta **US$ 41**. Su plan cubre el 80%, o sea
> US$ 33: **usted pone US$ 8**.

Si el prestador es de nivel superior al del plan, **esa es la explicacion y va
dicha**, porque es la que convierte 8 dolares en 47:

> En Veris la consulta cuesta US$ 80. Veris es nivel 8 y su plan es nivel 5, y
> por cada nivel de diferencia la cobertura baja: le cubren el 41% en vez del
> 80%. **Usted pone US$ 47.**

Y si el convenio no esta vigente, primero eso: pagaria la tarifa sin convenio,
que es mayor.

';

UPDATE dbo.Agent
   SET SystemPrompt = REPLACE(CONVERT(nvarchar(max), SystemPrompt), @ancla, @seccion + @ancla)
 WHERE Code = N'AGENTE_CHAT_CLIENTE'
   AND CONVERT(nvarchar(max), SystemPrompt) LIKE N'%' + @ancla + N'%'
   AND CONVERT(nvarchar(max), SystemPrompt) NOT LIKE N'%tres escalones%';

SELECT Code, Json = ISJSON(CONVERT(nvarchar(max), BindingConfig)),
       LeeLaExcepcion = CASE WHEN CONVERT(nvarchar(max), BindingConfig) LIKE N'%ConvenioPlanExcepcion%'
                             THEN 'SI' ELSE 'NO' END
  FROM dbo.OPAITool WHERE Code = N'copago_del_prestador';

SELECT Code, PromptCascada = CASE WHEN CONVERT(nvarchar(max), SystemPrompt) LIKE N'%tres escalones%'
                                  THEN 'SI' ELSE 'NO' END,
       Largo = LEN(CONVERT(nvarchar(max), SystemPrompt))
  FROM dbo.Agent WHERE Code = N'AGENTE_CHAT_CLIENTE';
