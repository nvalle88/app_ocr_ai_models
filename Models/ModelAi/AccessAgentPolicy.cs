using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace app_tramites.Models.ModelAi;

public class AccessAgentPolicy
{
    [Key]
    public int Id { get; set; }
    public bool Status { get; set; }
    [Required]
    [Column(TypeName = "varchar(30)")]
    public string PolicyCode { get; set; } = null!;
    public int AgentProcessId { get; set; }

    public virtual Policys? Policy { get; set; }
    public virtual AgentProcess? AgentProcess { get; set; }

}
