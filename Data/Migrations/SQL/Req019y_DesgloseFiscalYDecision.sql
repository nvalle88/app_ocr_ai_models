/* =============================================================================
   REQ-019y — El desglose fiscal completo y EL PORQUÉ de cada decisión
   -----------------------------------------------------------------------------
   1) DESGLOSE FISCAL
      Hoy solo se guardaban Subtotal, Iva y Total. Una factura electrónica
      ecuatoriana trae el pie completo, y cada línea significa algo distinto
      para la liquidación:

        Subtotal Sin Impuestos       la base imponible real
        Subtotal 15% / 5% / 0%       por tarifa de IVA
        Subtotal No Objeto IVA       ← los servicios de salud caen aquí
        Descuentos                   se descuenta ANTES de cubrir
        ICE                          impuesto a consumos especiales
        IVA 15% / IVA 5%             el impuesto efectivamente cobrado
        Servicio %                   propina/servicio, NO es gasto médico
        Valor Total                  lo que paga el afiliado

      Perder ese desglose obliga a adivinar la base de cálculo. Con él, la
      liquidación puede excluir servicio y descuentos sin tener que suponerlos.

   2) EL PORQUÉ DE LA DECISIÓN — observabilidad de verdad
      Que el clasificador diga "CON_MED-FACTURA" sin decir por qué no es
      auditable: el analista no puede saber si acertó por la razón correcta o
      por casualidad. Se guarda, por documento:

        JustificacionTipo         por qué ESE tipo y no otro
        SenalesTipo               las marcas del texto que lo sustentan
        TipoDescartado            la alternativa que estuvo más cerca
        JustificacionDescarte     por qué se descartó
        JustificacionFactura      por qué es (o no) factura válida
        JustificacionSoporte      por qué ese tipo clínico de soporte

      Con eso, cuando alguien discuta una tipificación, la respuesta está en la
      base y no hay que volver a preguntarle al modelo.

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
   1) Desglose fiscal completo
   --------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.DocumentoClasificacion', 'SubtotalSinImpuestos') IS NULL
BEGIN
    ALTER TABLE dbo.DocumentoClasificacion ADD
        SubtotalSinImpuestos decimal(18,2) NULL,
        Subtotal15           decimal(18,2) NULL,
        Subtotal5            decimal(18,2) NULL,
        Subtotal0            decimal(18,2) NULL,
        SubtotalNoObjetoIva  decimal(18,2) NULL,
        SubtotalExentoIva    decimal(18,2) NULL,
        Descuentos           decimal(18,2) NULL,
        Ice                  decimal(18,2) NULL,
        Iva15                decimal(18,2) NULL,
        Iva5                 decimal(18,2) NULL,
        ServicioValor        decimal(18,2) NULL,   -- el monto de "Servicio %"
        ServicioPorcentaje   decimal(6,2)  NULL,   -- el % declarado, si consta
        Propina              decimal(18,2) NULL;
END
GO

/* ---------------------------------------------------------------------------
   2) El porqué de cada decisión
   --------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.DocumentoClasificacion', 'JustificacionTipo') IS NULL
BEGIN
    ALTER TABLE dbo.DocumentoClasificacion ADD
        JustificacionTipo     varchar(1000) NULL,  -- por qué ESE TipoArchivo
        SenalesTipo           varchar(1000) NULL,  -- las marcas del texto, separadas por |
        TipoDescartado        varchar(30)   NULL,  -- la alternativa más cercana
        JustificacionDescarte varchar(600)  NULL,  -- por qué se descartó
        JustificacionFactura  varchar(600)  NULL,  -- por qué es (o no) factura válida
        JustificacionSoporte  varchar(600)  NULL;  -- por qué ese tipo clínico
END
GO

/* ---------------------------------------------------------------------------
   3) De qué documento salió cada diagnóstico
      DocumentoDiagnostico ya tiene DataFileId, pero el resumen agrupado que ve
      la pantalla perdía esa traza y por eso no se podía anclar al documento.
      Nada que alterar aquí: se resuelve en la reconstrucción del DTO.
      Se deja constancia para que no se busque una columna que no hace falta.
   --------------------------------------------------------------------------- */

/* ---------------------------------------------------------------------------
   Verificación
   --------------------------------------------------------------------------- */
SELECT 'desglose fiscal' AS Bloque,
       CASE WHEN COL_LENGTH('dbo.DocumentoClasificacion','SubtotalSinImpuestos') IS NOT NULL
                 AND COL_LENGTH('dbo.DocumentoClasificacion','SubtotalNoObjetoIva') IS NOT NULL
                 AND COL_LENGTH('dbo.DocumentoClasificacion','ServicioValor') IS NOT NULL
            THEN 'OK' ELSE 'FALTA' END AS Estado
UNION ALL
SELECT 'porqué de la decisión',
       CASE WHEN COL_LENGTH('dbo.DocumentoClasificacion','JustificacionTipo') IS NOT NULL
                 AND COL_LENGTH('dbo.DocumentoClasificacion','SenalesTipo') IS NOT NULL
                 AND COL_LENGTH('dbo.DocumentoClasificacion','TipoDescartado') IS NOT NULL
            THEN 'OK' ELSE 'FALTA' END
UNION ALL
SELECT 'columnas nuevas en total',
       CONVERT(varchar(10), (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                             WHERE TABLE_NAME = 'DocumentoClasificacion'));
GO
