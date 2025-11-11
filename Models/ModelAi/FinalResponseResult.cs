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

        [Required]
        public string ResponseText { get; set; } = null!;

        public DateTime CreatedDate { get; set; }

        public string RequestText { get; set; } = null!;

        public string? MetadataJson { get; set; }

        [ForeignKey("CaseCode")]
        public virtual ProcessCase ProcessCase { get; set; } = null!;

    }
}
