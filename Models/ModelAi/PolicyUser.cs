using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace app_tramites.Models.ModelAi;

public class PolicyUser
{
    [Key]
    // Clave primaria IDENTITY (columna Id)
    public int Id { get; set; }

    // ----------------------------------------------------
    // Claves Foráneas y Única Compuesta

    // FK a Policy (usamos PolicyCode tal como está en la DB)
    [Column("PolicyCode")]
    public string PolicyCode { get; set; } = string.Empty;

    // FK a IdentityUser (AspNetUsers)
    public string UserId { get; set; } = string.Empty;

    // ----------------------------------------------------
    // Propiedades de Navegación (OPCIONALES pero recomendadas)

    // Asume que tienes una entidad Policy con propiedad 'Code' como PK
    public Policys Policys { get; set; } = null!;

    // Asume que usas IdentityUser como la clase de usuario
    public IdentityUser User { get; set; } = null!;
}
