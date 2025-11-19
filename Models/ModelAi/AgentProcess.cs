using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace app_tramites.Models.ModelAi
{
    public partial class AgentProcess
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [Column(TypeName = "varchar(30)")]
        public string DefinitionCode { get; set; } = null!;

        [Required]
        [Column(TypeName = "varchar(50)")]
        public string AgentCode { get; set; } = null!;

        public virtual Agent Agent { get; set; } = null!;
        public virtual Process Process { get; set; } = null!;
        public virtual ICollection<AccessAgentPolicy> AccessAgentPolicies { get; set; } = new List<AccessAgentPolicy>();
        public virtual ICollection<FinalResponseResult> FinalResponseResults { get; set; } = new List<FinalResponseResult>();
    }
}
