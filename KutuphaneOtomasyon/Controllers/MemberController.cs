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
                _context.Members.Add(member);
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
        public async Task<IActionResult> Details(int id, CancellationToken ct)
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

            // --- Risk & Öneri (kural tabanlı) ---
            var riskResult = _riskAnalyzer.Calculate(id);
            var suggestions = _recommendationService.RecommendForMember(id);

            // --- AI ile zenginleştir (anahtar yoksa OpenAiAssistant zaten no-op/fallback yapar) ---
            try
            {
                suggestions = await _ai.RerankAndExplainAsync(id, suggestions, ct);
            }
            catch { /* sessiz geç */ }

            try
            {
                if (riskResult != null)
                {
                    var memberVm = new MemberVm { MemberId = member.MemberId, Name = member.Name };
                    riskResult.Summary = await _ai.SummarizeRiskAsync(memberVm, riskResult, ct);
                }
            }
            catch { /* sessiz geç */ }

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
                Suggestions = suggestions
            };

            return View(vm);
        }

    }
}
