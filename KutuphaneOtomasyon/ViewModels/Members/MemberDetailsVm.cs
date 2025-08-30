namespace KutuphaneOtomasyon.ViewModels.Members
{
    public class MemberDetailsVm
    {
        public MemberVm Member { get; set; } = new();
        public List<BorrowRowVm> CurrentBorrows { get; set; } = new();
        public List<BorrowRowVm> History { get; set; } = new();

        // İstatistikler
        public int TotalBorrowCount { get; set; }
        public int LateReturnCount { get; set; }
        public double OnTimeRate { get; set; } // 0..1
        
        // --- YENİ: Toggle ve iki ayrı liste ---
        public bool AiEnabled { get; set; }            // Sistemde AI aktif mi?
        public bool UseAi { get; set; } = true;        // Toggle sonucu (UI)
        public DateTime? SuggestionsGeneratedAt { get; set; } // AI çalıştıysa zaman damgası

        public List<AiSuggestionVm> ClassicSuggestions { get; set; } = new(); // kural-tabanlı
        public List<AiSuggestionVm>? AiSuggestions { get; set; }              // AI re-rank
        // --------------------------------------
        // Yapay zekâ/öneri ve risk
        public List<AiSuggestionVm> Suggestions { get; set; } = new();   // ilk yüklemede boş olabilir
        public RiskResultVm? Risk { get; set; }                          // ilk yüklemede null olabilir
    }

    public class MemberVm
    {
        public int MemberId { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public DateTime RegisterDate { get; set; }
        public string? Notes { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Address { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class BorrowRowVm
    {
        public int BorrowId { get; set; }
        public int BookId { get; set; }
        public string Title { get; set; } = "";
        public string? Author { get; set; }
        public DateTime BorrowDate { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? ReturnDate { get; set; }
        public bool IsReturned => ReturnDate.HasValue;
        public bool IsOverdue => !IsReturned && DueDate.HasValue && DueDate.Value.Date < DateTime.Now.Date;
    }

    public class AiSuggestionVm
    {
        public int BookId { get; set; }
        public string Title { get; set; } = "";
        public string? Author { get; set; }
        public string? Reason { get; set; } // "Daha önce okuduğunuz X kategorisine benzediği için" gibi açıklama
        public string? ThumbnailUrl { get; set; }
        public int AvailableCopies { get; set; }
    }

    public class RiskResultVm
    {
        public int Score { get; set; }           // 0-100 (yüksek = risk yüksek)
        public string Level { get; set; } = "";  // "Low/Medium/High"
        public int OverdueCount { get; set; }
        public int DueSoonCount { get; set; }
        public List<RiskItemVm> Items { get; set; } = new(); // kitap bazlı durumlar
        public string Summary { get; set; } = "";            // kısa özet metin
    }

    public class RiskItemVm
    {
        public int BookId { get; set; }
        public string Title { get; set; } = "";
        public string Status { get; set; } = ""; // "Gecikti", "Son gün", "3 gün kaldı" gibi
        public int Weight { get; set; }          // skor katkısı
    }
}