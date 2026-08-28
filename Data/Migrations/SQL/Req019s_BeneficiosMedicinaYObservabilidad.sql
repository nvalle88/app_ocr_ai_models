/* =============================================================================
   REQ-019s — Beneficios correctos (A002 consultas, A010 marca, A011 genérica)
              + limpieza de observabilidad de las tools
   -----------------------------------------------------------------------------
   1) BENEFICIOS — verificado contra Salud.dbo.Lr05Procedimientos (2026-08-21)

        A002    237 filas   consultas médicas y valoraciones
                            ("VISITA PARA EVALUACION Y MANEJO DE UN PACIENTE…")
        A010 10.037 filas   MEDICINA DE MARCA
                            (#7460 "MEDICINA", #7859 "MEDICAMENTOS FYBECA",
                             #10576 "MEDICINA ONCOLOGICA")
        A011      3 filas   MEDICINA GENÉRICA
                            (#9425 "MEDICINA GENERICA",
                             #11937 "IVA medicina generica ODA")

      Es el propio catálogo el que separa marca de genérica por beneficio: no
      hace falta adivinarlo del nombre del producto. (CatalogoProductosMedicinas,
      que tendría Producto/Molecula, está VACÍA: 0 filas.)

      Consecuencia para la correlación: la regla de farmacia (Probabilidad >= 70)
      aplica a A010 **y** a A011 — las dos son medicina. Antes solo A010 estaba
      marcado como medicina, así que la genérica se validaba con el umbral de
      procedimiento. Eso es lo que hacía que algunos casos no resolvieran bien.

   2) OBSERVABILIDAD — consultar_coberturas_plan_prestador se desactiva

      Da 404 en TODAS las rutas probadas contra ServicioPrestador:
        /CoberturasPlan  /api/CoberturasPlan  /api/prestador/CoberturasPlan
        /api/Prestador/CoberturasPlan  /api/coberturas/CoberturasPlan
      El endpoint no existe en ese servicio. Ofrecérsela al agente solo gasta
      una llamada y ensucia el diagnóstico: una tool rota no debe estar en el
      catálogo. La cobertura del plan se obtiene con consultar_coberturas_plan
      (Armonix /api/IAConsultas/BuscarCoberturasIA), que responde 200.

      Para reactivarla: corregir BindingConfig.path con la ruta real y poner
      IsActive = 1 en OPAITool y IsEnabled = 1 en OPAIModelTool.

   LO QUE NO SE PUDO RESOLVER Y HAY QUE DECIRLO
      El PORCENTAJE de cobertura de medicina marca vs genérica del plan
      INDIVIDUAL no está disponible con lo cableado hoy:
        · Pr52CatalogosPlanes SÍ tiene PorcentajeMedicinaMarca/Generica, pero es
          el catálogo de CRÉDITO FARMACIA EMPRESARIAL (198 filas, planes
          "PLAN SALUD EMPRESARIAL…", con CodigoPrestador de cadena y los
          porcentajes en 0.00). No corresponde a un plan individual como N4-D-C.
        · BuscarCoberturasIA devuelve 25 coberturas para N4-D-C y NINGUNA es de
          medicina (son odontológicas por accidente, maternidad, recién nacido,
          discapacidad…). El % de medicina no viene por ahí.
      Falta identificar la fuente real del % de medicina del plan individual.

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
   1) Beneficios: A010 y A011 son medicina (umbral 70); A002 son consultas
   --------------------------------------------------------------------------- */
MERGE dbo.CatalogoBeneficioCorrelacion AS tgt
USING (SELECT * FROM (VALUES
    ('A010', 'Medicina de MARCA — regla de farmacia: correlacion 70 a 100',   1, 70),
    ('A011', 'Medicina GENERICA — misma regla de farmacia: 70 a 100',         1, 70),
    ('A002', 'Consultas medicas y valoraciones',                              0,  1),
    ('A003', 'Laboratorio clinico',                                           0,  1),
    ('A004', 'Imagen',                                                        0,  1),
    ('A005', 'Procedimientos / paquetes PMF',                                 0,  1),
    ('A037', 'Terapias psicologicas',                                         0,  1),
    ('H001', 'Honorarios medicos',                                            0,  1),
    ('DEFAULT', 'Cualquier otro beneficio: procedimiento',                    0,  1)
) AS v (CodigoBeneficio, Descripcion, EsMedicina, UmbralProbabilidad)) AS src
   ON tgt.CodigoBeneficio = src.CodigoBeneficio
WHEN MATCHED THEN UPDATE SET
    tgt.Descripcion        = src.Descripcion,
    tgt.EsMedicina         = src.EsMedicina,
    tgt.UmbralProbabilidad = src.UmbralProbabilidad,
    tgt.IsActive           = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (CodigoBeneficio, Descripcion, EsMedicina, UmbralProbabilidad, IsActive)
    VALUES (src.CodigoBeneficio, src.Descripcion, src.EsMedicina, src.UmbralProbabilidad, 1);
GO

/* Se guarda además si es marca o genérica, que es información distinta de
   "es medicina": el analista necesita verla y el % de cobertura difiere. */
IF COL_LENGTH('dbo.CatalogoBeneficioCorrelacion', 'TipoMedicina') IS NULL
    ALTER TABLE dbo.CatalogoBeneficioCorrelacion ADD TipoMedicina varchar(20) NULL;
GO

UPDATE dbo.CatalogoBeneficioCorrelacion SET TipoMedicina = 'MARCA'    WHERE CodigoBeneficio = 'A010';
UPDATE dbo.CatalogoBeneficioCorrelacion SET TipoMedicina = 'GENERICA' WHERE CodigoBeneficio = 'A011';
GO

IF COL_LENGTH('dbo.DocumentoProcedimiento', 'TipoMedicina') IS NULL
    ALTER TABLE dbo.DocumentoProcedimiento ADD TipoMedicina varchar(20) NULL;
GO

/* ---------------------------------------------------------------------------
   2) Observabilidad: fuera del catálogo la tool que da 404
   --------------------------------------------------------------------------- */
UPDATE dbo.OPAITool
SET IsActive    = 0,
    Description = N'[DESACTIVADA 2026-08-21] El endpoint no existe: 404 en /CoberturasPlan, '
                + N'/api/CoberturasPlan, /api/prestador/CoberturasPlan, /api/Prestador/CoberturasPlan y '
                + N'/api/coberturas/CoberturasPlan sobre ServicioPrestador. Usar consultar_coberturas_plan '
                + N'(Armonix BuscarCoberturasIA), que responde 200. Para reactivarla: corregir el path real '
                + N'en BindingConfig y volver a poner IsActive = 1 aqui e IsEnabled = 1 en OPAIModelTool.'
WHERE Code = 'consultar_coberturas_plan_prestador';

UPDATE dbo.OPAIModelTool
SET IsEnabled = 0
WHERE ToolCode = 'consultar_coberturas_plan_prestador';
GO

/* ---------------------------------------------------------------------------
   Verificación
   --------------------------------------------------------------------------- */
SELECT 'beneficios' AS q, CodigoBeneficio, EsMedicina, UmbralProbabilidad,
       ISNULL(TipoMedicina, '-') AS TipoMedicina, LEFT(Descripcion, 46) AS Descripcion
FROM dbo.CatalogoBeneficioCorrelacion
ORDER BY CASE WHEN EsMedicina = 1 THEN 0 ELSE 1 END, CodigoBeneficio;

SELECT 'tools activas' AS q, COUNT(*) AS n FROM dbo.OPAITool WHERE IsActive = 1;

SELECT 'tool 404 desactivada' AS q,
       CASE WHEN (SELECT IsActive FROM dbo.OPAITool WHERE Code = 'consultar_coberturas_plan_prestador') = 0
            THEN 'OK' ELSE 'SIGUE ACTIVA' END AS Estado;
GO
