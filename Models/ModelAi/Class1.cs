namespace app_tramites.Models.ModelAi
{
    public class Note
    {
        public int NoteId { get; set; }
        public Guid CaseCode { get; set; }

        public string Title { get; set; } = string.Empty;
        public string? Detail { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;

        // Relación inversa
        public virtual ProcessCase? ProcessCase { get; set; }
    }
}
