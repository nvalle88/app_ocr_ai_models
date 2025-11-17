using app_ocr_ai_models.Data;
using app_ocr_ai_models.Services;
using app_tramites.Data;
using app_tramites.Services.NexusProcess;
using Core;
using Microsoft.ApplicationInsights;
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


            builder.Services.AddApplicationInsightsTelemetry();

            builder.Services.ConfigureApplicationCookie(options =>
            {
                // Especifica la ruta personalizada de inicio de sesión
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

            // opcional: CORS para permitir llamadas desde Postman/otros clientes
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy => policy
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader());
            });

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

            app.UseCors();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");
            app.MapRazorPages();
            app.MapControllers(); // <-- mapear rutas de API/Controllers

            // =======================================================
            // === BLOQUE DE INICIALIZACIÓN DE DATOS (SEEDING) =======
            // =======================================================

            // Utilizamos un bloque try-catch para manejar errores durante la inicialización
            try
            {
                var scope = app.Services.CreateScope();
                var services = scope.ServiceProvider;

                // Ejecutar la inicialización de roles y usuarios de forma asíncrona
                await DataSeeder.SeedRolesAsync(services);
                await DataSeeder.SeedAdminUserAsync(services);

                // Opcional: Registrar que el Seeding fue exitoso
                LoggerService.LogInformation("Seeding de datos y roles completado con éxito.");
            }
            catch (Exception ex)
            {
                // Capturar y registrar cualquier error de inicialización
                LoggerService.LogErrorMensaje("Ocurrió un error durante el Seeding de datos.");
            }

            // =======================================================
            // === FIN DEL BLOQUE DE INICIALIZACIÓN ==================
            // =======================================================


            app.Run();
        }
    }
}
