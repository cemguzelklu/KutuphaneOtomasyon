using KutuphaneOtomasyon.ViewModels.Members;
using Microsoft.EntityFrameworkCore;

namespace KutuphaneOtomasyon.Services.Risk
{
    public class RiskScoringService : IRiskScoringService
    {
        private readonly Data.LibraryContext _db;
        public RiskScoringService(Data.LibraryContext db) => _db = db;

        public RiskResultVm Calculate(int memberId)
        {
            var now = DateTime.Now.Date;

            var current = _db.Borrows
                .Include(x => x.Book)
                .Where(x => x.MemberId == memberId && x.ReturnDate == null)
                .ToList();

            var history = _db.Borrows
                .Include(x => x.Book)
                .Where(x => x.MemberId == memberId)
                .ToList();

            int lateEver = history.Count(h => h.DueDate.HasValue && h.ReturnDate.HasValue && h.ReturnDate.Value.Date > h.DueDate.Value.Date);
            double lateRate = history.Count > 0 ? (double)lateEver / history.Count : 0.0;

            int score = 0, overdueCount = 0, dueSoonCount = 0;
            var items = new List<RiskItemVm>();

            foreach (var b in current)
            {
                int w; string status;
                if (b.DueDate.HasValue)
                {
                    var d = (b.DueDate.Value.Date - now).Days;
                    if (d < 0) { w = 35; overdueCount++; status = $"{Math.Abs(d)} gün gecikti"; }
                    else if (d == 0) { w = 25; dueSoonCount++; status = "Son gün"; }
                    else if (d <= 3) { w = 15; dueSoonCount++; status = $"{d} gün kaldı"; }
                    else if (d <= 7) { w = 8; status = $"{d} gün kaldı"; }
                    else { w = 2; status = $"{d} gün kaldı"; }
                }
                else { w = 10; status = "Son teslim yok"; }

                score += w;
                items.Add(new RiskItemVm { BookId = b.BookId, Title = b.Book.Title, Status = status, Weight = w });
            }

            score += (int)Math.Round(lateRate * 30.0);
            score = Math.Clamp(score, 0, 100);

            string level = score >= 70 ? "High" : (score >= 40 ? "Medium" : "Low");
            string summary = level switch
            {
                "High" => "Gecikme riski yüksek: hatırlatma önerilir.",
                "Medium" => "Orta risk: yaklaşan iade tarihleri var.",
                _ => "Düşük risk."
            };

            return new RiskResultVm
            {
                Score = score,
                Level = level,
                OverdueCount = overdueCount,
                DueSoonCount = dueSoonCount,
                Items = items,
                Summary = summary
            };
        }
    }
}