/* =============================================================================
   REQ-022b - Enganchar cobertura_beneficio_plan a los agentes
   -----------------------------------------------------------------------------
   Una tool que existe y no esta enlazada no se llama nunca. Es exactamente lo
   que le paso al CodigoBeneficio: el homologador lo calculaba desde REQ-019r y
   no llegaba a ninguna parte.

   Va a los tres agentes que resuelven un reembolso, porque los tres necesitan
   el mismo numero y tiene que ser EL MISMO:

       AGENTE_AUDITOR_MEDICINA   la bandeja del auditor
       AGENTE_CLAUDE             el motor de resolucion del portal
       AGENTE_PORTAL_CLIENTE     lo que se le explica al afiliado

   Si el portal calculara su porcentaje por su cuenta, el afiliado veria una
   cifra y el auditor otra sobre el mismo caso.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @tool nvarchar(100) = N'cobertura_beneficio_plan';

IF NOT EXISTS (SELECT 1 FROM dbo.OPAITool WHERE Code = @tool)
BEGIN
    RAISERROR('Falta la tool cobertura_beneficio_plan. Correr antes Req022a.', 16, 1);
    RETURN;
END

DECLARE @agentes TABLE (Code nvarchar(100));
INSERT @agentes (Code) VALUES
    (N'AGENTE_AUDITOR_MEDICINA'), (N'AGENTE_CLAUDE'), (N'AGENTE_PORTAL_CLIENTE');

/* Ojo: la columna se llama [Order] -palabra reservada- e IsEnabled, no IsActive. */
INSERT dbo.OPAIModelTool (ModelCode, ToolCode, [Order], IsEnabled)
SELECT a.Code, @tool,
       ISNULL((SELECT MAX(mt.[Order]) FROM dbo.OPAIModelTool mt
                WHERE mt.ModelCode = a.Code), 0) + 1,
       1
  FROM @agentes a
 WHERE EXISTS (SELECT 1 FROM dbo.Agent g WHERE g.Code = a.Code)
   AND NOT EXISTS (SELECT 1 FROM dbo.OPAIModelTool mt
                    WHERE mt.ModelCode = a.Code AND mt.ToolCode = @tool);

SELECT mt.ModelCode, mt.ToolCode, mt.[Order], mt.IsEnabled
  FROM dbo.OPAIModelTool mt WHERE mt.ToolCode = @tool ORDER BY mt.ModelCode;
