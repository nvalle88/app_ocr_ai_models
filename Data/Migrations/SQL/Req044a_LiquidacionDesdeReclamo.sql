/* =============================================================================
   REQ-044a - La liquidacion del sobre salia sin valores: tabla equivocada
   -----------------------------------------------------------------------------
   Nestor, sobre NA-2735345 (liquidado): el chat no pudo mostrar cuanto le
   reconocieron ni cuanto quedo a su cargo -"la liquidacion no me esta
   devolviendo los valores"-.

   Causa: consultar_liquidacion_sobre_bd leia bdd_Salud_Consultas.DetalleSobre,
   donde el sobre es solo la ENTRADA: ValorPresentadoDetalle 3.144,75 pero
   ValorConsultor NULL. La liquidacion de verdad no se escribe ahi: se hace en
   el RECLAMO. Medido en Salud.dbo.Lr02Reclamos (SQLCORPROD) por NumeroSobre:

       Reclamo 2133124257  estado 10 (liquidado y pagado)
       Presento     3.144,75
       Cubierto     3.109,99   <- lo que le reconocen / pagan
       No cubierto     34,76   <- lo que queda a su cargo
       Deducible / Copago 0 / 0
       Liquidado 27/04/2026 · Pagado 28/04/2026

   Ahora la tool lee Lr02Reclamos y devuelve ese desglose. La relacion cuadra:
   Presentado = Cubierto + NoCubierto (3144,75 = 3109,99 + 34,76).

   OJO conexion: la liquidacion esta en SaludReclamos (SQLCORPROD/Salud), NO en
   SaludConsultas -que es donde vive el sobre de entrada-.
   ============================================================================= */

IF DB_NAME() NOT IN ('db-nexus-test', 'db-nexus-aud')
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Migracion para db-nexus-test o db-nexus-aud. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @desc nvarchar(1000) = N'LA LIQUIDACION REAL de un sobre de reembolso: cuanto PRESENTO, cuanto le RECONOCEN (cubierto), cuanto le PAGARON, el deducible, el copago y lo que quedo A SU CARGO (no cubierto), con las fechas de liquidacion y pago. Sale del reclamo (Lr02Reclamos por NumeroSobre), no del sobre de entrada -ahi el valor liquidado esta vacio-. Usala para el desglose de la liquidacion. Si vuelve vacia, el sobre aun no se liquido como reclamo.';
DECLARE @binding nvarchar(max) = N'{"connection": "SaludReclamos", "maxRows": 20, "query": "SELECT TOP 20 r.NumeroSobre, r.NumeroReclamo, EstadoReclamo = r.EstadoReclamo, QueSignifica = CASE r.EstadoReclamo WHEN 10 THEN ''liquidado y pagado'' WHEN 27 THEN ''pagado'' WHEN 61 THEN ''aprobado, en cola de pago'' ELSE ''en proceso (estado '' + CONVERT(varchar(10), r.EstadoReclamo) + '')'' END, Presento = CONVERT(decimal(12,2), r.MontoPresentado), LeReconocen = CONVERT(decimal(12,2), r.MontoCubierto), LePagaron = CONVERT(decimal(12,2), r.MontoPagado), Deducible = CONVERT(decimal(12,2), r.MontoDeducible), Copago = CONVERT(decimal(12,2), r.MontoCopago), ACargoDelAfiliado = CONVERT(decimal(12,2), r.MontoNoCubierto), Bonificado = CONVERT(decimal(12,2), r.MontoBonificado), FechaLiquidacion = CONVERT(varchar(10), r.FechaLiquidacionReclamo, 23), FechaPago = CONVERT(varchar(10), r.FechaPagoReclamo, 23), ComoSeLee = ''Presento el total. Le RECONOCEN el MontoCubierto. Lo que quedo A SU CARGO es el NoCubierto. El deducible y el copago se descuentan de lo reconocido para llegar a lo que le PAGARON.'' FROM Salud.dbo.Lr02Reclamos r WITH (NOLOCK) WHERE r.NumeroSobre = @numeroSobre ORDER BY r.NumeroReclamo"}';

UPDATE dbo.OPAITool
   SET Description = @desc, BindingConfig = @binding, IsActive = 1
 WHERE Code = N'consultar_liquidacion_sobre_bd';

SELECT Code, Json = ISJSON(CONVERT(nvarchar(max), BindingConfig)),
       LeeReclamo = CASE WHEN CONVERT(nvarchar(max), BindingConfig) LIKE N'%Lr02Reclamos%' THEN 'SI' ELSE 'NO' END,
       Conexion = JSON_VALUE(CONVERT(nvarchar(max), BindingConfig), '$.connection')
  FROM dbo.OPAITool WHERE Code = N'consultar_liquidacion_sobre_bd';
