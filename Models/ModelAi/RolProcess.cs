using System.ComponentModel.DataAnnotations;

namespace app_tramites.Models.ModelAi;

public class RolProcess
{
    [Key]
    // Clave primaria IDENTITY (columna Id)
    public int Id { get; set; }

    // ----------------------------------------------------
    // Claves Foráneas y Única Compuesta

    // FK a Process (usamos ProcessCode tal como está en la DB)
    public string ProcessCode { get; set; } = string.Empty;

    // FK a IdentityRole (AspNetRoles)
    public string RolId { get; set; } = string.Empty;

    // ----------------------------------------------------
    // Propiedades de Navegación (OPCIONALES pero recomendadas)

    // Asume que tienes una entidad Process con propiedad 'Code' como PK
    public Process Process { get; set; } = null!;

    // Asume que usas IdentityRole como la clase de rol
    public AspNetRole Rol { get; set; } = null!;
}
