using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;                 // <-- ToListAsync, Where vs.
using KutuphaneOtomasyon.Data;                       // <-- LibraryContext
using KutuphaneOtomasyon.Services.AI;
using KutuphaneOtomasyon.Services.Recommendations;
using KutuphaneOtomasyon.ViewModels.Admin;
using KutuphaneOtomasyon.ViewModels.Members;

namespace KutuphaneOtomasyon.Controllers
{
    [Route("admin/ai")]
    public class AiController : Controller
    {
        private readonly IAiAssistant _ai;
        private readonly IRecommendationService _rec;
        private readonly LibraryContext _context;     // <-- EKLENDİ

        public AiController(IAiAssistant ai, IRecommendationService rec, LibraryContext context) // <-- EKLENDİ
        {
            _ai = ai;
            _rec = rec;
            _context = context;                       // <-- EKLENDİ
        }

        [HttpGet("diagnostics")]
        public async Task<IActionResult> Diagnostics(int? memberId, CancellationToken ct)
        {
            // 1) İsteğe bağlı test (AI çağrısını tetikle ki log/snippet dolsun)
            List<AiSuggestionVm>? testResults = null;
            if (memberId.HasValue)
            {
                var classic = _rec.RecommendForMember(memberId.Value) ?? new List<AiSuggestionVm>();
                if (_ai.IsEnabled && classic.Any())
                    testResults = await _ai.RerankAndExplainAsync(memberId.Value, classic, ct);
                else
                    testResults = classic;
            }

            // 2) Testten sonra tanılama bilgilerini al
            var d = await _ai.DiagnosticsAsync(ct);

            var vm = new AiDiagnosticsVm
            {
                Enabled = d.Enabled,
                Provider = d.Provider,
                Model = d.Model,
                LastLatencyMs = d.LastLatencyMs,
                LastPromptSnippet = d.LastPromptSnippet,
                LastResponseSnippet = d.LastResponseSnippet,
                TestMemberId = memberId,
                TestAiResults = testResults
            };

            return View(vm);
        }

        // Projedeki admin kontrolü (session ile)
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (HttpContext.Session.GetString("IsAdmin") != "true")
            {
                context.Result = RedirectToAction("Login", "Auth");
            }
            base.OnActionExecuting(context);
        }

        [HttpGet("logs")]
        public async Task<IActionResult> Logs(int days = 7)
        {
            var since = DateTime.UtcNow.AddDays(-days);
            var logs = await _context.AiLogs
                .Where(x => x.CreatedAtUtc >= since)
                .OrderByDescending(x => x.AiLogId)
                .Take(500)
                .ToListAsync();

            return View(logs);
        }
    }
}
