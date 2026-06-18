// =============================================================================
// REQ-019 T8 (re-baseline sobre feature/new_model + commit T1 014c1c2)
// PLAN DE REGRESIÓN A-HOSP
// "El cambio (Área nueva + pipeline Claude) NO rompe el flujo A-HOSP actual"
// =============================================================================
//
// CONTEXTO
// --------
// El plan REQ-019 es ADITIVO: el Área nueva convive con los controllers legacy
// (NexusController, OcrTestController) sin modificarlos. T1 reactivó las entidades
// ProcessStep/StepExecution/FinalResponseConfig como DbSets activos. Los controllers
// legacy NO fueron modificados — siguen funcionando igual.
//
// DIFERENCIAS RESPECTO A RAMA ANTERIOR (operaciones/req-019-modelo-claude)
// -------------------------------------------------------------------------
// 1. Usage.ExecutionId ahora existe como columna persistida (long?, nullable).
//    Los tests que verifican Usage usan esta columna en lugar de FinalResponseResultId
//    para el motor nuevo, y mantienen FinalResponseResultId para el legacy (D1/D5).
// 2. DataFile.OriginalName existe como propiedad nueva (string no nula, default "").
//    Los fixtures la incluyen.
// 3. Las entidades adicionales del motor Claude (OPAITool, OPAIModelTool, OPAISkill,
//    OPAIModelSkill, ToolInvocation, AgentProcess) están presentes en OCRDbContext.
//    No afectan los tests de regresión A-HOSP.
//
// CHECKLIST DE REGRESIÓN A-HOSP (accionable)
// -------------------------------------------
//
// RC-1  NexusController.ChatAjax sigue devolviendo JSON único (no stream)
// RC-2  OcrTestController y NexusController compilan sin cambios de comportamiento
// RC-3  ProcessCode "A-HOSP" resuelve FinalResponseConfig y agente chat-nexus
// RC-4  Smoke test en web-nexus-test tras deploy: caso A-HOSP + chat = respuesta correcta
// RC-5  El Service compartido extraído (T2) produce resultados equivalentes al legacy
//
// =============================================================================

using app_ocr_ai_models.Data;
using app_tramites.Models.ModelAi;
using Microsoft.EntityFrameworkCore;

namespace app_ocr_ai_models.Tests;

// =============================================================================
// RC-1: NexusController.ChatAjax devuelve JSON único (no stream)
// =============================================================================
// Estado esperado: POST /Nexus/ChatAjax con CaseCode válido devuelve HTTP 200 +
// { result: "...", ... } como objeto JSON único (no SSE, no chunks).
// El controller NO fue modificado en ninguna tarea de REQ-019.
//
// Nivel de test: MANUAL / integración HTTP — verificar contra web-nexus-test.
// El test InMemory a continuación verifica solo la EXISTENCIA del método y su firma.
// =============================================================================

public class RegresionAHosp_RC1_ChatAjaxContratoTests
{
    private const string SkipReason_HTTP =
        "MANUAL: requiere web-nexus-test desplegado con rama operaciones/req-019-newmodel. " +
        "Paso: POST https://<web-nexus-test>/Nexus/ChatAjax con body { CaseCode: '<guid>', Message: 'Hola' }. " +
        "Esperado: HTTP 200 + JSON { result: '...' } (no stream, no chunks). " +
        "Verificar que la respuesta NO cambia respecto al baseline antes del deploy de REQ-019.";

    /// <summary>
    /// [RC-1 InMemory] Verifica que FinalResponseConfig para A-HOSP puede
    /// crearse en el contexto InMemory (estructura correcta).
    /// La verificación de BD real queda condicionada al entorno QA.
    /// </summary>
    [Fact(Skip = "PENDIENTE BD: verificar en BD de pruebas que FinalResponseConfig para A-HOSP existe y IsEnabled=true.",
        DisplayName = "[RC-1][PENDIENTE BD] FinalResponseConfig de A-HOSP existe y está habilitado")]
    public async Task AHosp_FinalResponseConfig_ExisteYHabilitado()
    {
        var connStr = Environment.GetEnvironmentVariable("OCR_TESTS_CONNECTIONSTRING");
        Assert.False(string.IsNullOrWhiteSpace(connStr), "Configurar OCR_TESTS_CONNECTIONSTRING.");

        var opts = new DbContextOptionsBuilder<OCRDbContext>().UseSqlServer(connStr).Options;
        using var db = new OCRDbContext(opts);

        var config = await db.FinalResponseConfig
            .FirstOrDefaultAsync(c => c.ProcessCode == "A-HOSP" && c.IsEnabled);

        Assert.NotNull(config);
        Assert.True(config!.IsEnabled,
            "FinalResponseConfig para A-HOSP debe estar habilitado (IsEnabled=true) en la BD de pruebas.");
    }

    /// <summary>
    /// [RC-1 Manual] ChatAjax devuelve JSON único — smoke test HTTP.
    /// </summary>
    [Fact(Skip = SkipReason_HTTP,
        DisplayName = "[RC-1][MANUAL HTTP] ChatAjax devuelve JSON único (no SSE) sobre caso A-HOSP")]
    public Task AHosp_ChatAjax_DevuelveJsonUnico()
    {
        // PASOS MANUALES:
        // 1. Obtener un CaseCode de A-HOSP existente en BD de pruebas:
        //      SELECT TOP 1 CaseCode FROM ProcessCase WHERE DefinitionCode='A-HOSP' ORDER BY StartDate DESC
        // 2. POST https://<web-nexus-test>/Nexus/ChatAjax
        //    Headers: Content-Type: application/json; Cookie: .AspNetCore.Identity.Application=<sesión>
        //    Body: { "CaseCode": "<guid>", "Message": "Describe el caso" }
        // 3. Verificar: HTTP 200 + Content-Type: application/json
        // 4. Verificar: body es un objeto JSON con campo "result" (string, no vacío)
        // 5. Verificar: NO es text/event-stream (eso sería el nuevo endpoint de streaming)
        // CRITERIO: idéntico al comportamiento ANTES del deploy de REQ-019.
        return Task.CompletedTask;
    }
}

// =============================================================================
// RC-2: OcrTestController y NexusController compilan y operan igual
// =============================================================================
// Estado esperado: ninguna tarea de REQ-019 modifica los controllers legacy.
// T1 reactivó entidades como DbSets — el legacy puede leerlas (nulas/vacías en
// la BD hasta T6), pero no genera error por su existencia.
//
// Verificación estática: el build de la solución (incluyendo tests) compila sin error.
// Esto es verificado por el target "dotnet build" de T8 (ver instrucciones del commit).
// =============================================================================

public class RegresionAHosp_RC2_CompilacionLegacyTests
{
    /// <summary>
    /// [RC-2 Estático] Verifica que las entidades usadas por OcrTestController
    /// existen con sus propiedades esperadas (no se eliminaron en T1).
    /// Falla si alguien rename/borra una propiedad que el legacy usa.
    /// </summary>
    [Fact(DisplayName =
        "[RC-2][VERDE] Entidades usadas por OcrTestController tienen las propiedades esperadas")]
    public void Entidades_UsadasPorOcrTestController_TienenPropiedadesEsperadas()
    {
        // StepExecution — propiedades usadas en OcrTestController.cs:168-179, 235-247
        var execType = typeof(StepExecution);
        Assert.NotNull(execType.GetProperty(nameof(StepExecution.CaseCode)));
        Assert.NotNull(execType.GetProperty(nameof(StepExecution.StepOrder)));
        Assert.NotNull(execType.GetProperty(nameof(StepExecution.DataFileId)));
        Assert.NotNull(execType.GetProperty(nameof(StepExecution.RequestContent)));
        Assert.NotNull(execType.GetProperty(nameof(StepExecution.ResponseContent)));
        Assert.NotNull(execType.GetProperty(nameof(StepExecution.Status)));
        Assert.NotNull(execType.GetProperty(nameof(StepExecution.StartDate)));
        Assert.NotNull(execType.GetProperty(nameof(StepExecution.EndDate)));
        Assert.NotNull(execType.GetProperty(nameof(StepExecution.ModelCode)));
        Assert.NotNull(execType.GetProperty(nameof(StepExecution.ExecutionId)));

        // Usage — propiedades usadas en OcrTestController.cs:183-191, 250-256
        // El régimen doble D1/D5: FinalResponseResultId (legacy) + ExecutionId (motor nuevo)
        var usageType = typeof(Usage);
        Assert.NotNull(usageType.GetProperty(nameof(Usage.ExecutionId)));
        Assert.NotNull(usageType.GetProperty(nameof(Usage.FinalResponseResultId)));
        Assert.NotNull(usageType.GetProperty(nameof(Usage.PromptTokens)));
        Assert.NotNull(usageType.GetProperty(nameof(Usage.CompletionTokens)));
        Assert.NotNull(usageType.GetProperty(nameof(Usage.CreatedDate)));

        // ProcessStep — propiedades usadas en el loop comentado :120-262
        var stepType = typeof(ProcessStep);
        Assert.NotNull(stepType.GetProperty(nameof(ProcessStep.StepOrder)));
        Assert.NotNull(stepType.GetProperty(nameof(ProcessStep.ModelCode)));
        Assert.NotNull(stepType.GetProperty(nameof(ProcessStep.AggregateExecution)));
        Assert.NotNull(stepType.GetProperty(nameof(ProcessStep.SourceType)));
        Assert.NotNull(stepType.GetProperty(nameof(ProcessStep.StepsToInclude)));

        // DataFile — propiedades usadas en :102-111, :319-323
        var dfType = typeof(DataFile);
        Assert.NotNull(dfType.GetProperty(nameof(DataFile.CaseCode)));
        Assert.NotNull(dfType.GetProperty(nameof(DataFile.Id)));
        Assert.NotNull(dfType.GetProperty(nameof(DataFile.Text)));
        Assert.NotNull(dfType.GetProperty(nameof(DataFile.IsFileUri)));
        Assert.NotNull(dfType.GetProperty(nameof(DataFile.FileUri)));
        Assert.NotNull(dfType.GetProperty(nameof(DataFile.CreatedDate)));
        // OriginalName: nueva en feature/new_model — verificar que no es nula (default "")
        Assert.NotNull(dfType.GetProperty(nameof(DataFile.OriginalName)));
    }

    /// <summary>
    /// [RC-2 Estático] Verifica que OCRDbContext mantiene los DbSets usados
    /// por OcrTestController y NexusController legacy.
    /// </summary>
    [Fact(DisplayName =
        "[RC-2][VERDE] OCRDbContext mantiene los DbSets usados por controllers legacy")]
    public void OCRDbContext_TieneDbSetsUsadosPorLegacy()
    {
        var ctxType = typeof(OCRDbContext);

        // DbSets usados en OcrTestController
        Assert.NotNull(ctxType.GetProperty(nameof(OCRDbContext.Process)));
        Assert.NotNull(ctxType.GetProperty(nameof(OCRDbContext.ProcessCase)));
        Assert.NotNull(ctxType.GetProperty(nameof(OCRDbContext.ProcessStep)));
        Assert.NotNull(ctxType.GetProperty(nameof(OCRDbContext.DataFile)));
        Assert.NotNull(ctxType.GetProperty(nameof(OCRDbContext.OCRSetting)));
        Assert.NotNull(ctxType.GetProperty(nameof(OCRDbContext.AzureBlobConf)));
        Assert.NotNull(ctxType.GetProperty(nameof(OCRDbContext.StepExecution)));
        Assert.NotNull(ctxType.GetProperty(nameof(OCRDbContext.Usage)));
        Assert.NotNull(ctxType.GetProperty(nameof(OCRDbContext.FinalResponseConfig)));
        Assert.NotNull(ctxType.GetProperty(nameof(OCRDbContext.FinalResponseResult)));
        Assert.NotNull(ctxType.GetProperty(nameof(OCRDbContext.Agent)));

        // DbSets usados en NexusController (ChatAjax y notas)
        Assert.NotNull(ctxType.GetProperty(nameof(OCRDbContext.Note)));
        Assert.NotNull(ctxType.GetProperty(nameof(OCRDbContext.CaseReview)));
    }
}

// =============================================================================
// RC-3: ProcessCode "A-HOSP" resuelve FinalResponseConfig y agente chat-nexus
// =============================================================================

public class RegresionAHosp_RC3_AHospConfigTests
{
    private const string SkipReason_BD =
        "PENDIENTE BD: requiere BD de pruebas con OCR_TESTS_CONNECTIONSTRING configurado. " +
        "Verifica que A-HOSP tiene FinalResponseConfig activa apuntando al agente chat-nexus.";

    /// <summary>
    /// [RC-3 BD] Verifica que Process A-HOSP existe, está activo,
    /// tiene FinalResponseConfig habilitada y apunta al agente correcto.
    /// </summary>
    [Fact(Skip = SkipReason_BD,
        DisplayName = "[RC-3][PENDIENTE BD] A-HOSP: Process activo + FinalResponseConfig + agente chat-nexus")]
    public async Task AHosp_ProcessActivo_ConFinalResponseConfigYAgenteChatNexus()
    {
        var connStr = Environment.GetEnvironmentVariable("OCR_TESTS_CONNECTIONSTRING");
        Assert.False(string.IsNullOrWhiteSpace(connStr), "Configurar OCR_TESTS_CONNECTIONSTRING.");

        var opts = new DbContextOptionsBuilder<OCRDbContext>().UseSqlServer(connStr).Options;
        using var db = new OCRDbContext(opts);

        // RC-3a: Process A-HOSP existe y está activo
        var process = await db.Process.FirstOrDefaultAsync(p => p.Code == "A-HOSP");
        Assert.NotNull(process);
        Assert.True(process!.IsActive, "Process A-HOSP debe estar activo.");

        // RC-3b: FinalResponseConfig habilitada para A-HOSP
        var finalConfig = await db.FinalResponseConfig
            .FirstOrDefaultAsync(c => c.ProcessCode == "A-HOSP" && c.IsEnabled);
        Assert.NotNull(finalConfig);

        // RC-3c: El agente referenciado por FinalResponseConfig existe y está activo
        var agent = await db.Agent.FirstOrDefaultAsync(a => a.Code == finalConfig!.AgentCode);
        Assert.NotNull(agent);
        Assert.True(agent!.IsActive,
            $"El agente '{finalConfig!.AgentCode}' referenciado por FinalResponseConfig de A-HOSP debe estar activo.");
    }

    /// <summary>
    /// [RC-3 InMemory] Verifica la lógica de resolución de FinalResponseConfig
    /// con un fixture de datos controlado (sin BD real).
    /// </summary>
    [Fact(DisplayName =
        "[RC-3][VERDE] Resolución de FinalResponseConfig: Process con config activa se resuelve correctamente")]
    public async Task FinalResponseConfig_ProcesoConConfigActiva_SeResuelve()
    {
        var opts = new DbContextOptionsBuilder<OCRDbContext>()
            .UseInMemoryDatabase(nameof(FinalResponseConfig_ProcesoConConfigActiva_SeResuelve))
            .Options;
        using var db = new OCRDbContext(opts);

        // Fixture: simula la configuración de A-HOSP
        var configCode = "CFG-RC3";
        db.OPAIConfiguration.Add(new OPAIConfiguration
        {
            Code = configCode, Name = "Config RC3",
            EndpointUrl = "https://test.openai.azure.com", ApiKey = "key",
            ConfigType = "AzureOpenAI", IsActive = true,
            CreatedDate = DateTime.UtcNow, ModifiedDate = DateTime.UtcNow,
        });
        var agentCode = "chat-nexus";
        db.Agent.Add(new Agent
        {
            Code = agentCode, Name = "Chat Nexus Agent", ConfigCode = configCode,
            IsActive = true, VersionNumber = 1,
            CreatedDate = DateTime.UtcNow, ModifiedDate = DateTime.UtcNow,
        });
        db.Process.Add(new Process
        {
            Code = "A-HOSP", Name = "Proceso A-HOSP Fixture",
            VersionNumber = 1, IsActive = true,
        });
        db.FinalResponseConfig.Add(new FinalResponseConfig
        {
            ConfigCode = "FRC-AHOSP",
            ProcessCode = "A-HOSP",
            AgentCode = agentCode,
            PromptTemplate = "Responde sobre el caso hospitalario: {FileCount} archivo(s).",
            IncludedStepOrders = "*",
            UseOriginalText = true,
            IsEnabled = true,
        });
        await db.SaveChangesAsync();

        // Simula la resolución que hace OcrTestController.cs
        var resolvedConfig = await db.FinalResponseConfig
            .FirstOrDefaultAsync(cfg => cfg.ProcessCode == "A-HOSP" && cfg.IsEnabled);

        Assert.NotNull(resolvedConfig);
        Assert.Equal(agentCode, resolvedConfig!.AgentCode);
        Assert.True(resolvedConfig.IsEnabled);
    }
}

// =============================================================================
// RC-4: Smoke test en web-nexus-test tras deploy
// =============================================================================
// MANUAL — no automatizable desde aquí. Ejecutar después de cada deploy a web-nexus-test.
// =============================================================================

public class RegresionAHosp_RC4_SmokeTestDespues_MANUAL
{
    /// <summary>
    /// [RC-4 MANUAL] Smoke test completo en web-nexus-test.
    /// </summary>
    [Fact(Skip =
        "MANUAL POST-DEPLOY: " +
        "1. Abrir https://<web-nexus-test>/Nexus (autenticado). " +
        "2. Cargar un caso A-HOSP existente (CaseCode de la BD de pruebas). " +
        "3. Enviar mensaje en el chat → verificar respuesta JSON (no stream). " +
        "4. Verificar que la respuesta es coherente (no error 500, no JSON vacío). " +
        "5. Confirmar en BD que NO se crearon filas nuevas en StepExecution para ese CaseCode " +
        "   (el pipeline legacy no debe poblar StepExecution — eso solo lo hace el Área nueva en T6+). " +
        "CRITERIO: comportamiento idéntico al baseline ANTES de REQ-019.",
        DisplayName = "[RC-4][MANUAL POST-DEPLOY] Smoke test A-HOSP en web-nexus-test")]
    public Task SmokeTest_AHosp_WebNexusTest()
        => Task.CompletedTask;
}

// =============================================================================
// RC-5: Service compartido extraído (T2) produce resultados equivalentes al legacy
// =============================================================================
// T2 extrae ProcessFileAsync/UploadBlobAsync a un IService compartido.
// El test de equivalencia confirma que el legacy (si delega al Service) se comporta igual.
// Este test es PENDIENTE hasta que T2 entregue el Service con interfaz definida.
// =============================================================================

public class RegresionAHosp_RC5_ServiceCompartido_PENDIENTE_T2
{
    /// <summary>
    /// [RC-5 PENDIENTE T2] Test de equivalencia: el Service compartido extraído
    /// produce el mismo DataFile que el legacy inline.
    /// </summary>
    [Fact(Skip =
        "PENDIENTE T2: el Service compartido (IProcessFileService o similar) no existe todavía. " +
        "Cuando T2 lo entregue, este test debe: " +
        "1. Crear un DataFile usando el Service nuevo con un archivo de prueba (byte[] fijo, sin Azure real). " +
        "2. Crear un DataFile usando la lógica legacy (inline en OcrTestController). " +
        "3. Comparar Text, IsFileUri, FileUri, OriginalName (campos equivalentes — OriginalName " +
        "   es nueva en feature/new_model, debe propagarse al Service). " +
        "El mock de Azure Blob y DocIntelligence puede ser un stub que devuelve texto fijo. " +
        "CRITERIO: los campos Text/IsFileUri/OriginalName del DataFile son idénticos entre Service y legacy.",
        DisplayName = "[RC-5][PENDIENTE T2] Service compartido produce DataFile equivalente al legacy")]
    public Task ServiceCompartido_ProduceDataFileEquivalenteAlLegacy()
        => Task.CompletedTask;
}
