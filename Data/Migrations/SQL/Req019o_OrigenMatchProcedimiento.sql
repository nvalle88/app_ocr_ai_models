SET QUOTED_IDENTIFIER ON;
GO
IF DB_NAME() <> 'db-nexus-test'
BEGIN
    RAISERROR('Solo db-nexus-test', 16, 1);
    RETURN;
END
GO
/* REQ-019o - de donde salio el codigo de liquidacion:
     CPT         el documento traia el codigo y coincidio en el catalogo
     DESCRIPCION no habia codigo; se emparejo por texto (SUGERENCIA a revisar)
     NULL        no se pudo emparejar                                        */
IF COL_LENGTH('dbo.DocumentoProcedimiento', 'OrigenMatch') IS NULL
    ALTER TABLE dbo.DocumentoProcedimiento ADD OrigenMatch varchar(20) NULL;
GO
SELECT CASE WHEN COL_LENGTH('dbo.DocumentoProcedimiento','OrigenMatch') IS NOT NULL
            THEN 'OK OrigenMatch' ELSE 'FALTA' END AS Estado;
GO
