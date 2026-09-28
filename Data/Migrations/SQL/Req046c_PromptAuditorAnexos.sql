-- ============================================================
-- REQ-046c — El agente auditor aprende a usar la biblioteca de anexos.
--   Anexa (idempotente) al SystemPrompt de AGENTE_AUDITOR_MEDICINA la guía de
--   las tools anexo_* y las reglas de prelación contractual.
-- ============================================================
IF DB_NAME() <> N'db-nexus-test'
BEGIN
    RAISERROR('Script REQ-046c solo permitido en db-nexus-test. Abortado.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @bloque nvarchar(max) = N'

=== BIBLIOTECA DE ANEXOS (fuente contractual estructurada de Saludsa) ===
Para el ANÁLISIS CONTRACTUAL usa SIEMPRE estas herramientas, que leen las condiciones del plan ya estructuradas en nuestra base (no en documentos sueltos):
- anexo_por_plan(codigoPlan[, codigoProducto]): ubica el anexo del plan del cliente y su contrato base. Empieza por aquí.
- anexo_coberturas(codigoPlan[, beneficio]): porcentaje de cobertura, tope, deducible, copago, periodo y ámbito por beneficio.
- anexo_carencias(codigoPlan): días de carencia por beneficio.
- anexo_exclusiones(codigoPlan): exclusiones del plan y del contrato base. Cítalas TEXTUAL; una exclusión NO se interpreta por analogía.
- anexo_clausula(tipoContrato[, texto]): cláusula, numeral y literal exactos para citar (formato: Cláusula X, numeral Y, literal Z).

El codigoPlan del cliente viene en el contexto del caso; si no consta, indícalo como dato faltante y no lo inventes.

Reglas de prelación (no negociables): para pagar se requieren AMBAS, pertinencia médica Y cobertura contractual. Ante conflicto de interpretación PRIMA EL CONTRATO; dentro del contrato PRIMA LA TABLA DEL ANEXO cuando contempla el escenario específico. Si una condición (cobertura, tope, deducible, carencia, exclusión o cláusula) no aparece en la biblioteca de anexos, escríbelo como "no localizada en la biblioteca de anexos — verificar"; nunca inventes números de cláusula ni textos contractuales.';

IF EXISTS (SELECT 1 FROM dbo.Agent WHERE Code = 'AGENTE_AUDITOR_MEDICINA')
   AND NOT EXISTS (SELECT 1 FROM dbo.Agent WHERE Code = 'AGENTE_AUDITOR_MEDICINA' AND SystemPrompt LIKE '%anexo_coberturas%')
BEGIN
    UPDATE dbo.Agent
    SET SystemPrompt = ISNULL(SystemPrompt, N'') + @bloque
    WHERE Code = 'AGENTE_AUDITOR_MEDICINA';
    PRINT 'SystemPrompt de AGENTE_AUDITOR_MEDICINA actualizado con la guía de anexos.';
END
ELSE
    PRINT 'Sin cambios (agente no existe o ya tenía la guía de anexos).';

SELECT Code,
       CASE WHEN SystemPrompt LIKE '%anexo_coberturas%' THEN 'CON guía anexos' ELSE 'sin guía' END AS Estado,
       LEN(SystemPrompt) AS LargoPrompt
FROM dbo.Agent WHERE Code = 'AGENTE_AUDITOR_MEDICINA';
