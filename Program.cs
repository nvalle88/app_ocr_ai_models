using app_ocr_ai_models.Data;
using app_ocr_ai_models.Services;
using Core;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace app_ocr_ai_models
{
    public class Program
    {
        public static void Main(string[] args)
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
                options.ClaimsIdentity.UserIdClaimType = "/Account/Login";
            });
            builder.Services.AddControllersWithViews();

            builder.Services.AddTransient<IEmailSender, EmailSender>();

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

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");
            app.MapRazorPages();

            app.Run();
        }
    }
}
