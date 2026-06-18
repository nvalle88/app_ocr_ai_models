// =============================================================================
// REQ-019 T8 (re-baseline sobre feature/new_model + commit T1 014c1c2)
// TEST ROJO: Riesgo 3 — Pipeline multi-paso comentado
// =============================================================================
//
// PROPÓSITO
// ---------
// Este test codifica el criterio de aceptación CA-2 del Plan de Trabajo REQ-019:
//
//   "Tras ejecutar un Process con ≥1 ProcessStep y un ProcessCase con DataFiles,
//    debe existir ≥1 StepExecution por paso ejecutado, y Usage correspondiente."
//
// ESTADO HOY (BASELINE — ROJO)
// ----------------------------
// El pipeline multi-paso está COMENTADO en Controllers/OcrTestController.cs:113-287.
// Como consecuencia directa:
//   - StepExecution NUNCA se puebla (0 filas para cualquier CaseCode procesado).
//   - Usage NUNCA se puebla con ExecutionId (0 filas vinculadas a StepExecution).
//
// DIFERENCIAS RESPECTO A RAMA ANTERIOR (operaciones/req-019-modelo-claude)
// -------------------------------------------------------------------------
// Las entidades ProcessStep / StepExecution / FinalResponseConfig se
// REACTIVARON (descomentadas) en T1 (commit 014c1c2) — ya son DbSets activos en
// OCRDbContext. Sin embargo, NINGÚN flujo las puebla aún:
//   - OcrTestController.cs:113-287: pipeline multi-paso sigue COMENTADO.
//   - Área nueva (motor Claude): no existe todavía (pendiente T2..T6).
// Por tanto el test rojo SIGUE siendo válido: tras "ejecutar" el flujo actual,
// StepExecution permanece en 0 filas.
//
// ENTIDAD Usage — RÉGIMEN DOBLE (D1/D5):
//   - Usage.FinalResponseResultId: FK legacy para el flujo A-HOSP (nullable).
//   - Usage.ExecutionId: FK canónico al motor nuevo, FK a StepExecution.ExecutionId
//     (long?, nullable para compatibilidad). El test rojo verifica esta columna.
//
// NOTA DE ENTORNO
// ---------------
// Tests usan EF Core InMemory — sin BD real. La prueba de integración completa
// con BD (Riesgo3_IntegracionConBD_PENDIENTE_ENTORNO_QA) queda condicionada al
// entorno QA con cadena de conexión real configurada.
//
// =============================================================================

using app_ocr_ai_models.Data;
using app_tramites.Models.ModelAi;
using Microsoft.EntityFrameworkCore;

namespace app_ocr_ai_models.Tests;

/// <summary>
/// Suite principal del test rojo de Riesgo 3.
/// Usa OCRDbContext con proveedor InMemory — sin BD real — para poder compilar
/// y ejecutarse en CI/CD sin cadena de conexión.
/// </summary>
public class Riesgo3_PipelineMultiPasoTests
{
    // -------------------------------------------------------------------------
    // Helper: construye un OCRDbContext InMemory con datos mínimos de fixture
    // -------------------------------------------------------------------------
    private static OCRDbContext BuildInMemoryContext(string dbName)
    {
        var opts = new DbContextOptionsBuilder<OCRDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new OCRDbContext(opts);
    }

    // -------------------------------------------------------------------------
    // TEST ROJO PRINCIPAL (REQ-019 CA-2)
    // Nombre: StepExecution_SePopulaPorCadaPasoEjecutado
    //
    // HOY FALLA porque el pipeline multi-paso está comentado en
    // OcrTestController.cs:113-287 y NADA crea filas en StepExecution.
    //
    // PASARÁ A VERDE cuando T6 reimplemente el pipeline en el Área nueva y
    // pueble StepExecution/Usage por cada ProcessStep ejecutado.
    // -------------------------------------------------------------------------
    [Fact(DisplayName =
        "[ROJO REQ-019 CA-2] Tras ejecutar pipeline, StepExecution tiene ≥1 fila por ProcessStep ejecutado")]
    public async Task StepExecution_SePopulaPorCadaPasoEjecutado()
    {
        // ---- ARRANGE -------------------------------------------------------
        // Datos de fixture: Process con 2 pasos, ProcessCase con 1 DataFile.
        // Representan el escenario real mínimo del criterio de aceptación CA-2.

        using var db = BuildInMemoryContext(nameof(StepExecution_SePopulaPorCadaPasoEjecutado));

        var configCode = "CFG-TEST";
        var config = new OPAIConfiguration
        {
            Code = configCode,
            Name = "Config Test",
            EndpointUrl = "https://test.openai.azure.com",
            ApiKey = "test-key",
            ConfigType = "AzureOpenAI",
            IsActive = true,
            CreatedDate = DateTime.UtcNow,
            ModifiedDate = DateTime.UtcNow,
        };
        db.OPAIConfiguration.Add(config);

        var agentCode = "AGT-TEST";
        var agent = new Agent
        {
            Code = agentCode,
            Name = "Agente Test",
            ConfigCode = configCode,
            IsActive = true,
            VersionNumber = 1,
            CreatedDate = DateTime.UtcNow,
            ModifiedDate = DateTime.UtcNow,
        };
        db.Agent.Add(agent);

        var processCode = "PROC-TEST";
        var process = new Process
        {
            Code = processCode,
            Name = "Proceso Test REQ-019",
            Description = "Fixture para test rojo Riesgo 3",
            VersionNumber = 1,
            IsActive = true,
        };
        db.Process.Add(process);

        // 2 pasos — el pipeline debe generar 1 StepExecution por paso
        db.ProcessStep.AddRange(
            new ProcessStep
            {
                ProcessCode = processCode,
                StepOrder = 1,
                ModelCode = agentCode,
                StepName = "Paso 1 - Extracción",
                SourceType = InputSourceType.Original,
                AggregateExecution = false,
                StepsToInclude = 0,
            },
            new ProcessStep
            {
                ProcessCode = processCode,
                StepOrder = 2,
                ModelCode = agentCode,
                StepName = "Paso 2 - Clasificación",
                SourceType = InputSourceType.PreviousSteps,
                AggregateExecution = false,
                StepsToInclude = 1,
            }
        );

        var caseCode = Guid.NewGuid();
        db.ProcessCase.Add(new ProcessCase
        {
            CaseCode = caseCode,
            DefinitionCode = processCode,
            StartDate = DateTime.UtcNow,
            State = "Started",
        });

        db.DataFile.Add(new DataFile
        {
            CaseCode = caseCode,
            IsFileUri = false,
            FileUri = string.Empty,
            Text = "Texto OCR de prueba para fixture REQ-019",
            CreatedDate = DateTime.UtcNow,
            OriginalName = "fixture.pdf",
        });

        await db.SaveChangesAsync();

        // ---- ACT -----------------------------------------------------------
        // SIMULACIÓN DEL ESTADO ACTUAL (código comentado):
        // El pipeline multi-paso NO se ejecuta — OcrTestController.cs:113-287
        // está comentado. Por tanto ningún StepExecution se crea.
        //
        // En el escenario real (T6 implementado), el motor Claude poblaría
        // StepExecution/Usage aquí mediante IClaudePipelineService o similar.
        //
        // Para hacer el test ejecutable sin invocar el controller (que requiere
        // la cadena de DI completa de ASP.NET + Azure), el test verifica
        // directamente el estado de la BD después del "proceso".
        // Cuando T6 esté listo, este bloque de ACT llamará al servicio/command
        // correspondiente antes del assert.

        // (Ningún código actual puebla StepExecution — este es el estado rojo.)

        // ---- ASSERT --------------------------------------------------------
        // El criterio CA-2 exige: ≥1 StepExecution por paso ejecutado.
        // Los 2 pasos del fixture deben producir ≥2 filas de StepExecution.

        var stepCount = await db.ProcessStep
            .Where(s => s.ProcessCode == processCode)
            .CountAsync();

        var executionCount = await db.StepExecution
            .Where(e => e.CaseCode == caseCode)
            .CountAsync();

        // ESTE ASSERT FALLA HOY (rojo): executionCount == 0 porque el pipeline
        // está comentado. Pasará a verde cuando T6 implemente el pipeline.
        Assert.True(
            executionCount >= stepCount,
            $"[ROJO] Se esperaban ≥{stepCount} StepExecution(s) para CaseCode={caseCode} " +
            $"(uno por ProcessStep ejecutado), pero se encontraron {executionCount}. " +
            $"CAUSA: el pipeline multi-paso en OcrTestController.cs:113-287 está comentado; " +
            $"StepExecution nunca se puebla. Este test pasará a verde cuando T6 " +
            $"reimplemente el pipeline en el Área nueva (REQ-019)."
        );
    }

    // -------------------------------------------------------------------------
    // TEST ROJO COMPLEMENTARIO — Usage.ExecutionId
    // Verifica que cada StepExecution tenga su Usage con ExecutionId registrado.
    // También falla hoy por el mismo motivo.
    //
    // NOTA: Usage tiene régimen doble D1/D5:
    //   - Usage.FinalResponseResultId: FK legacy (flujo A-HOSP, nullable)
    //   - Usage.ExecutionId: FK nuevo (StepExecution.ExecutionId, long?, nullable)
    // Este test verifica la columna ExecutionId del motor nuevo.
    // -------------------------------------------------------------------------
    [Fact(DisplayName =
        "[ROJO REQ-019 CA-2] Cada StepExecution generado debe tener ≥1 Usage con ExecutionId registrado")]
    public async Task Usage_SeRegistraPorCadaStepExecution()
    {
        using var db = BuildInMemoryContext(nameof(Usage_SeRegistraPorCadaStepExecution));

        // Arrange mínimo para el assert
        var configCode = "CFG-USG";
        db.OPAIConfiguration.Add(new OPAIConfiguration
        {
            Code = configCode, Name = "Config Usage Test",
            EndpointUrl = "https://test.openai.azure.com", ApiKey = "key",
            ConfigType = "AzureOpenAI", IsActive = true,
            CreatedDate = DateTime.UtcNow, ModifiedDate = DateTime.UtcNow,
        });
        var agentCode = "AGT-USG";
        db.Agent.Add(new Agent
        {
            Code = agentCode, Name = "Agente Usage", ConfigCode = configCode,
            IsActive = true, VersionNumber = 1,
            CreatedDate = DateTime.UtcNow, ModifiedDate = DateTime.UtcNow,
        });
        var processCode = "PROC-USG";
        db.Process.Add(new Process
        {
            Code = processCode, Name = "Proceso Usage Test",
            VersionNumber = 1, IsActive = true,
        });
        db.ProcessStep.Add(new ProcessStep
        {
            ProcessCode = processCode, StepOrder = 1, ModelCode = agentCode,
            StepName = "Paso 1", SourceType = InputSourceType.Original,
        });
        var caseCode = Guid.NewGuid();
        db.ProcessCase.Add(new ProcessCase
        {
            CaseCode = caseCode, DefinitionCode = processCode,
            StartDate = DateTime.UtcNow, State = "Started",
        });
        await db.SaveChangesAsync();

        // Act: nada inserta StepExecution/Usage en el código actual (pipeline comentado)

        // Assert: cada StepExecution debe tener ≥1 Usage vinculado por ExecutionId
        var executions = await db.StepExecution
            .Where(e => e.CaseCode == caseCode)
            .ToListAsync();

        // FALLA HOY: executions.Count == 0 (pipeline comentado → sin StepExecution → sin Usage)
        Assert.True(
            executions.Count >= 1,
            $"[ROJO] No se encontró ningún StepExecution para CaseCode={caseCode}. " +
            $"El pipeline multi-paso (OcrTestController.cs:113-287) está comentado. " +
            $"Este assert pasará a verde cuando T6 esté implementado (REQ-019)."
        );

        foreach (var exec in executions)
        {
            // Verifica por Usage.ExecutionId (long?) — FK al motor nuevo.
            // El régimen doble D1/D5 mantiene también FinalResponseResultId para el legacy.
            var usageCount = await db.Usage
                .Where(u => u.ExecutionId == exec.ExecutionId)
                .CountAsync();
            Assert.True(
                usageCount >= 1,
                $"[ROJO] StepExecution ExecutionId={exec.ExecutionId} no tiene Usage " +
                $"vinculado por Usage.ExecutionId. " +
                $"Se espera ≥1 Usage por ejecución con tokens registrados (REQ-019 CA-2)."
            );
        }
    }

    // -------------------------------------------------------------------------
    // TEST DE INVARIANTE (VERDE HOY) — verifica que el fixture de datos compila
    // y el contexto InMemory funciona. No prueba el pipeline, solo la estructura.
    // -------------------------------------------------------------------------
    [Fact(DisplayName =
        "[VERDE] El contexto InMemory resuelve Process+ProcessStep+ProcessCase+DataFile sin error")]
    public async Task Fixture_ContextoInMemory_ResuelveEntidades()
    {
        using var db = BuildInMemoryContext(nameof(Fixture_ContextoInMemory_ResuelveEntidades));

        var configCode = "CFG-INV";
        db.OPAIConfiguration.Add(new OPAIConfiguration
        {
            Code = configCode, Name = "Config Invariante",
            EndpointUrl = "https://test.openai.azure.com", ApiKey = "key",
            ConfigType = "AzureOpenAI", IsActive = true,
            CreatedDate = DateTime.UtcNow, ModifiedDate = DateTime.UtcNow,
        });
        var agentCode = "AGT-INV";
        db.Agent.Add(new Agent
        {
            Code = agentCode, Name = "Agente Invariante", ConfigCode = configCode,
            IsActive = true, VersionNumber = 1,
            CreatedDate = DateTime.UtcNow, ModifiedDate = DateTime.UtcNow,
        });
        var processCode = "PROC-INV";
        db.Process.Add(new Process
        {
            Code = processCode, Name = "Proceso Invariante",
            VersionNumber = 1, IsActive = true,
        });
        db.ProcessStep.Add(new ProcessStep
        {
            ProcessCode = processCode, StepOrder = 1, ModelCode = agentCode,
            StepName = "Paso Invariante", SourceType = InputSourceType.Original,
        });
        var caseCode = Guid.NewGuid();
        db.ProcessCase.Add(new ProcessCase
        {
            CaseCode = caseCode, DefinitionCode = processCode,
            StartDate = DateTime.UtcNow, State = "Started",
        });
        db.DataFile.Add(new DataFile
        {
            CaseCode = caseCode, IsFileUri = false,
            FileUri = string.Empty, Text = "Texto OCR test",
            CreatedDate = DateTime.UtcNow, OriginalName = "test.pdf",
        });
        await db.SaveChangesAsync();

        var stepCount = await db.ProcessStep.Where(s => s.ProcessCode == processCode).CountAsync();
        var dataFileCount = await db.DataFile.Where(d => d.CaseCode == caseCode).CountAsync();

        Assert.Equal(1, stepCount);
        Assert.Equal(1, dataFileCount);
    }
}

// =============================================================================
// CLASE PENDIENTE DE ENTORNO QA CON BD DE PRUEBAS (db-nexus-test)
// =============================================================================
//
// CONDICIÓN: la clase siguiente NO se ejecuta en CI sin BD real.
// Para habilitarla en el entorno QA, configurar la variable de entorno:
//   OCR_TESTS_CONNECTIONSTRING = "Server=<srv>;Database=<db>;..."
// y descomentar el [Fact] o usar [Fact(Skip = ...)] cuando no haya BD.
//
// OBJETIVO: ejecutar el flujo completo (POST /OcrTest con Process A-HOSP o
// cualquier Process con ≥1 ProcessStep y archivos reales), luego verificar
// directamente en la BD de pruebas que StepExecution tiene ≥1 fila.
//
// Esta validación complementa el test InMemory: mientras el test rojo de arriba
// prueba la LÓGICA del invariante, este test de integración probaría el PIPELINE
// REAL end-to-end contra la BD de pruebas de web-nexus-test.
//
// PENDIENTE ENTORNO: la cadena de conexión a db-nexus-test no está configurada
// en este entorno. Ver trace REQ-019 para instrucciones de configuración.
//
// INSTRUCCIONES PARA EL QA EN ENTORNO BD
// ----------------------------------------
// 1. Desplegar la rama operaciones/req-019-newmodel en web-nexus-test.
// 2. Configurar la cadena de conexión real en appsettings o variable de entorno.
// 3. Ejecutar: dotnet test --filter "IntegracionBD" desde el directorio de tests.
// 4. Verificar que el test falla con el baseline (pipeline comentado).
// 5. Tras T6 (pipeline implementado), volver a correr y confirmar verde.
//
// =============================================================================

/// <summary>
/// Suite de integración contra BD real — CONDICIONADA al entorno QA con BD de pruebas.
/// Todos los tests están marcados como Skip hasta que el entorno esté disponible.
/// </summary>
public class Riesgo3_IntegracionConBD_PENDIENTE_ENTORNO_QA
{
    private const string SkipReason =
        "PENDIENTE ENTORNO QA: requiere BD de pruebas (db-nexus-test en SQL Server). " +
        "Configurar OCR_TESTS_CONNECTIONSTRING y ejecutar manualmente con: " +
        "dotnet test --filter IntegracionBD. Ver trace REQ-019 para instrucciones.";

    /// <summary>
    /// [ROJO] Integración end-to-end: tras una invocación real del pipeline sobre
    /// un ProcessCase con DataFiles, StepExecution debe tener ≥1 fila por paso.
    /// HOY FALLA porque el pipeline está comentado (OcrTestController.cs:113-287).
    /// </summary>
    [Fact(Skip = SkipReason,
        DisplayName = "[PENDIENTE BD][ROJO] Pipeline end-to-end: StepExecution poblado en BD real")]
    public async Task IntegracionBD_PipelineEndToEnd_StepExecutionPoblado()
    {
        var connStr = Environment.GetEnvironmentVariable("OCR_TESTS_CONNECTIONSTRING");
        Assert.False(string.IsNullOrWhiteSpace(connStr),
            "Configurar la variable de entorno OCR_TESTS_CONNECTIONSTRING.");

        var opts = new DbContextOptionsBuilder<OCRDbContext>()
            .UseSqlServer(connStr)
            .Options;

        using var db = new OCRDbContext(opts);

        // Usar un Process ya existente en la BD de pruebas con ≥1 ProcessStep.
        // El QA debe ajustar "A-HOSP" u otro ProcessCode según lo que exista en pruebas.
        var targetProcessCode = "A-HOSP";

        var stepCount = await db.ProcessStep
            .Where(s => s.ProcessCode == targetProcessCode)
            .CountAsync();

        Assert.True(stepCount >= 1,
            $"El Process '{targetProcessCode}' debe tener ≥1 ProcessStep en la BD de pruebas.");

        // ASSERT: buscar el CaseCode más reciente de A-HOSP y verificar StepExecution.
        var lastCase = await db.ProcessCase
            .Where(c => c.DefinitionCode == targetProcessCode)
            .OrderByDescending(c => c.StartDate)
            .FirstOrDefaultAsync();

        Assert.NotNull(lastCase);

        var executionCount = await db.StepExecution
            .Where(e => e.CaseCode == lastCase!.CaseCode)
            .CountAsync();

        // FALLA HOY (rojo): pipeline comentado → 0 filas.
        // VERDE tras T6: pipeline implementado → ≥stepCount filas.
        Assert.True(
            executionCount >= stepCount,
            $"[ROJO ESPERADO HASTA T6] StepExecution tiene {executionCount} fila(s) " +
            $"pero se esperan ≥{stepCount} (una por ProcessStep de '{targetProcessCode}'). " +
            $"CaseCode consultado: {lastCase?.CaseCode}. " +
            $"CAUSA: pipeline OcrTestController.cs:113-287 está comentado (REQ-019 Riesgo 3)."
        );
    }
}
