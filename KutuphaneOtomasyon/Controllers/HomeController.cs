using System.Diagnostics;
using KutuphaneOtomasyon.Data;
using KutuphaneOtomasyon.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

        public async Task<IActionResult>  Index()
        {
            ViewBag.TotalBooks = await _context.Books.CountAsync();
            ViewBag.TotalMembers=await _context.Members.CountAsync();
            ViewBag.BorrowedBooks = await _context.Borrows.Where(b => b.ReturnDate == null).CountAsync();
            ViewBag.TotalBorrows=await _context.Borrows.CountAsync();

            ViewBag.BookList=await _context.Books.Take(10).ToListAsync();
            ViewBag.MemberList=await _context.Members.Take(10).ToListAsync();
            ViewBag.BorrowList=await _context.Borrows
                .Include(b => b.Book)
                .Include(b => b.Member)
                .Where(b => b.ReturnDate == null)
                .Take(10)
                .ToListAsync();

            ViewBag.TodayBorrows = _context.Borrows.Count(b => b.BorrowDate.Date == DateTime.Today);
            ViewBag.TodayReturns = _context.Borrows.Count(b => b.ReturnDate != null && b.ReturnDate.Value.Date == DateTime.Today);
            ViewBag.TopMember = _context.Borrows
                .Include(b => b.Member)
                .GroupBy(b => b.Member.Name)
                .OrderByDescending(g => g.Count())
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .FirstOrDefault();




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
