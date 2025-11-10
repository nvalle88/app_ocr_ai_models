using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace app_tramites.Models.ModelAi;

public partial class Policys
{
    [Key]
    [Column(TypeName = "varchar(30)")]
    public string? Code { get; set; }

    [Required]
    [Column(TypeName = "NVARCHAR(100)")]
    public string? PolicyName { get; set; }

    // Propiedad de navegación: Una política tiene muchas reglas de acceso
    public virtual ICollection<AccessAgentPolicy> AccessAgentPolicies { get; set; } = new List<AccessAgentPolicy>();
}
