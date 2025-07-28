using KutuphaneOtomasyon.Data;
using KutuphaneOtomasyon.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KutuphaneOtomasyon.Controllers
{
    public class BorrowController : Controller
    {
        private readonly LibraryContext _context;

        public BorrowController(LibraryContext context)
        {
            _context = context;
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
            var borrows = await _context.Borrows
                .Include(b => b.Book)
                .Include(b => b.Member)
                .ToListAsync();
            return View(borrows);
        }

        public IActionResult Create()
        {
            ViewBag.Members = new SelectList(_context.Members.ToList(), "MemberId", "Name");
            ViewBag.Books = new SelectList(_context.Books.ToList(), "BookId", "Title");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Borrow borrow)
        {
           

            if (ModelState.IsValid)
            {
                borrow.BorrowDate = DateTime.Now;
                var book = await _context.Books.FindAsync(borrow.BookId);
                if (book == null || book.AvailableCopies <= 0)
                {
                    ModelState.AddModelError("BookId", "Seçilen kitap mevcut değil veya tüm kopyaları ödünç verilmiş.");
                    ViewBag.Members = new SelectList(_context.Members.ToList(), "MemberId", "Name");
                    ViewBag.Books = new SelectList(_context.Books.ToList(), "BookId", "Title");
                    return View(borrow);
                }
                book.AvailableCopies -= 1; 
                _context.Borrows.Add(borrow);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            ViewBag.Members = new SelectList(_context.Members.ToList(), "MemberId", "Name");
            ViewBag.Books = new SelectList(_context.Books.ToList(), "BookId", "Title");
            return View(borrow);
        }

        public async Task<IActionResult> Return(int? id)
        {
            if (id == null)
                return NotFound();

            var borrow = await _context.Borrows
                .Include(b => b.Book)
                .Include(b => b.Member)
                .FirstOrDefaultAsync(b => b.BorrowId == id);


            if (borrow == null || borrow.ReturnDate != null)
            {
                return NotFound(); // Zaten iade edilmişse
            }

            return View(borrow);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Return(int id, DateTime returnDate)
        {
            var borrow = await _context.Borrows
                .Include(b => b.Book)
                .Include(b => b.Member)
                .FirstOrDefaultAsync(b => b.BorrowId == id);

            if (borrow == null)
                return NotFound();

            var borrowDate = borrow.BorrowDate.Date;

            if (returnDate.Date < borrowDate)
            {
                ModelState.AddModelError("ReturnDate", "İade tarihi, ödünç alma tarihinden önce olamaz.");
            }
            else if ((returnDate - borrowDate).TotalDays > 30)
            {
                ModelState.AddModelError("ReturnDate", "İade tarihi, ödünç alma tarihinden en fazla 30 gün sonrası olabilir.");
            }

            if (!ModelState.IsValid)
            {
                // Hatalıysa tekrar modeli gönder
                return View("Return", borrow);
            }

            borrow.ReturnDate = returnDate;
            var book=await _context.Books.FindAsync(borrow.BookId);
            if (book != null)
            {
                book.AvailableCopies += 1; // Kitap iade edildiğinde kopya sayısını artır
                _context.Books.Update(book);
            }
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        /*public async Task<IActionResult> Update(int? id)
       {
           if (id == null) return NotFound();

           var borrow = await _context.Borrows.FindAsync(id);
           if (borrow == null) return NotFound();

           ViewBag.Books = _context.Books.ToList();
           ViewBag.Members = _context.Members.ToList();
           return View(borrow);
       }

       [HttpPost]
       [ValidateAntiForgeryToken]
       public async Task<IActionResult> Update(int id, Borrow borrow)
       {
           if (id != borrow.BorrowId) return NotFound();

           if (ModelState.IsValid)
           {
               _context.Update(borrow);
               await _context.SaveChangesAsync();
               return RedirectToAction(nameof(Index));
           }

           ViewBag.Books = _context.Books.ToList();
           ViewBag.Members = _context.Members.ToList();
           return View(borrow);
       }*/
    }
}
