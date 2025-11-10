using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace app_tramites.Models.ModelAi
{
    

    // ================================================
    // EF Core Entity: FinalResponseResult
    // ================================================
    public class FinalResponseResult
    {
        [Key]
        public long Id { get; set; }

        [Required]
        public Guid CaseCode { get; set; }

        public int? FileId { get; set; } // NULL para resumen global

        [Required]
        [StringLength(50)]
        public string ConfigCode { get; set; } = null!;

        [Required]
        public string ResponseText { get; set; } = null!;

        [StringLength(1000)]
        public string? ExecutionSummary { get; set; }

        public DateTime CreatedDate { get; set; }

        public string RequestText { get; set; } = null!;

        [ForeignKey("CaseCode")]
        public virtual ProcessCase ProcessCase { get; set; } = null!;

        [ForeignKey("ConfigCode")]
        public virtual FinalResponseConfig FinalResponseConfig { get; set; } = null!;

        [ForeignKey("FileId")]
        public virtual DataFile? DataFile { get; set; }
    }
}
