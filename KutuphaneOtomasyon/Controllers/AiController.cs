using KutuphaneOtomasyon.Data;
using KutuphaneOtomasyon.Models;
using KutuphaneOtomasyon.Services.AI;
using KutuphaneOtomasyon.Services.Recommendations;
using KutuphaneOtomasyon.ViewModels.Admin;
using KutuphaneOtomasyon.ViewModels.Members;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace KutuphaneOtomasyon.Controllers
{
    [Route("admin/ai")]
    public class AiController : Controller
    {
        private readonly IAiAssistant _ai;
        private readonly IRecommendationService _rec;
        private readonly LibraryContext _context;

        public AiController(IAiAssistant ai, IRecommendationService rec, LibraryContext context)
        {
            _ai = ai;
            _rec = rec;
            _context = context;
        }
        [HttpGet("logs", Name = "AiLogs")]
        public async Task<IActionResult> Logs(CancellationToken ct)
        {
            var model = await _context.AiLogs
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAtUtc)
                //.Take(1000) // istersen limit koy
                .ToListAsync(ct);

            return View(model); // Views/Ai/Logs.cshtml (zaten paylaştığın Razor bu görünüme ait)
        }
        [HttpGet("diagnostics")]
        public async Task<IActionResult> Diagnostics(int? memberId, CancellationToken ct)
        {
            List<AiSuggestionVm>? testResults = null;
            if (memberId.HasValue)
            {
                var classic = _rec.RecommendForMember(memberId.Value) ?? new List<AiSuggestionVm>();
                testResults = (_ai.IsEnabled && classic.Any())
                    ? await _ai.RerankAndExplainAsync(memberId.Value, classic, ct)
                    : classic;
            }

            var d = await _ai.DiagnosticsAsync(ct);

            // (İsteğe bağlı) sayfa açılışında “Geçmiş Öneriler”i doldurmak istiyorsan:
            var lastSaved = await _context.AiRecommendationHistories
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Take(10)
                .ToListAsync(ct);

            var vm = new AiDiagnosticsVm
            {
                Enabled = d.Enabled,
                Provider = d.Provider,
                Model = d.Model,
                LastLatencyMs = d.LastLatencyMs,
                LastPromptSnippet = d.LastPromptSnippet,
                LastResponseSnippet = d.LastResponseSnippet,
                TestMemberId = memberId,
                TestAiResults = testResults,
                SavedRecommendations = lastSaved   // <-- View’da kullanıyorsun
            };

            return View(vm);
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (HttpContext.Session.GetString("IsAdmin") != "true")
                context.Result = RedirectToAction("Login", "Auth");
            base.OnActionExecuting(context);
        }

        // === API’ler ===

        [HttpPost("saverecommendations")]
        public async Task<IActionResult> SaveRecommendations([FromBody] SaveRecsDto dto)
        {
            if (dto?.Items == null || dto.Items.Count == 0) return BadRequest();

            var now = DateTime.UtcNow;
            foreach (var it in dto.Items)
            {
                var row = new AiRecommendationHistory
                {
                    MemberId = it.MemberId,
                    BookId = it.BookId,
                    Title = it.Title?.Trim() ?? "",
                    Author = string.IsNullOrWhiteSpace(it.Author) ? null : it.Author.Trim(),
                    Reason = string.IsNullOrWhiteSpace(it.Reason) ? null : it.Reason.Trim(),
                    Score = it.Score,
                    Rank = it.Rank,
                    Source = "Diagnostics",
                    CreatedAt = now
                };
                _context.AiRecommendationHistories.Add(row);
            }
            await _context.SaveChangesAsync();
            return Ok(new { ok = true });
        }

        // AiController.cs
        [HttpGet("History")]
        public async Task<IActionResult> History(int? memberId, int page = 1, int pageSize = 10)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            var q = _context.AiRecommendationHistories.AsNoTracking();
            if (memberId.HasValue) q = q.Where(x => x.MemberId == memberId);

            // --- TOPLAMLAR (tüm satırlar) ---
            var total = await q.CountAsync();
            // Not: SQL Server kolasyonun case-insensitive ise Distinct zaten CI çalışır.
            // Emin olmak için ToLower() da kullanıyoruz.
            var uniqueTitles = await q.Select(x => x.Title.ToLower()).Distinct().CountAsync();
            var lastCreatedAt = await q.OrderByDescending(x => x.CreatedAt)
                                       .Select(x => x.CreatedAt)
                                       .FirstOrDefaultAsync();

            // --- Sayfalı kayıtlar ---
            var items = await q.OrderByDescending(x => x.CreatedAt)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .Select(x => new
                               {
                                   id = x.Id,
                                   memberId = x.MemberId,
                                   title = x.Title,
                                   author = x.Author,
                                   createdAt = x.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
                               })
                               .ToListAsync();

            var totalPages = (int)Math.Ceiling(total / (double)pageSize);

            return Ok(new
            {
                items,
                page,
                pageSize,
                total,
                totalPages,
                stats = new
                {
                    count = total,
                    unique = uniqueTitles,
                    last = (lastCreatedAt == default(DateTime))
                        ? null
                        : lastCreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
                }
            });
        }



        [HttpPost("history/delete")]
        public async Task<IActionResult> DeleteHistoryItem([FromBody] DeleteDto dto)
        {
            var row = await _context.AiRecommendationHistories.FindAsync(dto.Id);
            if (row == null) return NotFound();
            _context.AiRecommendationHistories.Remove(row);
            await _context.SaveChangesAsync();
            return Ok(new { ok = true });
        }

        [HttpPost("history/clear")]
        public async Task<IActionResult> ClearHistory([FromBody] ClearDto dto)
        {
            var q = _context.AiRecommendationHistories.AsQueryable();
            if (dto.MemberId.HasValue) q = q.Where(x => x.MemberId == dto.MemberId);
            _context.AiRecommendationHistories.RemoveRange(q);
            await _context.SaveChangesAsync();
            return Ok(new { ok = true });
        }
    }

    // DTO'lar
    public record SaveRecsDto(List<SaveRecItem> Items);
    public record SaveRecItem(int? MemberId, int? BookId, string Title, string? Author, string? Reason, decimal? Score, int? Rank);
    public record DeleteDto(int Id);
    public record ClearDto(int? MemberId);
}
