using KutuphaneOtomasyon.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace KutuphaneOtomasyon.Controllers
{
    public class SearchController : Controller
    {
        public readonly LibraryContext _context;

        public SearchController(LibraryContext context)
        {
            _context = context;
        }
        public async Task<IActionResult> Index(String query)
        {   
            if(string.IsNullOrEmpty(query))
            {
                return View();
            }


            var books = await _context.Books
                .Where(b => b.Title.Contains(query) || b.Author.Contains(query) || b.Category.Contains(query))
                .ToListAsync();

            var members = await _context.Members
                .Where(m => m.Name.Contains(query) || m.Email.Contains(query))
                .ToListAsync();

            var borrows = await _context.Borrows
                .Where(b => b.Book.Title.Contains(query) || b.Member.Name.Contains(query))
                .Include(b => b.Book)
                .Include(b => b.Member)
                .ToListAsync();
          
            ViewBag.Query = query;
            ViewBag.Books = books;
            ViewBag.Members = members;
            ViewBag.Borrows = borrows;

            return View();
            
            }
           
        }
    }

