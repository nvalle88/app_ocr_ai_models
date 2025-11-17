//using System.ComponentModel.DataAnnotations.Schema;
//using System.ComponentModel.DataAnnotations;

//namespace app_tramites.Models.ModelAi
//{
//    public class FinalResponseConfig
//    {
//        [Key]
//        [StringLength(50)]
//        public string ConfigCode { get; set; } = null!;

//        [Required]
//        [StringLength(30)]
//        public string ProcessCode { get; set; } = null!;

//        [Required]
//        [StringLength(50)]
//        public string AgentCode { get; set; } = null!;

//        [Required]
//        public string PromptTemplate { get; set; } = null!;

//        public bool UseOriginalText { get; set; } = true;

//        [Required]
//        [StringLength(100)]
//        public string IncludedStepOrders { get; set; } = "*";

//        public bool IncludeStepNames { get; set; } = false;
//        public bool IncludeFileCount { get; set; } = true;
//        public string? MetadataJson { get; set; }
//        public bool IsEnabled { get; set; } = true;

//        [ForeignKey("ProcessCode")]
//        public virtual Process Process { get; set; } = null!;

//        [ForeignKey("AgentCode")]
//        public virtual Agent Agent { get; set; } = null!;
//    }
//}
