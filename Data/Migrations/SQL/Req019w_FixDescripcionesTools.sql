/* =============================================================================
   REQ-019w — Arreglo: OPAITool.Description es nvarchar(500) y truncaba en silencio
   -----------------------------------------------------------------------------
   QUE PASO
     Los MERGE de Req019p/u/v traian descripciones de 700-900 caracteres. La
     columna Description es nvarchar(500), asi que el UPDATE reventaba con
     "String or binary data would be truncated. The statement has been
     terminated." — pero como venia despues de un INSERT exitoso, el script
     terminaba con rc=0 y PARECIA aplicado.

     Resultado: cinco tools se quedaron con la descripcion CORTA de la rama
     INSERT (VersionNumber = 1):
        buscar_medicina_catalogo_cf     143 chars
        consultar_beneficio_convenio    101
        consultar_coberturas_convenio   164
        resolver_convenio_por_ruc       177
        validar_medicina_vademecum      129

   POR QUE IMPORTA Y NO ES COSMETICO
     Description es lo que LEE EL MODELO para decidir cuando y como usar la
     tool. Con la version corta se perdieron justo los matices que evitan
     conclusiones falsas: los TRES estados del vademecum, el aviso de que A002
     es MONTO y no porcentaje, y que un RUC sin convenio significa FUERA DE RED
     y no "faltan datos".

   QUE HACE ESTE SCRIPT
     Reescribe esas cinco descripciones en <= 500 caracteres SIN perder el
     matiz, arregla la query de buscar_medicina_catalogo_cf (que tambien se
     quedo sin las llaves hacia Lr05), y verifica al final que ninguna
     descripcion pase de 500.

   LECCION PARA LOS PROXIMOS SEED DE TOOLS
     Description <= 500. Si hace falta mas contexto, va en el prompt del agente
     o en un OPAIPrompt apilado, no aqui.

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

/* -- 1) resolver_convenio_por_ruc ------------------------------------------ */
UPDATE dbo.OPAITool
SET Description = N'PASO 1 del flujo de convenio: convierte el RUC del prestador que EMITE la factura en su NumeroConvenio, y valida contra la base que ese RUC existe. Devuelve nombre, tipo, estado y vigencia. SI NO DEVUELVE FILAS el prestador NO tiene convenio con Saludsa: esta FUERA DE RED, que es una conclusion valida y distinta de "faltan datos". Con el NumeroConvenio se llama despues a consultar_coberturas_convenio y a validar_medicina_vademecum.',
    VersionNumber = ISNULL(VersionNumber, 0) + 1
WHERE Code = 'resolver_convenio_por_ruc';
GO

/* -- 2) consultar_coberturas_convenio -------------------------------------- */
UPDATE dbo.OPAITool
SET Description = N'PASO 2: los PORCENTAJES de cobertura negociados con ese prestador (tabla convenio_valores). Trae por separado medicina de MARCA y medicina GENERICA -que es lo que hay que distinguir en un reembolso de medicina-, mas laboratorio, imagenes, procedimientos y terapias, y el fee de consulta en DOLARES. Los porcentajes vienen 0-100. Un convenio puede tener varias listas y productos: si conoces el producto (COR, IND, POO), pasalo para acotar.',
    VersionNumber = ISNULL(VersionNumber, 0) + 1
WHERE Code = 'consultar_coberturas_convenio';
GO

/* -- 3) consultar_beneficio_convenio --------------------------------------- */
UPDATE dbo.OPAITool
SET Description = N'PASO 3 (detalle fino): el beneficio negociado plan por plan. CUIDADO: EsPorcentaje NO siempre es 1. A010 (medicina marca) y A011 (generica) vienen como PORCENTAJE, pero A002 (consultas medicas) viene como MONTO en dolares -12,00 en un convenio real-. Usa la columna ValorLegible, que ya resuelve esa diferencia, y no interpretes Valor por tu cuenta. Un convenio puede tener mas de cien planes: acota con codigoPlan o codigoBeneficio.',
    VersionNumber = ISNULL(VersionNumber, 0) + 1
WHERE Code = 'consultar_beneficio_convenio';
GO

/* -- 4) buscar_medicina_catalogo_cf: descripcion Y query ------------------- */
DECLARE @sqlMed nvarchar(max) = N'
SELECT TOP (25)
       m.CodigoMedicinaSaludsa,
       m.Descripcion,
       m.PrincipioActivo,
       m.TipoProducto,
       m.CodigoBeneficio,
       m.TipoTratamiento,
       m.EsContinuo,
       m.AplicaIva,
       m.Iva,
       m.Activo,
       -- Llaves hacia el catalogo de procedimientos Lr05, para encadenar con la
       -- correlacion diagnostico-procedimiento (Lr46) sin otra consulta:
       m.CodigoMedicinaSaludsa           AS CodigoHarvardLr05,
       m.CodigoProcedimiento             AS NumeroProcedimientoLr05,
       m.CodigoProcedimientoNuevosPlanes AS CodigoLiquidacionNuevosPlanes
FROM Saludsa.CreditoFarmacia.CFMedicina m WITH (NOLOCK)
WHERE m.Activo = 1
  AND (
        (@codigoMedicinaSaludsa IS NOT NULL AND m.CodigoMedicinaSaludsa = @codigoMedicinaSaludsa)
     OR (@texto IS NOT NULL AND m.Descripcion     LIKE ''%'' + @texto + ''%'')
     OR (@texto IS NOT NULL AND m.PrincipioActivo LIKE ''%'' + @texto + ''%'')
      )
ORDER BY CASE WHEN m.Descripcion LIKE @texto + ''%'' THEN 0 ELSE 1 END, m.Descripcion';

UPDATE dbo.OPAITool
SET BindingConfig = (SELECT 'SaludsaCreditoFarmacia' AS connection, 25 AS maxRows, @sqlMed AS query
                     FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
    Description = N'Catalogo de medicinas de Saludsa (CFMedicina, 6.711 activas). Del nombre en la factura devuelve el CodigoMedicinaSaludsa, la descripcion oficial, el PRINCIPIO ACTIVO y si es de MARCA o GENERICO (TipoProducto = CodigoBeneficio: A010 marca, A011 generico), ademas de AGUDO/Cronico/Mixto y si es continuo. Devuelve tambien CodigoHarvardLr05 y NumeroProcedimientoLr05 para encadenar con la correlacion Lr46. Es el paso previo a validar_medicina_vademecum.',
    VersionNumber = ISNULL(VersionNumber, 0) + 1
WHERE Code = 'buscar_medicina_catalogo_cf';
GO

/* -- 5) validar_medicina_vademecum ----------------------------------------- */
UPDATE dbo.OPAITool
SET Description = N'INFORMATIVA, NUNCA BLOQUEANTE. Dice si una medicina esta en el vademecum del convenio. EstadoVademecum tiene TRES valores y hay que respetar la diferencia: EN_VADEMECUM (esta y activa); NO_EN_VADEMECUM (el convenio SI tiene vademecum y esta medicina no aparece); CONVENIO_SIN_VADEMECUM (ese convenio no tiene vademecum cargado, solo 15 lo tienen, asi que NO SE PUEDE AFIRMAR NADA). Reportar el tercero como "no esta en vademecum" seria una negativa falsa.',
    VersionNumber = ISNULL(VersionNumber, 0) + 1
WHERE Code = 'validar_medicina_vademecum';
GO

/* ---------------------------------------------------------------------------
   Verificacion: ninguna descripcion truncada, la query con las llaves nuevas
   --------------------------------------------------------------------------- */
SELECT Code,
       LEN(Description) AS LargoDesc,
       CASE WHEN LEN(Description) > 500 THEN 'SE VA A TRUNCAR' ELSE 'OK' END AS Cabe,
       VersionNumber AS Ver
FROM dbo.OPAITool
WHERE Code IN ('resolver_convenio_por_ruc','consultar_coberturas_convenio','consultar_beneficio_convenio',
               'buscar_medicina_catalogo_cf','validar_medicina_vademecum','buscar_medicina_prestador_vademecum',
               'historial_reembolsos_cliente_bd','consultar_liquidacion_sobre_bd','buscar_factura_repetida_bd')
ORDER BY Code;

SELECT 'llaves Lr05 en la query' AS q,
       CASE WHEN CHARINDEX('CodigoHarvardLr05', BindingConfig) > 0 THEN 'OK' ELSE 'FALTA' END AS Estado
FROM dbo.OPAITool WHERE Code = 'buscar_medicina_catalogo_cf';

SELECT 'descripciones que no caben en 500' AS q, COUNT(*) AS n
FROM dbo.OPAITool WHERE IsActive = 1 AND LEN(Description) > 500;
GO
