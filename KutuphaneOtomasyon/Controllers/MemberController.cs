using KutuphaneOtomasyon.Data;
using KutuphaneOtomasyon.Models;
using KutuphaneOtomasyon.Services.Recommendations;
using KutuphaneOtomasyon.Services.Risk;
using KutuphaneOtomasyon.ViewModels.Members;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using KutuphaneOtomasyon.Services.AI;
namespace KutuphaneOtomasyon.Controllers
{
    public class MemberController : Controller
    {
        private readonly LibraryContext _context;
        private readonly IRiskScoringService _riskAnalyzer;
        private readonly IRecommendationService _recommendationService;
        private readonly IAiAssistant _ai;
        // ❗️ DÜZELTİLMİŞ CTOR (LibraryContext sadece bir kez)
        public MemberController(
            LibraryContext context,
            IRiskScoringService riskAnalyzer,
            IRecommendationService recommendationService,
            IAiAssistant ai)
        {
            _context = context;
            _riskAnalyzer = riskAnalyzer;
            _recommendationService = recommendationService;
            _ai = ai;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (HttpContext.Session.GetString("IsAdmin") != "true")
            {
                context.Result = RedirectToAction("Login", "Auth");
            }
            base.OnActionExecuting(context);
        }

        public async Task<IActionResult> Index()
        {
            var members = await _context.Members.ToListAsync();
            return View(members);
        }

        public IActionResult Create()
        {
            ViewBag.MemberTypes = Enum.GetValues(typeof(MemberTypeEnum))
                .Cast<MemberTypeEnum>()
                .Select(mt => new SelectListItem
                {
                    Value = ((int)mt).ToString(),
                    Text = mt.ToString()
                }).ToList();

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Member member)
        {
            if (ModelState.IsValid)
            {
                if (member.JoinedAt == default) member.JoinedAt = DateTime.UtcNow;
                _context.Add(member);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(member);
        }

        public async Task<IActionResult> Update(int? id)
        {
            if (id == null || _context.Members == null) return NotFound();

            var member = await _context.Members.FindAsync(id);
            if (member == null) return NotFound();

            ViewBag.MemberTypes = Enum.GetValues(typeof(MemberTypeEnum))
                .Cast<MemberTypeEnum>()
                .Select(mt => new SelectListItem
                {
                    Value = ((int)mt).ToString(),
                    Text = mt.ToString()
                }).ToList();

            return View(member);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(int id, Member member)
        {
            if (id != member.MemberId) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(member);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Members.Any(m => m.MemberId == id)) return NotFound();
                    throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(member);
        }

        [HttpPost("Member/DeleteConfirmed/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var member = await _context.Members.FindAsync(id);
            if (member != null)
            {
                _context.Members.Remove(member);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        // === DETAY ===
        public async Task<IActionResult> Details(int id, bool useAi = true, CancellationToken ct = default)
        {
            var member = await _context.Members
                .Include(m => m.Borrows).ThenInclude(b => b.Book)
                .FirstOrDefaultAsync(m => m.MemberId == id, ct);
            if (member == null) return NotFound();

            // Şu an eldeki kitaplar
            var currentBorrows = member.Borrows
                .Where(b => b.ReturnDate == null)
                .Select(b => new BorrowRowVm
                {
                    BorrowId = b.BorrowId,
                    BookId = b.BookId,
                    Title = b.Book?.Title ?? "",
                    Author = b.Book?.Author,
                    BorrowDate = b.BorrowDate,
                    DueDate = b.DueDate,
                    ReturnDate = b.ReturnDate
                })
                .OrderBy(b => b.DueDate ?? DateTime.MaxValue)
                .ToList();

            // Geçmiş
            var history = member.Borrows
                .Where(b => b.ReturnDate != null)
                .OrderByDescending(b => b.ReturnDate)
                .Select(b => new BorrowRowVm
                {
                    BorrowId = b.BorrowId,
                    BookId = b.BookId,
                    Title = b.Book!.Title,
                    Author = b.Book.Author,
                    BorrowDate = b.BorrowDate,
                    DueDate = b.DueDate,
                    ReturnDate = b.ReturnDate
                })
                .ToList();

            var totalBorrowCount = member.Borrows.Count;
            var lateReturnCount = member.Borrows.Count(b =>
                b.DueDate != null && b.ReturnDate != null && b.ReturnDate > b.DueDate);
            var onTimeRate = totalBorrowCount == 0
                ? 1
                : (double)(totalBorrowCount - lateReturnCount) / totalBorrowCount;

            // --- Risk (eski mantığın kalsın) ---
            // --- Risk (hesaplandı) ---
            var riskResult = _riskAnalyzer.Calculate(id);

            // --- Normalize: Score 0..100 + Level üret ---
            if (riskResult != null)
            {
                riskResult.Score = Math.Max(0, Math.Min(100, riskResult.Score));
                if (string.IsNullOrWhiteSpace(riskResult.Level))
                    riskResult.Level = riskResult.Score < 30 ? "Low"
                                     : riskResult.Score < 70 ? "Medium"
                                     : "High";

                // (Opsiyonel) maddeleri kısalt/sırala
                if (riskResult.Items?.Count > 10)
                    riskResult.Items = riskResult.Items
                        .OrderByDescending(x => x.Weight)
                        .ThenByDescending(x => x.Status)
                        .Take(10).ToList();

                // --- AI özet (quota yoksa bile fallback hazır) ---
                try
                {
                    var memberVmForAi = new MemberVm { MemberId = member.MemberId, Name = member.Name };
                    var aiText = await _ai.SummarizeRiskAsync(memberVmForAi, riskResult, ct);

                    if (!string.IsNullOrWhiteSpace(aiText))
                        riskResult.Summary = aiText.Trim();

                    if (string.IsNullOrWhiteSpace(riskResult.Summary))
                        riskResult.Summary = $"Skor {riskResult.Score} ({riskResult.Level}). " +
                                             $"{riskResult.OverdueCount} gecikmiş, {riskResult.DueSoonCount} yaklaşan iade.";
                }
                catch
                {
                    if (string.IsNullOrWhiteSpace(riskResult.Summary))
                        riskResult.Summary = $"Skor {riskResult.Score} ({riskResult.Level}). " +
                                             $"{riskResult.OverdueCount} gecikmiş, {riskResult.DueSoonCount} yaklaşan iade.";
                }
            }

            // --- ÖNERİLER ---
            // 1) Kural-tabanlı (klasik)
            var classic = _recommendationService.RecommendForMember(id) ?? new List<AiSuggestionVm>();

            // 2) AI (toggle ve provider açık ise)
            List<AiSuggestionVm>? ai = null;
            DateTime? generatedAt = null;
            var aiEnabled = false;
            try
            {
                aiEnabled = (_ai?.GetType().Name ?? "") != "NullAiAssistant"; // veya _ai.IsEnabled
                if (useAi && aiEnabled && classic.Any())
                {
                    ai = await _ai.RerankAndExplainAsync(id, classic, ct);
                    generatedAt = DateTime.Now;
                }
            }
            catch
            {
                // AI hatası durumunda klasik'e düş
                ai = null;
            }

            // 3) ViewModel
            var vm = new MemberDetailsVm
            {
                Member = new MemberVm
                {
                    MemberId = member.MemberId,
                    Name = member.Name,
                    Email = member.Email,
                },
                CurrentBorrows = currentBorrows,
                History = history,
                TotalBorrowCount = totalBorrowCount,
                LateReturnCount = lateReturnCount,
                OnTimeRate = onTimeRate,

                Risk = riskResult,

                AiEnabled = aiEnabled,
                UseAi = useAi,
                SuggestionsGeneratedAt = generatedAt,
                ClassicSuggestions = classic,
                AiSuggestions = ai
            };

            // Nihai gösterilecek liste (toggle + mevcutluğa göre)
            vm.Suggestions = (vm.UseAi && vm.AiEnabled && (vm.AiSuggestions?.Any() ?? false))
                ? vm.AiSuggestions!
                : vm.ClassicSuggestions;

            return View(vm);
        }
        // MemberController sınıfının içine ekle
        [HttpGet("/api/ai/member-suggestions")]
        public async Task<IActionResult> QuickSuggestions(int memberId, int take = 6, CancellationToken ct = default)
        {
            // 1) Kural tabanlı öneri
            var classic = _recommendationService.RecommendForMember(memberId) ?? new List<AiSuggestionVm>();

            // 2) AI varsa re-rank
            List<AiSuggestionVm> result = classic;
            if (_ai.IsEnabled && classic.Any())
            {
                try
                {
                    var ai = await _ai.RerankAndExplainAsync(memberId, classic, ct);
                    if (ai != null && ai.Any()) result = ai;
                }
                catch
                {
                    // hata olursa sessizce klasik'e dön
                }
            }

            // 3) Sadece gereken alanları dön (hafif JSON)
            var payload = result
                .Take(Math.Max(1, take))
                .Select(s => new {
                    s.BookId,
                    s.Title,
                    s.Author,
                    s.Reason,
                    s.AvailableCopies,
                    s.ThumbnailUrl
                });

            return Json(payload);
        }

    }
}
