using Microsoft.AspNetCore.Identity;

namespace app_tramites.Data;

public class DataSeeder
{
    // Método para asegurar que los roles existen en la base de datos
    public static async Task SeedRolesAsync(IServiceProvider serviceProvider)
    {
        var RoleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        string[] roleNames = { "Administrador", "UsuarioEstandar", "Editor" };

        foreach (var roleName in roleNames)
        {
            if (!await RoleManager.RoleExistsAsync(roleName))
            {
                await RoleManager.CreateAsync(new IdentityRole(roleName));
            }
        }
    }

    // Método para crear un usuario administrador y asignarle un rol
    public static async Task SeedAdminUserAsync(IServiceProvider serviceProvider)
    {
        // Reemplaza 'IdentityUser' si usas una clase de usuario personalizada (ej. ApplicationUser)
        var UserManager = serviceProvider.GetRequiredService<UserManager<IdentityUser>>();

        // Define las credenciales del usuario administrador
        string adminUserName = "yvalle@saludsa.com.ec"; // Usar un email real
        string adminPassword = "TuPasswordSeguro123!"; // ¡Cambiar en producción!
        string adminRole = "Administrador";

        // Busca si el usuario ya existe
        var user = await UserManager.FindByNameAsync(adminUserName);

        if (user == null)
        {
            // 1. Crea la instancia del usuario
            user = new IdentityUser
            {
                UserName = adminUserName,
                Email = adminUserName,
                EmailConfirmed = true // Asumimos que está confirmado para el admin
            };

            // 2. Intenta crear el usuario
            var result = await UserManager.CreateAsync(user, adminPassword);

            if (result.Succeeded)
            {
                // 3. Asigna el rol de administrador
                await UserManager.AddToRoleAsync(user, adminRole);
            }
        }
        else
        {
            // Solo asegura que si ya existe, tenga el rol asignado
            if (!await UserManager.IsInRoleAsync(user, adminRole))
            {
                await UserManager.AddToRoleAsync(user, adminRole);
            }
        }
    }
}
