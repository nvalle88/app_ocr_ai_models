using app_ocr_ai_models.Data;
using app_ocr_ai_models.Services;
using app_ocr_ai_models.Services.Documents;
using app_ocr_ai_models.Services.Zendesk;
using app_tramites.Data;
using app_tramites.Services.Ai;
using app_tramites.Services.Ai.Tools;
using app_tramites.Services.Graph;
using app_tramites.Services.NexusProcess;
using app_tramites.Utils;
using Core;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace app_ocr_ai_models
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            var ocrAiConnection = builder.Configuration.GetConnectionString("OcrAiConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

            builder.Services.AddDbContext<OCRDbContext>(options =>
                options.UseSqlServer(ocrAiConnection));

            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(connectionString));
           
            builder.Services.AddDatabaseDeveloperPageExceptionFilter();

            builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = false)
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>();

            // Llaves de Data Protection persistidas: sin esto se regeneran en cada
            // arranque y toda cookie de sesión / token antiforgery emitido antes del
            // reinicio queda inválido (el login se rechaza en silencio y la página
            // solo se repinta). Con carpeta fija, la sesión sobrevive al reinicio.
            var keysDir = new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys"));
            keysDir.Create();
            builder.Services.AddDataProtection()
                .PersistKeysToFileSystem(keysDir)
                .SetApplicationName("app_ocr_ai_models");


            builder.Services.AddApplicationInsightsTelemetry();

            builder.Services.ConfigureApplicationCookie(options =>
            {
                // Especifica la ruta personalizada de inicio de sesi�n
                options.LoginPath = "/Account/Login";
            });

            builder.Services.Configure<IdentityOptions>(options =>
            {
                // Other settings can go here
                options.ClaimsIdentity.UserIdClaimType = ClaimTypes.NameIdentifier; ;
            });
            builder.Services.AddControllersWithViews();
            builder.Services.AddControllers(); // <-- registrar controllers para APIs

            builder.Services.AddTransient<IEmailSender, EmailSender>();
            builder.Services.AddTransient<INexusService, NexusService>();
            builder.Services.AddMemoryCache();
            // REQ-019r: homologa el texto de la factura contra Salud.dbo.Lr05 y valida Lr46
            // La comprobacion de factura repetida NO pasa por el agente: se
            // pregunta directamente, en cuanto el documento se identifica.
            builder.Services.AddScoped<app_ocr_ai_models.Services.IBuscadorFacturaRepetida,
                                       app_ocr_ai_models.Services.BuscadorFacturaRepetida>();

            // Comprobar la factura contra el SRI. El BLOQUEO por "no existe en el
            // SRI" esta apagado por defecto -Saludsa:BloquearSiNoEstaEnSri- porque
            // hoy la respuesta de pruebas no lo permite: ver VerificadorSri.cs.
            builder.Services.AddScoped<app_ocr_ai_models.Services.IVerificadorSri,
                                       app_ocr_ai_models.Services.VerificadorSri>();

            // Lo que se consulta SI o SI -contrato, preexistencias, convenio- se
            // lanza en paralelo ANTES de la primera llamada al modelo, para no
            // pagar una ida y vuelta por cada una. Medido: 7,3 herramientas por
            // ejecucion = 8 viajes = 115 segundos.
            builder.Services.AddScoped<app_ocr_ai_models.Services.Ai.IPreValidaciones,
                                       app_ocr_ai_models.Services.Ai.PreValidaciones>();

            // El modelo saca los codigos y estructura los documentos; con esos
            // hechos, QUE CUBRE EL PLAN se consulta y se calcula, no se le
            // pregunta a nadie.
            builder.Services.AddScoped<app_ocr_ai_models.Services.IEvaluadorDeCobertura,
                                       app_ocr_ai_models.Services.EvaluadorDeCobertura>();

            builder.Services.AddScoped<app_ocr_ai_models.Services.Ai.IHomologadorProcedimientos,
                                        app_ocr_ai_models.Services.Ai.HomologadorProcedimientos>();
            builder.Services.AddScoped<IOcrIngestService, OcrIngestService>();
            builder.Services.AddScoped<IZendeskClient, ZendeskClient>(); // REQ-019 T3

            // REQ-019 T22: proveedores documentales (IDocumentSourceProvider + Armonix)
            // ZendeskDocumentProvider se registra con nombre explícito; Armonix directamente.
            builder.Services.AddScoped<ZendeskDocumentProvider>();
            builder.Services.AddScoped<ArmonixDocumentProvider>();

            // REQ-019 T6: motor Claude — factory de proveedor IA + orquestador multi-paso
            builder.Services.AddSingleton<AiCompletionServiceFactory>();
            builder.Services.AddScoped<IProcessOrchestrator, ProcessOrchestrator>();

            // REQ-019 T5: capa de tools (function calling directo)
            // B2/T0b: SaludsaTokenProvider falla en runtime si no hay credenciales (no en startup)
            // B1/T0a: InternalApiToolExecutor falla en runtime si las baseUrl no están configuradas
            // REQ-020g — Las APIs de Saludsa cierran las conexiones ociosas antes
            // de que el pool de HttpClient se entere, y al reutilizar un socket
            // muerto la llamada revienta con "The response ended prematurely".
            // Se midió en vivo contra el endpoint de token: Python funciona
            // porque urllib abre conexión nueva cada vez; HttpClient no.
            // Con un idle timeout corto el pool descarta la conexión antes de
            // que el servidor la cierre por su cuenta.
            static SocketsHttpHandler PoolSano() => new()
            {
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(20),
                PooledConnectionLifetime    = TimeSpan.FromMinutes(2),
                ConnectTimeout              = TimeSpan.FromSeconds(15)
            };

            builder.Services.AddHttpClient("SaludsaOAuth2", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            }).ConfigurePrimaryHttpMessageHandler(PoolSano);

            builder.Services.AddHttpClient("SaludsaInternalApi", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(60);
            }).ConfigurePrimaryHttpMessageHandler(PoolSano);
            builder.Services.AddSingleton<ISaludsaTokenProvider, SaludsaTokenProvider>();
            builder.Services.AddSingleton<IToolAuthorizationGuard, ToolAuthorizationGuard>();
            builder.Services.AddScoped<IToolExecutor, InternalApiToolExecutor>();

            // REQ-020: el portal del afiliado resuelve sus contratos por código,
            // no preguntándole al modelo. Va sobre el mismo IToolExecutor para
            // heredar el token, el guardián D2 y la auditoría de ToolInvocation.
            builder.Services.AddScoped<app_ocr_ai_models.Services.Ai.PortalClienteService>();

            // REQ-019 T19/T20/T21: grafo de conocimiento Neo4j (bloqueo B6)
            // Neo4jGraphService falla en runtime si Neo4j:Uri/User/Password no están configurados;
            // no falla en startup. IAsyncDisposable se gestiona por el contenedor DI.
            builder.Services.AddSingleton<IGraphService, Neo4jGraphService>();

            // REQ-038: las credenciales de Zendesk se leen de la tabla de
            // parametros, no de appsettings: ahi se pueden rotar sin desplegar.
            builder.Services.AddSingleton<app_tramites.Services.Zendesk.ParametrosZendesk>();
            builder.Services.AddScoped<IGraphExtractionService, GraphExtractionService>();
            builder.Services.AddScoped<GraphToolExecutor>();

            // opcional: CORS para permitir llamadas desde Postman/otros clientes
            // REQ-019: CORS AllowAnyOrigin es deuda preexistente — restringir a orígenes conocidos (fuera de alcance)
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy => policy
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader());
            });

            builder.Services.AddHttpClient("FileDownloaderClient", client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("app_tramites-file-downloader/1.0");
                client.Timeout = TimeSpan.FromMinutes(2);
            });
            builder.Services.AddSingleton<FileDownloader>();

            var app = builder.Build();

            var telemetryClient = app.Services.GetRequiredService<TelemetryClient>();
            LoggerService.Configure(telemetryClient);

            app.UseMiddleware<LoggingMiddleware>();
            app.UseMiddleware<IdTransaccionMiddleware>();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseMigrationsEndPoint();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseCors(options =>
            {
                options.AllowAnyOrigin();
                options.AllowAnyMethod();
                options.AllowAnyHeader();

            });

            // REQ-019 T4: routing de áreas (aditivo; agrega Studio sin tocar el default)
            app.MapControllerRoute(
                name: "areas",
                pattern: "{area:exists}/{controller=Sobres}/{action=Index}/{id?}");
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");
            app.MapRazorPages();
            app.MapControllers(); // <-- mapear rutas de API/Controllers

            // =======================================================
            // === BLOQUE DE INICIALIZACI�N DE DATOS (SEEDING) =======
            // =======================================================

            // Utilizamos un bloque try-catch para manejar errores durante la inicializaci�n
            try
            {
                var scope = app.Services.CreateScope();
                var services = scope.ServiceProvider;

                // Ejecutar la inicializaci�n de roles y usuarios de forma as�ncrona
                //await DataSeeder.SeedRolesAsync(services);
                //await DataSeeder.SeedAdminUserAsync(services);

                // Opcional: Registrar que el Seeding fue exitoso
                LoggerService.LogInformation("Seeding de datos y roles completado con �xito.");
            }
            catch (Exception ex)
            {
                // Capturar y registrar cualquier error de inicializaci�n
                LoggerService.LogErrorMensaje("Ocurri� un error durante el Seeding de datos.");
            }

            // =======================================================
            // === FIN DEL BLOQUE DE INICIALIZACI�N ==================
            // =======================================================


            app.Run();
        }
    }
}
