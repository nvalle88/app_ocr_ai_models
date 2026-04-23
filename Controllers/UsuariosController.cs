#region Using

using app_ocr_ai_models.Utils;
using app_tramites.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

#endregion

namespace SmartAdmin.Web.Controllers
{
    [Authorize]
    public class UsuariosController : Controller
    {
        private readonly UserManager<IdentityUser> userManager;
        private readonly RoleManager<IdentityRole> roleManager;

        public UsuariosController(UserManager<IdentityUser> userMgr, RoleManager<IdentityRole> roleMgr)
        {
            userManager = userMgr;
            roleManager = roleMgr;
        }

        public async Task<IActionResult> Index()
        {
            var users = await userManager.Users.OrderBy(u => u.Email).ToListAsync();
            return View(users);
        }

        public async Task<IActionResult> Manage(string id)
        {
            try
            {
                ViewBag.accion = string.IsNullOrEmpty(id) ? "Create" : "Edit";
                var rolesDisponibles = await roleManager.Roles.OrderBy(r => r.Name).ToListAsync();
                ViewBag.RolesDisponibles = rolesDisponibles;

                if (!string.IsNullOrEmpty(id))
                {
                    var user = await userManager.FindByIdAsync(id);
                    if (user == null)
                        return this.RedirectTo($"{Mensaje.Error}|{Mensaje.RecordNotFound}");

                    var rolesUsuario = await userManager.GetRolesAsync(user);
                    ViewBag.RolesUsuario = rolesUsuario;
                    return View(user);
                }

                ViewBag.RolesUsuario = new List<string>();
                return View(new IdentityUser());
            }
            catch (Exception)
            {
                return this.RedirectTo($"{Mensaje.Error}|{Mensaje.ErrorLoadData}");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Manage(string id, string email, string password, bool emailConfirmed, string[] roles)
        {
            try
            {
                var rolesDisponibles = await roleManager.Roles.OrderBy(r => r.Name).ToListAsync();
                ViewBag.RolesDisponibles = rolesDisponibles;

                if (string.IsNullOrEmpty(id))
                {
                    // Crear
                    ViewBag.accion = "Create";
                    if (string.IsNullOrEmpty(password))
                    {
                        ModelState.AddModelError("", "La contraseña es requerida para nuevos usuarios.");
                        ViewBag.RolesUsuario = new List<string>();
                        return View(new IdentityUser { Email = email, UserName = email });
                    }

                    var newUser = new IdentityUser
                    {
                        Email = email,
                        UserName = email,
                        EmailConfirmed = emailConfirmed
                    };

                    var result = await userManager.CreateAsync(newUser, password);
                    if (!result.Succeeded)
                    {
                        foreach (var error in result.Errors)
                            ModelState.AddModelError("", error.Description);
                        ViewBag.RolesUsuario = new List<string>();
                        return View(newUser);
                    }

                    if (roles != null && roles.Length > 0)
                        await userManager.AddToRolesAsync(newUser, roles);
                }
                else
                {
                    // Editar
                    ViewBag.accion = "Edit";
                    var user = await userManager.FindByIdAsync(id);
                    if (user == null)
                        return this.RedirectTo($"{Mensaje.Error}|{Mensaje.RecordNotFound}");

                    user.Email = email;
                    user.UserName = email;
                    user.EmailConfirmed = emailConfirmed;
                    await userManager.UpdateAsync(user);

                    if (!string.IsNullOrEmpty(password))
                    {
                        var token = await userManager.GeneratePasswordResetTokenAsync(user);
                        await userManager.ResetPasswordAsync(user, token, password);
                    }

                    // Actualizar roles
                    var currentRoles = await userManager.GetRolesAsync(user);
                    await userManager.RemoveFromRolesAsync(user, currentRoles);
                    if (roles != null && roles.Length > 0)
                        await userManager.AddToRolesAsync(user, roles);
                }

                return this.RedirectTo($"{Mensaje.MessaggeOK}|{Mensaje.Satisfactory}");
            }
            catch (Exception)
            {
                return this.RedirectTo($"{Mensaje.Error}|{Mensaje.Excepcion}");
            }
        }

        [HttpGet]
        public async Task<JsonResult> Delete(string id)
        {
            try
            {
                var user = await userManager.FindByIdAsync(id);
                if (user == null)
                    return Json(new { Estado = Constantes.ErrorState, Mensaje = Mensaje.RecordNotFound });

                var result = await userManager.DeleteAsync(user);
                if (!result.Succeeded)
                    return Json(new { Estado = Constantes.ErrorState, Mensaje = string.Join(", ", result.Errors.Select(e => e.Description)) });

                TempData["Mensaje"] = $"{Mensaje.MessaggeOK}|{Mensaje.Satisfactory}";
                return Json(new { Estado = Constantes.OKState, Mensaje = Mensaje.Satisfactory });
            }
            catch (Exception ex)
            {
                return Json(new { Estado = Constantes.ErrorState, Mensaje = ex.Message });
            }
        }

        [HttpGet]
        public async Task<JsonResult> ToggleLock(string id)
        {
            try
            {
                var user = await userManager.FindByIdAsync(id);
                if (user == null)
                    return Json(new { Estado = Constantes.ErrorState, Mensaje = Mensaje.RecordNotFound });

                if (await userManager.IsLockedOutAsync(user))
                {
                    await userManager.SetLockoutEndDateAsync(user, null);
                    return Json(new { Estado = Constantes.OKState, Mensaje = "Usuario desbloqueado correctamente.", Locked = false });
                }
                else
                {
                    await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
                    return Json(new { Estado = Constantes.OKState, Mensaje = "Usuario bloqueado correctamente.", Locked = true });
                }
            }
            catch (Exception ex)
            {
                return Json(new { Estado = Constantes.ErrorState, Mensaje = ex.Message });
            }
        }
    }
}
