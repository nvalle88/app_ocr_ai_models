/* =============================================================================
   REQ-019v — Vademécum y catálogo de medicinas (Saludsa.CreditoFarmacia)
   -----------------------------------------------------------------------------
   EL MAPA, levantado de las FK reales de la base (no inferido):

     CFPrestador (29.549)            el producto como lo llama la FARMACIA
        │  (NumeroConvenio + CodigoMedicinaPrestador)
        ▼
     CFMedicinaPrestadorVademecum (29.282)   ◄── EL VADEMÉCUM
        │  CodigoMedicinaPrestador, NumeroConvenio, CodigoMedicinaSaludsa,
        │  CodigoVademecum, Activo
        ├──► CFMedicina (6.711)      catálogo de medicinas de Saludsa
        └──► CFVademecum (2)         tipos de vademécum (solo 'GENERAL' en uso)

   CFMedicina resuelve de una vez lo que antes no encontrábamos:
        TipoProducto     MARCA 5.248  /  GENERICO 1.461   (+2 N/A)
        CodigoBeneficio  A010 = MARCA  /  A011 = GENERICO  ← coincidencia 1:1
        PrincipioActivo  la molécula
        TipoTratamiento  AGUDO 3.804 / Crónico 1.894 / Mixto 1.011  + EsContinuo
        AplicaIva, Iva

   EL VADEMÉCUM ES INFORMATIVO, NO BLOQUEANTE — y hay razón dura para ello:
        solo 15 convenios tienen vademécum cargado (las cadenas de farmacia con
        crédito). Para cualquier otro prestador la respuesta correcta NO es
        "no está en vademécum" sino "este convenio no tiene vademécum cargado".
        Por eso la tool devuelve TRES estados y nunca dos.

   LAS LLAVES CONTRA Lr05 — medidas sobre las 6.711 medicinas activas
        CodigoMedicinaSaludsa  ──► Lr05.CodigoHarvard   6.702 (99,9%), y el
                                   98,3% cae en un beneficio de medicina
                                   (A010/A011). CodigoSalud da lo mismo:
                                   6.701. Los nombres coinciden literalmente
                                   (301208 PAZIDOL 1-2 SUSx500MGx1 en ambas).
                                   ESTA es la llave de negocio.
        CodigoProcedimiento    ──► Lr05.NumeroProcedimiento (la PK de Lr05)
                                   6.554 (97,7%), 98,4% de acierto semantico.
        CodigoProcNuevosPlanes ──► Lr05.CodigoHarvard   5 de 5 (100%): son los
                                   codigos de liquidacion de nuevos planes
                                   (304001 = MEDICINAS NO CONTINUAS GENERICAS).

        Corrección de una nota anterior de este mismo script: se habia escrito
        que CFMedicina.CodigoProcedimiento "no enlaza" con Lr05, generalizando
        desde los codigos 10455/10456/10457 (FINALIN, SINGRIPAL, FLEMISOL), que
        en Lr05 son "COLOCACION DE FERULA". Eran 3 de las 105 colisiones que
        existen sobre 6.554 coincidencias: la conclusion era falsa. Enlaza.

   Solo LECTURA. Idempotente y con guarda de base.
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
   TOOL 1 — buscar_medicina_catalogo_cf
   Del texto de la factura al código de medicina de Saludsa, con marca/genérica
   y principio activo. Es el paso previo a validar el vademécum.
   --------------------------------------------------------------------------- */
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
       -- Llaves hacia el catalogo de procedimientos Lr05, para poder encadenar
       -- con la correlacion diagnostico-procedimiento (Lr46) sin otra consulta:
       m.CodigoMedicinaSaludsa AS CodigoHarvardLr05,   -- = Lr05.CodigoHarvard (99,9%)
       m.CodigoProcedimiento   AS NumeroProcedimientoLr05,
       m.CodigoProcedimientoNuevosPlanes AS CodigoLiquidacionNuevosPlanes
FROM Saludsa.CreditoFarmacia.CFMedicina m WITH (NOLOCK)
WHERE m.Activo = 1
  AND (
        (@codigoMedicinaSaludsa IS NOT NULL AND m.CodigoMedicinaSaludsa = @codigoMedicinaSaludsa)
     OR (@texto IS NOT NULL AND m.Descripcion   LIKE ''%'' + @texto + ''%'')
     OR (@texto IS NOT NULL AND m.PrincipioActivo LIKE ''%'' + @texto + ''%'')
      )
ORDER BY CASE WHEN m.Descripcion LIKE @texto + ''%'' THEN 0 ELSE 1 END, m.Descripcion';

DECLARE @schMed nvarchar(max) = N'{
  "type": "object",
  "properties": {
    "texto": {
      "type": "string",
      "description": "Nombre del medicamento tal como aparece en la factura (o su principio activo). Usa una o dos palabras del nombre comercial, no la linea entera con dosis y presentacion."
    },
    "codigoMedicinaSaludsa": {
      "type": "string",
      "description": "Codigo exacto de la medicina, si ya lo tienes. Opcional."
    }
  },
  "required": []
}';

MERGE dbo.OPAITool AS tgt
USING (SELECT 'buscar_medicina_catalogo_cf' AS Code) AS src ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    tgt.Name          = N'buscar_medicina_catalogo_cf',
    tgt.Description   = N'Catalogo de medicinas de Saludsa (CFMedicina, 6.711 activas). Del nombre que aparece en la factura devuelve el CodigoMedicinaSaludsa, la descripcion oficial, el PRINCIPIO ACTIVO y, sobre todo, si es de MARCA o GENERICO (TipoProducto, que coincide 1:1 con CodigoBeneficio: A010 marca, A011 generico). Tambien dice si el tratamiento es AGUDO, Cronico o Mixto y si es continuo, y si aplica IVA. Es el PASO PREVIO para validar el vademecum: primero se obtiene el codigo de la medicina, despues se valida contra el convenio. Devuelve ademas las llaves hacia el catalogo de procedimientos: CodigoHarvardLr05 (el propio CodigoMedicinaSaludsa, que es Lr05.CodigoHarvard en el 99,9% de los casos) y NumeroProcedimientoLr05 (la PK de Lr05, 97,7%). Con NumeroProcedimientoLr05 puedes encadenar directo a la correlacion diagnostico-procedimiento de Lr46.',
    tgt.InputSchema   = @schMed,
    tgt.BindingType   = 'Sql',
    tgt.BindingConfig = (SELECT 'SaludsaCreditoFarmacia' AS connection, 25 AS maxRows, @sqlMed AS query FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
    tgt.IsActive      = 1,
    tgt.VersionNumber = ISNULL(tgt.VersionNumber, 0) + 1
WHEN NOT MATCHED THEN INSERT (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES ('buscar_medicina_catalogo_cf', N'buscar_medicina_catalogo_cf',
        N'Catalogo de medicinas de Saludsa: del nombre en la factura al CodigoMedicinaSaludsa, con principio activo y si es MARCA o GENERICO (A010/A011).',
        @schMed, 0, 'Sql',
        (SELECT 'SaludsaCreditoFarmacia' AS connection, 25 AS maxRows, @sqlMed AS query FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
        1, 1, GETUTCDATE());
GO

/* ---------------------------------------------------------------------------
   TOOL 2 — validar_medicina_vademecum
   INFORMATIVA. Tres estados, nunca dos.
   --------------------------------------------------------------------------- */
DECLARE @sqlVad nvarchar(max) = N'
SELECT TOP (20)
       @numeroConvenio                         AS NumeroConvenio,
       @codigoMedicinaSaludsa                  AS CodigoMedicinaSaludsaConsultado,
       tot.MedicinasEnVademecum,
       v.CodigoMedicinaPrestador,
       v.CodigoMedicinaSaludsa,
       v.CodigoVademecum,
       v.Activo                                AS ActivoEnVademecum,
       p.Nombre                                AS NombreEnLaFarmacia,
       m.Descripcion                           AS DescripcionSaludsa,
       m.TipoProducto,
       m.CodigoBeneficio,
       CASE
         WHEN tot.MedicinasEnVademecum = 0 THEN ''CONVENIO_SIN_VADEMECUM''
         WHEN v.CodigoMedicinaSaludsa IS NULL THEN ''NO_EN_VADEMECUM''
         WHEN v.Activo = 1 THEN ''EN_VADEMECUM''
         ELSE ''EN_VADEMECUM_INACTIVA''
       END                                     AS EstadoVademecum
FROM (SELECT COUNT(*) AS MedicinasEnVademecum
      FROM Saludsa.CreditoFarmacia.CFMedicinaPrestadorVademecum WITH (NOLOCK)
      WHERE NumeroConvenio = @numeroConvenio) tot
LEFT JOIN Saludsa.CreditoFarmacia.CFMedicinaPrestadorVademecum v WITH (NOLOCK)
       ON v.NumeroConvenio = @numeroConvenio
      AND v.CodigoMedicinaSaludsa = @codigoMedicinaSaludsa
LEFT JOIN Saludsa.CreditoFarmacia.CFPrestador p WITH (NOLOCK)
       ON p.NumeroConvenio = v.NumeroConvenio
      AND p.CodigoMedicinaPrestador = v.CodigoMedicinaPrestador
LEFT JOIN Saludsa.CreditoFarmacia.CFMedicina m WITH (NOLOCK)
       ON m.CodigoMedicinaSaludsa = v.CodigoMedicinaSaludsa';

DECLARE @schVad nvarchar(max) = N'{
  "type": "object",
  "properties": {
    "numeroConvenio": {
      "type": "string",
      "description": "NumeroConvenio del prestador, obtenido con resolver_convenio_por_ruc."
    },
    "codigoMedicinaSaludsa": {
      "type": "string",
      "description": "CodigoMedicinaSaludsa devuelto por buscar_medicina_catalogo_cf."
    }
  },
  "required": ["numeroConvenio", "codigoMedicinaSaludsa"]
}';

MERGE dbo.OPAITool AS tgt
USING (SELECT 'validar_medicina_vademecum' AS Code) AS src ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    tgt.Name          = N'validar_medicina_vademecum',
    tgt.Description   = N'INFORMATIVA, NUNCA BLOQUEANTE. Dice si una medicina esta en el vademecum del convenio del prestador. Devuelve EstadoVademecum con TRES valores y hay que respetar la diferencia: EN_VADEMECUM (esta y activa); NO_EN_VADEMECUM (el convenio SI tiene vademecum cargado y esta medicina no aparece); CONVENIO_SIN_VADEMECUM (ese convenio no tiene vademecum cargado, asi que NO SE PUEDE AFIRMAR NADA — solo 15 convenios lo tienen, son las cadenas de farmacia con credito). Reportar CONVENIO_SIN_VADEMECUM como si fuera "no esta en vademecum" seria una negativa falsa. En ningun caso este resultado bloquea el reembolso: es informacion para el analista.',
    tgt.InputSchema   = @schVad,
    tgt.BindingType   = 'Sql',
    tgt.BindingConfig = (SELECT 'SaludsaCreditoFarmacia' AS connection, 20 AS maxRows, @sqlVad AS query FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
    tgt.IsActive      = 1,
    tgt.VersionNumber = ISNULL(tgt.VersionNumber, 0) + 1
WHEN NOT MATCHED THEN INSERT (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES ('validar_medicina_vademecum', N'validar_medicina_vademecum',
        N'INFORMATIVA, no bloqueante. EN_VADEMECUM / NO_EN_VADEMECUM / CONVENIO_SIN_VADEMECUM (solo 15 convenios tienen vademecum cargado).',
        @schVad, 0, 'Sql',
        (SELECT 'SaludsaCreditoFarmacia' AS connection, 20 AS maxRows, @sqlVad AS query FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
        1, 1, GETUTCDATE());
GO

/* ---------------------------------------------------------------------------
   TOOL 3 — buscar_medicina_prestador_vademecum
   El equivalente literal de ObtenerMedicinaPrestadorVademecum(convenio, prestacion):
   busca por el nombre con el que la FARMACIA llama al producto.
   --------------------------------------------------------------------------- */
DECLARE @sqlPre nvarchar(max) = N'
SELECT TOP (25)
       p.NumeroConvenio,
       p.CodigoMedicinaPrestador,
       p.Nombre                AS NombreEnLaFarmacia,
       p.Descripcion           AS DescripcionEnLaFarmacia,
       v.CodigoMedicinaSaludsa,
       v.Activo                AS ActivoEnVademecum,
       m.Descripcion           AS DescripcionSaludsa,
       m.PrincipioActivo,
       m.TipoProducto,
       m.CodigoBeneficio,
       m.TipoTratamiento,
       m.EsContinuo
FROM Saludsa.CreditoFarmacia.CFPrestador p WITH (NOLOCK)
LEFT JOIN Saludsa.CreditoFarmacia.CFMedicinaPrestadorVademecum v WITH (NOLOCK)
       ON v.NumeroConvenio = p.NumeroConvenio
      AND v.CodigoMedicinaPrestador = p.CodigoMedicinaPrestador
LEFT JOIN Saludsa.CreditoFarmacia.CFMedicina m WITH (NOLOCK)
       ON m.CodigoMedicinaSaludsa = v.CodigoMedicinaSaludsa
WHERE p.NumeroConvenio = @numeroConvenio
  AND p.Activo = 1
  AND (@texto IS NULL OR p.Nombre LIKE ''%'' + @texto + ''%'' OR p.Descripcion LIKE ''%'' + @texto + ''%'')
ORDER BY p.Nombre';

DECLARE @schPre nvarchar(max) = N'{
  "type": "object",
  "properties": {
    "numeroConvenio": {
      "type": "string",
      "description": "NumeroConvenio de la farmacia, obtenido con resolver_convenio_por_ruc."
    },
    "texto": {
      "type": "string",
      "description": "Nombre del producto tal como lo escribe la farmacia en su factura. Opcional: sin el, lista el vademecum del convenio."
    }
  },
  "required": ["numeroConvenio"]
}';

MERGE dbo.OPAITool AS tgt
USING (SELECT 'buscar_medicina_prestador_vademecum' AS Code) AS src ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    tgt.Name          = N'buscar_medicina_prestador_vademecum',
    tgt.Description   = N'Busca en el vademecum del convenio POR EL NOMBRE QUE USA LA FARMACIA, que casi nunca es igual al nombre oficial de Saludsa. Devuelve el CodigoMedicinaSaludsa homologado, el principio activo y si es MARCA o GENERICO. Usala cuando la factura viene de una cadena de farmacia y el nombre del producto no aparece en buscar_medicina_catalogo_cf.',
    tgt.InputSchema   = @schPre,
    tgt.BindingType   = 'Sql',
    tgt.BindingConfig = (SELECT 'SaludsaCreditoFarmacia' AS connection, 25 AS maxRows, @sqlPre AS query FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
    tgt.IsActive      = 1,
    tgt.VersionNumber = ISNULL(tgt.VersionNumber, 0) + 1
WHEN NOT MATCHED THEN INSERT (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES ('buscar_medicina_prestador_vademecum', N'buscar_medicina_prestador_vademecum',
        N'Busca en el vademecum por el nombre que usa la farmacia y devuelve el codigo homologado de Saludsa.',
        @schPre, 0, 'Sql',
        (SELECT 'SaludsaCreditoFarmacia' AS connection, 25 AS maxRows, @sqlPre AS query FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
        1, 1, GETUTCDATE());
GO

/* Habilitarlas para los agentes que ya tienen tools */
;WITH nuevas ([ToolCode], [Order]) AS (
    SELECT 'buscar_medicina_catalogo_cf',         80 UNION ALL
    SELECT 'validar_medicina_vademecum',          81 UNION ALL
    SELECT 'buscar_medicina_prestador_vademecum', 82
),
modelos (ModelCode) AS (SELECT DISTINCT ModelCode FROM dbo.OPAIModelTool)
MERGE dbo.OPAIModelTool AS tgt
USING (
    SELECT m.ModelCode, n.[ToolCode], n.[Order]
    FROM nuevas n CROSS JOIN modelos m
    JOIN dbo.OPAITool t ON t.Code = n.[ToolCode] AND t.IsActive = 1
) AS src
   ON tgt.ModelCode = src.ModelCode AND tgt.ToolCode = src.ToolCode
WHEN MATCHED THEN UPDATE SET tgt.[Order] = src.[Order], tgt.IsEnabled = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (ModelCode, ToolCode, [Order], IsEnabled)
    VALUES (src.ModelCode, src.[ToolCode], src.[Order], 1);
GO

SELECT 'tools' AS q, Code, JSON_VALUE(BindingConfig, '$.connection') AS Conexion, IsActive
FROM dbo.OPAITool
WHERE Code IN ('buscar_medicina_catalogo_cf','validar_medicina_vademecum','buscar_medicina_prestador_vademecum');

SELECT 'total tools activas' AS q, COUNT(*) AS n FROM dbo.OPAITool WHERE IsActive = 1;
GO
