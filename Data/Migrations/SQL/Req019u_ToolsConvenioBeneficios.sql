/* =============================================================================
   REQ-019u — Beneficios POR CONVENIO: del RUC del documento al % de cobertura
   -----------------------------------------------------------------------------
   EL FLUJO, tal como lo definió Operaciones y como quedó verificado en vivo:

     RUC del documento (OCR)
        └─► Co03Convenio.Rucins              Salud @ SQLMIGRACION      ── tool 1
            └─► NumeroConvenio
                ├─► convenio_valores          bdd_prestadores @ salud37 ── tool 2
                │     % medicina MARCA / % medicina GENERICA / lab /
                │     imagen / procedimientos / terapias + fee de consulta
                └─► ConvenioPlan ⋈ BeneficioConvenio                    ── tool 3
                      beneficio por beneficio (A002, A010, A011, A003...)
                      con su Valor y si es porcentaje o monto

   VERIFICADO CON EL CASO REAL (NA-2612602, 2026-08-21)
     RUC 0916422173001  ->  convenio 5088598  ->  Roberto Felipe Muñoz Jaramillo
     convenio_valores (producto COR, lista 100001155):
        FeeConsulta 12.00 · MedicinaMarca 70% · MedicinaGenerica 70%
        LabClinico 70% · Imagenes 70% · Procedimientos 70%

   POR QUE TRES TOOLS Y NO UNA
     Se pidieron lo más atómicas posible, y hay razón de fondo: el RUC puede no
     estar en Co03 (prestador sin convenio = fuera de red, que es una respuesta
     válida y distinta de "no hay datos"); un convenio puede tener MUCHOS planes
     (el 5088598 tiene más de 100); y el beneficio fino no siempre hace falta.
     Encadenarlas deja ver en la auditoría exactamente dónde se cortó.

   MATIZ QUE NO SE PUEDE PASAR POR ALTO
     En BeneficioConvenio, EsPorcentaje NO es siempre 1:
        A010 / A011 (medicina marca y generica)  -> EsPorcentaje = 1  (es %)
        A002 (consultas medicas)                 -> EsPorcentaje = 0  (es MONTO)
     Tratar el valor de A002 como porcentaje da un disparate. La tool devuelve
     la bandera y la descripcion ya resuelta para que no se confunda.

   VADEMECUM — buscado y NO encontrado, se dice en vez de inventarlo
     · bdd_prestadores no tiene tabla de vademecum: sus tablas grandes son
       BeneficioConvenio (72,9M), ConvenioPlan (9,1M) y convenio_valores (433k);
       PRODUCTO (9 filas) y PRODUCTO_PRESTADOR (16) son modulos del portal.
     · Salud @ SQLMIGRACION tampoco tiene tabla ni columna con 'vademec'.
     · api-prestador: /api/Vademecum, /ObtenerVademecum, /ConsultarVademecum y
       /ObtenerMedicinaPrestadorVademecum devuelven 404 en pruebas.
     · Salud.dbo.CatalogoProductosMedicinas (molecula/ControlHumano) esta VACIA.
     El cerebro apunta a CorrelacionService.cs:154-176 del repo de reembolso
     automatico (ObtenerMedicinaPrestadorVademecum(convenio, prestacion)): ahi
     esta la fuente real, falta cablearla. Mientras tanto el vademecum es
     NO VERIFICABLE — y como es informativo y no bloqueante, eso basta: se
     reporta como "no verificable", nunca como "no esta en vademecum".

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
   TOOL 1 — resolver_convenio_por_ruc
   El RUC sale del OCR; esta tool lo VALIDA contra la base. Si no hay filas, el
   prestador no tiene convenio: es fuera de red, no "faltan datos".
   --------------------------------------------------------------------------- */
DECLARE @sqlConv nvarchar(max) = N'
SELECT TOP (10)
       c.NumeroConvenio,
       c.NombrePrestador,
       c.NombreComercial,
       c.Rucins,
       c.TipoPrestador,
       c.EstadoConvenio,
       CONVERT(varchar(10), c.FechaInicioConvenio, 23) AS FechaInicioConvenio,
       CONVERT(varchar(10), c.FechaFinConvenio, 23)    AS FechaFinConvenio,
       c.NivelPrestadorDesde,
       c.NivelPrestadorHasta
FROM Salud.dbo.Co03Convenio c WITH (NOLOCK)
WHERE REPLACE(RTRIM(LTRIM(c.Rucins)), '' '', '''') = REPLACE(RTRIM(LTRIM(@ruc)), '' '', '''')
ORDER BY c.NumeroConvenio';

DECLARE @schConv nvarchar(max) = N'{
  "type": "object",
  "properties": {
    "ruc": {
      "type": "string",
      "description": "RUC de 13 digitos del prestador que EMITE la factura, tal como se leyo del documento. Sin guiones ni espacios."
    }
  },
  "required": ["ruc"]
}';

MERGE dbo.OPAITool AS tgt
USING (SELECT 'resolver_convenio_por_ruc' AS Code) AS src ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    tgt.Name          = N'resolver_convenio_por_ruc',
    tgt.Description   = N'PASO 1 del flujo de convenio. Convierte el RUC del prestador que emite la factura en su NumeroConvenio, y de paso VALIDA contra la base que ese RUC existe. Devuelve nombre del prestador, tipo, estado y vigencia del convenio. Si no devuelve filas, el prestador NO tiene convenio con Saludsa: es un prestador FUERA DE RED, que es una conclusion valida y distinta de "faltan datos". Con el NumeroConvenio que devuelve se llama despues a consultar_coberturas_convenio.',
    tgt.InputSchema   = @schConv,
    tgt.BindingType   = 'Sql',
    tgt.BindingConfig = (SELECT 'SaludProcedimientos' AS connection, 10 AS maxRows, @sqlConv AS query FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
    tgt.IsActive      = 1,
    tgt.VersionNumber = ISNULL(tgt.VersionNumber, 0) + 1
WHEN NOT MATCHED THEN INSERT (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES ('resolver_convenio_por_ruc', N'resolver_convenio_por_ruc',
        N'PASO 1 del flujo de convenio. Convierte el RUC del prestador que emite la factura en su NumeroConvenio y valida que exista. Si no devuelve filas, el prestador esta FUERA DE RED.',
        @schConv, 0, 'Sql',
        (SELECT 'SaludProcedimientos' AS connection, 10 AS maxRows, @sqlConv AS query FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
        1, 1, GETUTCDATE());
GO

/* ---------------------------------------------------------------------------
   TOOL 2 — consultar_coberturas_convenio
   Los porcentajes de cobertura del convenio, ya legibles. Aqui esta la
   respuesta a "medicina de marca vs generica".
   --------------------------------------------------------------------------- */
DECLARE @sqlCob nvarchar(max) = N'
SELECT TOP (20)
       cv.NumeroConvenio,
       cv.RazonSocial,
       cv.NombreComercial,
       cv.CodigoProducto,
       cv.lista                        AS Lista,
       cv.ValFeeConsulta               AS FeeConsulta,
       cv.prj_CobMedicinaMarca         AS PctMedicinaMarca,
       cv.prj_CobMedicinaGenerica      AS PctMedicinaGenerica,
       cv.prj_CobLabClinico            AS PctLaboratorioClinico,
       cv.prj_CobImagenes              AS PctImagenes,
       cv.prj_CobProcedimientos        AS PctProcedimientos,
       cv.prj_CobTerapiaFisica         AS PctTerapiaFisica,
       cv.prj_CobTerapiaRespiratoria   AS PctTerapiaRespiratoria,
       cv.prj_CobTerapiaLenguaje       AS PctTerapiaLenguaje,
       CONVERT(varchar(10), cv.FechaUltModificaion, 23) AS FechaUltModificacion
FROM dbo.convenio_valores cv WITH (NOLOCK)
WHERE cv.NumeroConvenio = @numeroConvenio
  AND (@codigoProducto IS NULL OR cv.CodigoProducto = @codigoProducto)
ORDER BY cv.FechaUltModificaion DESC, cv.lista DESC';

DECLARE @schCob nvarchar(max) = N'{
  "type": "object",
  "properties": {
    "numeroConvenio": {
      "type": "string",
      "description": "NumeroConvenio devuelto por resolver_convenio_por_ruc."
    },
    "codigoProducto": {
      "type": "string",
      "description": "Codigo de producto para acotar (COR, IND, POO...). Opcional: sin el, devuelve todas las listas del convenio."
    }
  },
  "required": ["numeroConvenio"]
}';

MERGE dbo.OPAITool AS tgt
USING (SELECT 'consultar_coberturas_convenio' AS Code) AS src ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    tgt.Name          = N'consultar_coberturas_convenio',
    tgt.Description   = N'PASO 2 del flujo de convenio: los PORCENTAJES de cobertura negociados con ese prestador. Trae por separado medicina de MARCA y medicina GENERICA (que es justo lo que hay que distinguir en un reembolso de medicina), mas laboratorio, imagenes, procedimientos y terapias, y el fee de consulta en DOLARES. Un mismo convenio puede tener varias listas y productos: si sabes el producto, pasalo. Los porcentajes vienen 0-100.',
    tgt.InputSchema   = @schCob,
    tgt.BindingType   = 'Sql',
    tgt.BindingConfig = (SELECT 'SaludPrestadores' AS connection, 20 AS maxRows, @sqlCob AS query FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
    tgt.IsActive      = 1,
    tgt.VersionNumber = ISNULL(tgt.VersionNumber, 0) + 1
WHEN NOT MATCHED THEN INSERT (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES ('consultar_coberturas_convenio', N'consultar_coberturas_convenio',
        N'PASO 2: porcentajes de cobertura del convenio, con medicina de MARCA y GENERICA por separado, mas laboratorio, imagenes, procedimientos, terapias y fee de consulta.',
        @schCob, 0, 'Sql',
        (SELECT 'SaludPrestadores' AS connection, 20 AS maxRows, @sqlCob AS query FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
        1, 1, GETUTCDATE());
GO

/* ---------------------------------------------------------------------------
   TOOL 3 — consultar_beneficio_convenio
   El detalle fino, beneficio por beneficio. OJO con EsPorcentaje.
   --------------------------------------------------------------------------- */
DECLARE @sqlBen nvarchar(max) = N'
SELECT TOP (40)
       cp.NumeroConvenio,
       cp.CodigoProducto,
       cp.CodigoPlan,
       cp.VersionPlan,
       bc.CodigoBeneficio,
       bc.Valor,
       bc.EsPorcentaje,
       CASE WHEN bc.EsPorcentaje = 1
            THEN CONVERT(varchar(20), CONVERT(decimal(10,2), bc.Valor)) + '' %''
            ELSE ''USD '' + CONVERT(varchar(20), CONVERT(decimal(10,2), bc.Valor))
       END AS ValorLegible,
       bc.CodigoTipoGestionAtencion,
       CONVERT(varchar(10), cp.FechaInicioVigencia, 23) AS Desde,
       CONVERT(varchar(10), cp.FechaFinVigencia, 23)    AS Hasta
FROM dbo.BeneficioConvenio bc WITH (NOLOCK)
INNER JOIN dbo.ConvenioPlan cp WITH (NOLOCK) ON cp.IdConvenioPlan = bc.IdConvenioPlan
WHERE cp.NumeroConvenio = @numeroConvenio
  AND bc.EstadoActivo = 1
  AND (@codigoBeneficio IS NULL OR bc.CodigoBeneficio = @codigoBeneficio)
  AND (@codigoPlan IS NULL OR cp.CodigoPlan = @codigoPlan)
ORDER BY cp.FechaInicioVigencia DESC, bc.CodigoBeneficio';

DECLARE @schBen nvarchar(max) = N'{
  "type": "object",
  "properties": {
    "numeroConvenio": {
      "type": "string",
      "description": "NumeroConvenio devuelto por resolver_convenio_por_ruc."
    },
    "codigoBeneficio": {
      "type": "string",
      "description": "Beneficio a consultar: A010 medicina de MARCA, A011 medicina GENERICA, A002 consultas medicas, A003 laboratorio, A004 imagen, A005 procedimientos. Opcional."
    },
    "codigoPlan": {
      "type": "string",
      "description": "Plan concreto para acotar. Opcional: un convenio puede tener mas de cien planes."
    }
  },
  "required": ["numeroConvenio"]
}';

MERGE dbo.OPAITool AS tgt
USING (SELECT 'consultar_beneficio_convenio' AS Code) AS src ON tgt.Code = src.Code
WHEN MATCHED THEN UPDATE SET
    tgt.Name          = N'consultar_beneficio_convenio',
    tgt.Description   = N'PASO 3 (detalle fino) del flujo de convenio: el beneficio negociado plan por plan. CUIDADO con EsPorcentaje: A010 y A011 (medicina marca y generica) vienen como PORCENTAJE, pero A002 (consultas medicas) viene como MONTO en dolares. La columna ValorLegible ya resuelve esa diferencia; usala y no interpretes Valor por tu cuenta. Un convenio puede tener mas de cien planes: acota con codigoPlan o codigoBeneficio.',
    tgt.InputSchema   = @schBen,
    tgt.BindingType   = 'Sql',
    tgt.BindingConfig = (SELECT 'SaludPrestadores' AS connection, 40 AS maxRows, @sqlBen AS query FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
    tgt.IsActive      = 1,
    tgt.VersionNumber = ISNULL(tgt.VersionNumber, 0) + 1
WHEN NOT MATCHED THEN INSERT (Code, Name, Description, InputSchema, Strict, BindingType, BindingConfig, IsActive, VersionNumber, CreatedDate)
VALUES ('consultar_beneficio_convenio', N'consultar_beneficio_convenio',
        N'PASO 3: beneficio negociado plan por plan. A010/A011 son porcentaje; A002 es monto. Usa ValorLegible.',
        @schBen, 0, 'Sql',
        (SELECT 'SaludPrestadores' AS connection, 40 AS maxRows, @sqlBen AS query FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
        1, 1, GETUTCDATE());
GO

/* ---------------------------------------------------------------------------
   Habilitarlas para todos los agentes que ya tienen tools
   --------------------------------------------------------------------------- */
;WITH nuevas ([ToolCode], [Order]) AS (
    SELECT 'resolver_convenio_por_ruc',      70 UNION ALL
    SELECT 'consultar_coberturas_convenio',  71 UNION ALL
    SELECT 'consultar_beneficio_convenio',   72
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

/* Verificacion */
SELECT 'tools' AS q, Code, JSON_VALUE(BindingConfig, '$.connection') AS Conexion, IsActive
FROM dbo.OPAITool
WHERE Code IN ('resolver_convenio_por_ruc','consultar_coberturas_convenio','consultar_beneficio_convenio');

SELECT 'links' AS q, ModelCode, ToolCode, [Order]
FROM dbo.OPAIModelTool
WHERE ToolCode IN ('resolver_convenio_por_ruc','consultar_coberturas_convenio','consultar_beneficio_convenio')
ORDER BY ModelCode, [Order];
GO
