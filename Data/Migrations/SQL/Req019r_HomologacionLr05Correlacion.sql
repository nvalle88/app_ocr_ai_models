/* =============================================================================
   REQ-019r — El código sugerido sale de Lr05 (BD Salud) y la correlación de Lr46
   -----------------------------------------------------------------------------
   QUÉ CAMBIA
     El código de un procedimiento deja de venir de una tabla sembrada a mano y
     pasa a salir del catálogo REAL:

       Salud.dbo.Lr05Procedimientos             37.448  catálogo maestro
       Salud.dbo.Lr46CorrelacionDXProcedimiento 583.590 correlación dx ↔ proc.

     (conexión nueva "SaludProcedimientos" = SQLMIGRACION / salud, solo lectura
      con UsrPrestadores. Lr05 se modificó el 2026-08-21: el catálogo está vivo.)

   LAS TRES SITUACIONES DE CORRELACIÓN — y por qué son tres y no dos
     CORRELACIONA     medicina: hay fila para el dx con Probabilidad >= 70.
                      procedimiento: hay fila activa para el dx.
     NO_CORRELACIONA  el procedimiento SÍ está en Lr46 (para otros dx) pero no
                      para este; o es medicina y la probabilidad quedó bajo 70.
     SIN_VALIDAR      el procedimiento no aparece en Lr46 bajo NINGÚN dx.

     Medido en la base, esto no es teórico:
       MEDICINA (A010)  10.037 en catálogo -> 5.167 con correlación (51%)
       PROCEDIMIENTO    27.411 en catálogo -> 3.243 con correlación (12%)
     Es decir: al 88% de los procedimientos NO se les puede validar correlación.
     Si la ausencia se tratara como "no correlaciona", se negarían gastos
     legítimos en masa. De ahí el tercer estado.

   UMBRALES ADMINISTRABLES
     La tabla CatalogoBeneficioCorrelacion define, por CodigoBeneficio de Lr05,
     si es medicina y con qué probabilidad mínima se considera correlacionado.
     Se siembra A010 = medicina / 70 (la regla de farmacia: 70 incluido a 100) y
     un default 'DEFAULT' = procedimiento / 1 (presencia con probabilidad > 0).
     Si Operaciones decide que para procedimientos basta la PRESENCIA aunque la
     probabilidad sea 0, es un UPDATE de una fila (Umbral = 0), sin desplegar.

   GOTCHA DEL DIAGNÓSTICO
     En Lr46 el CIE-10 va SIN punto y a menudo TRUNCADO a 3 caracteres: K58.0
     está como 'K58', no 'K580'. Hay que buscar las dos formas.

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
   1) Resultado de la homologación y de la correlación, por procedimiento
   --------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.DocumentoProcedimiento', 'NumeroProcedimiento') IS NULL
BEGIN
    ALTER TABLE dbo.DocumentoProcedimiento ADD
        -- Homologación contra Lr05
        NumeroProcedimiento   int            NULL,   -- Lr05.NumeroProcedimiento (la llave real)
        NombreLr05            varchar(400)   NULL,   -- Lr05.NombreEspanol, copiado para el histórico
        CodigoBeneficio       varchar(10)    NULL,   -- A010 = medicina, H001 honorarios, A004 imagen...
        EsMedicina            bit            NULL,
        ScoreHomologacion     decimal(5,3)   NULL,   -- F1 del emparejamiento de texto (0..1)
        HomologacionAmbigua   bit            NULL,   -- hubo empate: el analista debe mirar
        -- Correlación contra Lr46
        EstadoCorrelacion     varchar(20)    NULL,   -- CORRELACIONA | NO_CORRELACIONA | SIN_VALIDAR
        CorrelacionProb       int            NULL,   -- Probabilidad 0..100 de Lr46
        CorrelacionDx         varchar(10)    NULL,   -- el dx con el que correlacionó (como está en Lr46)
        CorrelacionConfirmada bit            NULL,   -- Lr46.EsConfirmado
        UmbralAplicado        int            NULL;   -- qué umbral se usó (auditabilidad)
END
GO

SET QUOTED_IDENTIFIER ON;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DocumentoProcedimiento_Lr05')
    CREATE INDEX IX_DocumentoProcedimiento_Lr05
        ON dbo.DocumentoProcedimiento (NumeroProcedimiento) WHERE NumeroProcedimiento IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DocumentoProcedimiento_Correlacion')
    CREATE INDEX IX_DocumentoProcedimiento_Correlacion
        ON dbo.DocumentoProcedimiento (EstadoCorrelacion) WHERE EstadoCorrelacion IS NOT NULL;
GO

/* ---------------------------------------------------------------------------
   2) Umbrales de correlación por familia de beneficio (administrable)
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.CatalogoBeneficioCorrelacion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CatalogoBeneficioCorrelacion
    (
        CodigoBeneficio     varchar(10)   NOT NULL,   -- 'DEFAULT' = el resto
        Descripcion         varchar(200)  NULL,
        EsMedicina          bit           NOT NULL,
        -- Probabilidad MÍNIMA (inclusive) de Lr46 para considerar que correlaciona.
        -- 70 = la regla de farmacia (70 a 100). 1 = basta que exista con prob > 0.
        -- 0 = basta la PRESENCIA de la fila, sin importar la probabilidad.
        UmbralProbabilidad  int           NOT NULL,
        IsActive            bit           NOT NULL CONSTRAINT DF_CatBenCorr_Active DEFAULT (1),
        CreatedDate         datetime      NOT NULL CONSTRAINT DF_CatBenCorr_Created DEFAULT (GETUTCDATE()),
        CONSTRAINT PK_CatalogoBeneficioCorrelacion PRIMARY KEY (CodigoBeneficio)
    );
END
GO

;WITH src (CodigoBeneficio, Descripcion, EsMedicina, UmbralProbabilidad) AS (
    SELECT * FROM (VALUES
     ('A010', 'Medicina / farmacia — regla de correlacion 70 a 100', 1, 70),
     ('A003', 'Laboratorio clinico',                                 0,  1),
     ('A004', 'Imagen',                                              0,  1),
     ('A005', 'Procedimientos / paquetes PMF',                       0,  1),
     ('A002', 'Consulta y psicologia',                               0,  1),
     ('A037', 'Terapias psicologicas',                               0,  1),
     ('H001', 'Honorarios medicos',                                  0,  1),
     ('DEFAULT', 'Cualquier otro beneficio: procedimiento',          0,  1)
    ) AS v (CodigoBeneficio, Descripcion, EsMedicina, UmbralProbabilidad)
)
INSERT INTO dbo.CatalogoBeneficioCorrelacion (CodigoBeneficio, Descripcion, EsMedicina, UmbralProbabilidad)
SELECT s.CodigoBeneficio, s.Descripcion, s.EsMedicina, s.UmbralProbabilidad
FROM src s
WHERE NOT EXISTS (SELECT 1 FROM dbo.CatalogoBeneficioCorrelacion c
                  WHERE c.CodigoBeneficio = s.CodigoBeneficio);
GO

/* ---------------------------------------------------------------------------
   Verificación
   --------------------------------------------------------------------------- */
SELECT 'columnas Lr05/Lr46' AS Objeto,
       CASE WHEN COL_LENGTH('dbo.DocumentoProcedimiento','NumeroProcedimiento') IS NOT NULL
                 AND COL_LENGTH('dbo.DocumentoProcedimiento','EstadoCorrelacion') IS NOT NULL
                 AND COL_LENGTH('dbo.DocumentoProcedimiento','UmbralAplicado') IS NOT NULL
            THEN 'OK' ELSE 'FALTA' END AS Estado
UNION ALL
SELECT 'CatalogoBeneficioCorrelacion (filas)',
       CONVERT(varchar(20), (SELECT COUNT(*) FROM dbo.CatalogoBeneficioCorrelacion))
UNION ALL
SELECT 'umbral medicina A010',
       CONVERT(varchar(20), (SELECT UmbralProbabilidad FROM dbo.CatalogoBeneficioCorrelacion WHERE CodigoBeneficio='A010'))
UNION ALL
SELECT 'indices nuevos (esperados 2)',
       CONVERT(varchar(20), (SELECT COUNT(*) FROM sys.indexes
                             WHERE name IN ('IX_DocumentoProcedimiento_Lr05',
                                            'IX_DocumentoProcedimiento_Correlacion')));
GO
