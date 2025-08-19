namespace app_tramites.Models.ModelAi
{
    public partial class CaseReview
    {
        public Guid ReviewId { get; set; }
        public Guid CaseCode { get; set; }
        public bool Answer { get; set; }
        public string? ReviewText { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }

        public virtual ProcessCase Case { get; set; } = null!;


    }
}
