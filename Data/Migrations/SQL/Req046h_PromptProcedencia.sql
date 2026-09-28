-- ============================================================
-- REQ-046h — El dictamen carga la PROCEDENCIA: en analisisContractual el agente
-- incluye pagina + textoOrigen (de las tools anexo_*), para anclar cada hallazgo
-- al segmento exacto del PDF. Idempotente. Cambiar BD para prod (db-nexus-aud).
-- ============================================================
IF DB_NAME() <> N'db-nexus-test'
BEGIN
    RAISERROR('Script REQ-046h solo permitido en db-nexus-test. Abortado.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON; SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON;

DECLARE @old nvarchar(max) = N'"analisisContractual":{"cobertura":{"incluido":false,"porcentaje":"","tope":"","deducible":"","copago":"","notas":""},"preexistencias":{"hallazgo":"","tipoNexo":"","evidencia":""},"carencias":{"aplica":false,"detalle":""},"exclusiones":{"aplica":false,"textoCitado":"","clausula":""}}';
DECLARE @new nvarchar(max) = N'"analisisContractual":{"cobertura":{"incluido":false,"porcentaje":"","tope":"","deducible":"","copago":"","notas":"","pagina":0,"textoOrigen":""},"preexistencias":{"hallazgo":"","tipoNexo":"","evidencia":""},"carencias":{"aplica":false,"detalle":"","pagina":0,"textoOrigen":""},"exclusiones":{"aplica":false,"textoCitado":"","clausula":"","pagina":0,"textoOrigen":""}}';

UPDATE dbo.Agent SET SystemPrompt = REPLACE(SystemPrompt, @old, @new)
 WHERE Code = 'AGENTE_AUDITOR_CASOS' AND CHARINDEX(@old, SystemPrompt) > 0;

DECLARE @instr nvarchar(max) = N'
PROCEDENCIA (obligatoria en analisisContractual): por cada cobertura, carencia y exclusión, copia "pagina" y "textoOrigen" tal cual vienen en el resultado de las tools anexo_coberturas / anexo_carencias / anexo_exclusiones / anexo_clausula (columnas Pagina y TextoOrigen de la fila usada). Sirven para abrir el PDF del anexo en esa página y resaltar el segmento. Si no usaste una tool para ese punto, deja pagina en null y textoOrigen vacío.';

UPDATE dbo.Agent SET SystemPrompt = SystemPrompt + @instr
 WHERE Code = 'AGENTE_AUDITOR_CASOS' AND CHARINDEX('PROCEDENCIA (obligatoria', SystemPrompt) = 0;

SELECT Code,
  CASE WHEN SystemPrompt LIKE '%"textoOrigen":""}},"preexistencias"%' OR SystemPrompt LIKE '%textoOrigen%' THEN 'salida con procedencia' ELSE 'sin' END AS Salida,
  CASE WHEN SystemPrompt LIKE '%PROCEDENCIA (obligatoria%' THEN 'con instruccion' ELSE 'sin' END AS Instruccion,
  LEN(SystemPrompt) AS Largo
FROM dbo.Agent WHERE Code = 'AGENTE_AUDITOR_CASOS';
