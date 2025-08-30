using KutuphaneOtomasyon.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using KutuphaneOtomasyon.Services.AI;
namespace KutuphaneOtomasyon.Controllers
{
    public class SearchController : Controller
    {
        public readonly LibraryContext _context;
        private readonly IAiAssistant _ai; 

        public SearchController(LibraryContext context, IAiAssistant ai)
        {
            _context = context;
            _ai = ai;
        }
        public async Task<IActionResult> Index(string query, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(query))
                return View();

            var books = await _context.Books
                .Where(b =>
                    (b.Title != null && b.Title.Contains(query)) ||
                    (b.Author != null && b.Author.Contains(query)) ||
                    (b.Category != null && b.Category.Contains(query)))
                .ToListAsync(ct);

            var members = await _context.Members
                .Where(m =>
                    (m.Name != null && m.Name.Contains(query)) ||
                    (m.Email != null && m.Email.Contains(query)))
                .ToListAsync(ct);

            var borrows = await _context.Borrows
                .Where(b =>
                    (b.Book != null && b.Book.Title != null && b.Book.Title.Contains(query)) ||
                    (b.Member != null && b.Member.Name != null && b.Member.Name.Contains(query)))
                .Include(b => b.Book)
                .Include(b => b.Member)
                .ToListAsync(ct);

            var totalCount = (books?.Count ?? 0) + (members?.Count ?? 0) + (borrows?.Count ?? 0);

            if (totalCount == 0 && !string.IsNullOrWhiteSpace(query))
            {
                string? rewrite = null;

                // 1) AI varsa dener
                if (_ai.IsEnabled)
                {
                    try { rewrite = await _ai.SuggestQueryRewriteAsync(query, ct); }
                    catch { /* sessiz geç */ }
                }

                // 2) AI yoksa/başarısızsa yerel fallback
                if (string.IsNullOrWhiteSpace(rewrite))
                {
                    try { rewrite = await TryLocalRewriteAsync(query, ct); }
                    catch { /* sessiz geç */ }
                }

                if (!string.IsNullOrWhiteSpace(rewrite) &&
                    !string.Equals(rewrite, query, StringComparison.OrdinalIgnoreCase))
                {
                    ViewBag.SuggestedQuery = rewrite;
                }
            }

            ViewBag.Query = query;
            ViewBag.Books = books;
            ViewBag.Members = members;
            ViewBag.Borrows = borrows;
            return View();
        }

        private static string NormalizeTr(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim();
            s = s.Replace('İ', 'I').Replace('ı', 'i')
                 .Replace('Ö', 'O').Replace('ö', 'o')
                 .Replace('Ü', 'U').Replace('ü', 'u')
                 .Replace('Ğ', 'G').Replace('ğ', 'g')
                 .Replace('Ş', 'S').Replace('ş', 's')
                 .Replace('Ç', 'C').Replace('ç', 'c');
            return s.ToLowerInvariant();
        }

        // Basit Levenshtein (küçük string’ler için yeterli)
        private static int Levenshtein(string a, string b)
        {
            if (a == null) return b?.Length ?? 0;
            if (b == null) return a.Length;
            var n = a.Length; var m = b.Length;
            var d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
                for (int j = 1; j <= m; j++)
                {
                    int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                                Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                                d[i - 1, j - 1] + cost);
                }
            return d[n, m];
        }
        private async Task<string?> TryLocalRewriteAsync(string query, CancellationToken ct = default)
        {
            var norm = NormalizeTr(query);
            if (norm.Length < 2) return null;

            // 1) Basit: Türkçe karakterleri sadeleştirilmiş hali sonuç getiriyorsa onu öner
            // (Aynı sorguyu ASCII’ye indirerek yeniden dene)
            var ascii = norm; // NormalizeTr zaten sadeleştiriyor
                              // Kitap/Üye/Ödünç tarafında hızlı bir LIKE testi (performans için sınırla)
            bool asciiHits =
                await _context.Books.AnyAsync(b =>
                      (b.Title != null && NormalizeTr(b.Title).Contains(ascii)) ||
                      (b.Author != null && NormalizeTr(b.Author).Contains(ascii)), ct)
                || await _context.Members.AnyAsync(m =>
                      (m.Name != null && NormalizeTr(m.Name).Contains(ascii)) ||
                      (m.Email != null && NormalizeTr(m.Email).Contains(ascii)), ct);

            if (asciiHits && !string.Equals(ascii, NormalizeTr(query), StringComparison.Ordinal))
                return query; // Kullanıcı girdisi zaten TR; görünür öneri olarak aynı metni göstermek yerine:
                              // istersen burada diakritiksiz formu geri döndür (tercih meselesi).

            // 2) Yakın başlık/yazar bul: İlk 3 harfi içeren adayları al, en yakın edit distance’ı seç
            var seed = norm.Length >= 3 ? norm.Substring(0, 3) : norm.Substring(0, 1);

            var candidates = await _context.Books
                .Where(b => b.Title != null && b.Title != "" && b.Title.Length >= seed.Length)
                .Select(b => b.Title)
                .Distinct()
                .Where(t => t.Contains(seed))  // kaba daraltma
                .Take(500)
                .ToListAsync(ct);

            // Normalize et ve puanla
            string? best = null;
            int bestScore = int.MaxValue;
            foreach (var c in candidates)
            {
                var nc = NormalizeTr(c);
                var score = Levenshtein(norm, nc);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }

            // Uygun eşik: uzunluk/2 veya max 2-3 fark
            var maxAllowed = Math.Max(2, norm.Length / 2);
            if (best != null && bestScore <= maxAllowed)
                return best;

            return null;
        }
    }

}