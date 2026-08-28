/* =============================================================================
   REQ-020c — El reembolso va dirigido a UN beneficiario, no al contrato
   -----------------------------------------------------------------------------
   El portal daba por sentado que el reembolso era del titular. No lo es: un
   contrato cubre al titular y a sus dependientes, y el gasto puede ser de
   cualquiera de ellos.

   Y esto NO es cosmetico. La respuesta de ObtenerContratoPorDocumento trae, por
   CADA beneficiario:

       DeducibleCubierto      cuanto deducible lleva consumido ESA persona
       EnCarencia             si todavia esta en periodo de espera
       DiasFinCarencia        cuantos dias le faltan
       Preexistencias[]       las suyas, no las del contrato

   Liquidar contra el titular cuando la factura es del hijo da un resultado
   equivocado: se aplica el deducible de otro y se ignora la carencia del que
   de verdad se atendio. Por eso el beneficiario tiene que elegirse antes de
   procesar, y viajar al ContextoSobre para que TODO el pipeline —tipificacion,
   auditoria y resolucion— trabaje contra la persona correcta.

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
   El beneficiario elegido, y la foto de todos los del contrato
   --------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.SolicitudCliente', 'NombreBeneficiario') IS NULL
BEGIN
    ALTER TABLE dbo.SolicitudCliente ADD
        /* A quien va dirigido el reembolso. NumeroPersona ya existia en la
           tabla como el titular; ahora significa el beneficiario elegido. */
        NombreBeneficiario   varchar(200) NULL,
        CedulaBeneficiario   varchar(20)  NULL,
        RelacionBeneficiario varchar(60)  NULL,   -- Titular | Conyuge | Hijo...
        EdadBeneficiario     int          NULL,
        GeneroBeneficiario   varchar(5)   NULL,

        /* Condiciones propias de esa persona en el momento de presentar. Se
           guardan porque cambian con el tiempo y hay que poder explicar
           despues por que se resolvio como se resolvio. */
        DeducibleCubierto    decimal(18,2) NULL,
        EnCarencia           bit           NULL,
        DiasFinCarencia      int           NULL,
        TienePreexistencias  bit           NULL,

        /* La lista completa de beneficiarios tal como la devolvio la API, para
           poder repintar el selector sin volver a llamar. */
        BeneficiariosJson    nvarchar(max) NULL;
END
GO

/* ---------------------------------------------------------------------------
   Verificacion
   --------------------------------------------------------------------------- */
SELECT 'beneficiario elegido' AS Pieza,
       CASE WHEN COL_LENGTH('dbo.SolicitudCliente','NombreBeneficiario') IS NOT NULL
                 AND COL_LENGTH('dbo.SolicitudCliente','RelacionBeneficiario') IS NOT NULL
            THEN 'OK' ELSE 'FALTA' END AS Estado
UNION ALL
SELECT 'condiciones de esa persona',
       CASE WHEN COL_LENGTH('dbo.SolicitudCliente','DeducibleCubierto') IS NOT NULL
                 AND COL_LENGTH('dbo.SolicitudCliente','EnCarencia') IS NOT NULL
            THEN 'OK' ELSE 'FALTA' END
UNION ALL
SELECT 'foto de todos los beneficiarios',
       CASE WHEN COL_LENGTH('dbo.SolicitudCliente','BeneficiariosJson') IS NOT NULL
            THEN 'OK' ELSE 'FALTA' END;
GO
