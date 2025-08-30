namespace KutuphaneOtomasyon.Models
{
    public class AiRecommendationHistory
    {
        public int Id { get; set; }
        public int? MemberId { get; set; }
        public int? BookId { get; set; }   // varsa kullan
        public string Title { get; set; } = "";
        public string? Author { get; set; }
        public string? Reason { get; set; }
        public decimal? Score { get; set; }
        public int? Rank { get; set; }
        public string? Source { get; set; } = "Diagnostics";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
