/* =============================================================================
   REQ-020j — ¿De quién es esta factura?
   -----------------------------------------------------------------------------
   Un contrato cubre a varias personas y el afiliado sube facturas de todas
   ellas. Hoy nada comprueba que la factura corresponda al beneficiario elegido:
   si el padre elige a la hija pequeña y sube la factura de la mayor, el sistema
   liquida contra el deducible y la carencia equivocados y nadie se entera.

   Lo que YA existía y estaba roto por tres lados:
     · El prompt tiene el código de alerta NOMBRE_FACTURA_NO_COINCIDE, pero NO
       está en la lista de alertas obligatorias.
     · Ningún C# lee esa alerta.
     · Las alertas del clasificador no se persisten en ninguna tabla: se
       pierden al terminar la corrida.

   Y sobre todo faltaba el otro lado de la comparación: el contexto mandaba
   siempre al TITULAR, así que un dependiente legítimo y un tercero ajeno eran
   indistinguibles. Desde REQ-020c el beneficiario elegido viaja en el contexto,
   con la lista completa de personas del contrato.

   SOBRE EL ANONIMATO. La regla dura del clasificador prohíbe escribir el nombre
   del paciente en la SALIDA, no leerlo en la entrada — el OCR llega íntegro al
   modelo — y contempla expresamente decir que el nombre no coincide "sin
   transcribir ninguno de los dos". Por eso la respuesta es un NÚMERO DE PERSONA
   de la lista que se le entrega, nunca un nombre.

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

IF COL_LENGTH('dbo.DocumentoClasificacion', 'PacienteNumeroPersona') IS NULL
BEGIN
    ALTER TABLE dbo.DocumentoClasificacion ADD
        /* A qué persona del contrato corresponde el documento. Es el
           NumeroPersona de la lista que se le entregó al clasificador; jamás
           un nombre, para no romper la regla de anonimato. */
        PacienteNumeroPersona int NULL,

        /* COINCIDE          el documento es del beneficiario elegido
           OTRO_BENEFICIARIO es de otra persona del MISMO contrato
           FUERA_DEL_PLAN    es de alguien que no está cubierto
           NO_SE_PUDO        no había nombre legible con qué comparar        */
        PacienteCoincide varchar(20) NULL,

        /* Por qué se decidió así, SIN transcribir ningún nombre. */
        PacienteJustificacion varchar(500) NULL;
END
GO

/* ---------------------------------------------------------------------------
   Verificación
   --------------------------------------------------------------------------- */
SELECT 'de quien es el documento' AS Pieza,
       CASE WHEN COL_LENGTH('dbo.DocumentoClasificacion','PacienteNumeroPersona') IS NOT NULL
                 AND COL_LENGTH('dbo.DocumentoClasificacion','PacienteCoincide') IS NOT NULL
                 AND COL_LENGTH('dbo.DocumentoClasificacion','PacienteJustificacion') IS NOT NULL
            THEN 'OK' ELSE 'FALTA' END AS Estado;
GO
