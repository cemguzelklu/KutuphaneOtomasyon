using System.Diagnostics;
using KutuphaneOtomasyon.Data;
using KutuphaneOtomasyon.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace KutuphaneOtomasyon.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly LibraryContext _context;

        public HomeController(ILogger<HomeController> logger,LibraryContext context)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            // === KPI'lar ===
            ViewBag.TotalBooks = await _context.Books.CountAsync();
            ViewBag.TotalMembers = await _context.Members.CountAsync();
            ViewBag.BorrowedBooks = await _context.Borrows.CountAsync(b => b.ReturnDate == null);
            ViewBag.TotalBorrows = await _context.Borrows.CountAsync();

            ViewBag.TodayBorrows = await _context.Borrows.CountAsync(b => b.BorrowDate.Date == DateTime.Today);
            ViewBag.TodayReturns = await _context.Borrows.CountAsync(b => b.ReturnDate.HasValue && b.ReturnDate.Value.Date == DateTime.Today);

            ViewBag.TopMember = await _context.Borrows
                .Where(b => b.Member != null)
                .GroupBy(b => new { b.Member.MemberId, b.Member.Name })
                .Select(g => new { g.Key.Name, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .FirstOrDefaultAsync();

            // === Son Eklenen Kitaplar (pagination'dan baðýmsýz, en yeni 10) ===
            // "CreatedAt" vb. alanýn yoksa BookId DESC en saðlýklýsý.
            ViewBag.RecentBooks = await _context.Books
                .OrderByDescending(b => b.BookId)
                .Take(10)
                .AsNoTracking()
                .ToListAsync();

            // === Site Analizi: Son 30 gün için günlük ödünç / iade ===
            var startDate = DateTime.Today.AddDays(-29);

            var borrowsAgg = await _context.Borrows
                .Where(b => b.BorrowDate >= startDate)
                .GroupBy(b => new { b.BorrowDate.Year, b.BorrowDate.Month, b.BorrowDate.Day })
                .Select(g => new { Day = new DateTime(g.Key.Year, g.Key.Month, g.Key.Day), Count = g.Count() })
                .ToListAsync();

            var returnsAgg = await _context.Borrows
                .Where(b => b.ReturnDate.HasValue && b.ReturnDate.Value >= startDate)
                .GroupBy(b => new { b.ReturnDate!.Value.Year, b.ReturnDate!.Value.Month, b.ReturnDate!.Value.Day })
                .Select(g => new { Day = new DateTime(g.Key.Year, g.Key.Month, g.Key.Day), Count = g.Count() })
                .ToListAsync();

            var days = Enumerable.Range(0, 30).Select(i => startDate.AddDays(i).Date).ToList();
            var labels = days.Select(d => d.ToString("dd.MM")).ToList();
            var borrowsSeries = days.Select(d => borrowsAgg.FirstOrDefault(x => x.Day == d)?.Count ?? 0).ToList();
            var returnsSeries = days.Select(d => returnsAgg.FirstOrDefault(x => x.Day == d)?.Count ?? 0).ToList();

            ViewBag.AnalyticsJson = JsonSerializer.Serialize(new
            {
                labels,
                borrows = borrowsSeries,
                returns = returnsSeries
            });

            // === En Popüler Kitaplar / En Aktif Üyeler ===
            ViewBag.TopBooks = await _context.Borrows
                .Where(b => b.Book != null)
                .GroupBy(b => new { b.Book.Title, b.Book.Author, b.Book.Category })
                .Select(g => new { g.Key.Title, g.Key.Author, g.Key.Category, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(8)
                .ToListAsync();

            ViewBag.TopMembers = await _context.Borrows
                .Where(b => b.Member != null)
                .GroupBy(b => new { b.Member.MemberId, b.Member.Name })
                .Select(g => new { g.Key.MemberId, g.Key.Name, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(8)
                .ToListAsync();

            // (Eski tablolarý bu sayfada göstermek istemiyorsan þu üç ViewBag'e artýk gerek yok)
            // ViewBag.BookList  = await _context.Books.Take(10).ToListAsync();
            // ViewBag.MemberList= await _context.Members.Take(10).ToListAsync();
            // ViewBag.BorrowList= await _context.Borrows.Include(b=>b.Book).Include(b=>b.Member)
            //                          .Where(b=>b.ReturnDate == null).Take(10).ToListAsync();

            return View();
        }


        public IActionResult Privacy()
        {
            return View();
         
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
