using KutuphaneOtomasyon.Data;
using KutuphaneOtomasyon.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using Newtonsoft.Json;

namespace KutuphaneOtomasyon.Controllers

{
    
    public class BookController : Controller
    {
        private readonly LibraryContext _context;
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly ILogger<BookController> _logger; // Logger ekleyin
        public BookController(LibraryContext context, ILogger<BookController> logger)
        {
            _context = context;
            _logger = logger;
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
            var books = await _context.Books.ToListAsync();
            return View(books);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Book book)
        {
            if (book.AvailableCopies > book.TotalCopies)
            {
                ModelState.AddModelError("AvailableCopies", "Mevcut kitap sayısı, toplam kitap sayısını geçemez.");
            }

            if (ModelState.IsValid)
            {
                Console.WriteLine("ModelState geçerli, kayıt ekleniyor...");
                _context.Books.Add(book);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            if (!ModelState.IsValid)
            {
                foreach (var key in ModelState.Keys)
                {
                    var errors = ModelState[key].Errors;
                    foreach (var error in errors)
                    {
                        Console.WriteLine($"[{key}] => {error.ErrorMessage}");
                    }
                }
            }

            return View(book);
        }

        public async Task<IActionResult> Update(int? id)
        {
            if (id == null || _context.Books == null)
            {
                return NotFound();
            }

            var book = await _context.Books.FindAsync(id);
            if (book == null)
            {
                return NotFound();
            }
            return View(book);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]

        public async Task<IActionResult> Update(int id, Book book)
        {
            if (id != book.BookId)
            {
                return NotFound();
            }
            if (book.AvailableCopies > book.TotalCopies)
            {
                ModelState.AddModelError("AvailableCopies", "Mevcut kitap sayısı, toplam kitap sayısını geçemez.");
            }
            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(book);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Books.Any(e => e.BookId == id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }

            return View(book);

        }

        [HttpPost("Book/DeleteConfirmed/{id}")]
        [ValidateAntiForgeryToken]

        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book != null)
            {
                _context.Books.Remove(book);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        // API'den kitap arama sayfası
        public IActionResult SearchInApi()
        {
            return View();
        }

        // API'den kitap arama sonuçları
        [HttpGet]
        public async Task<IActionResult> SearchInApi(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return View(new List<BookApiViewModel>());
            }

            try
            {
                // Google Books API isteği
                var apiUrl = $"https://www.googleapis.com/books/v1/volumes?q={Uri.EscapeDataString(query)}&maxResults=10";
                var response = await _httpClient.GetStringAsync(apiUrl);
                var result = JsonConvert.DeserializeObject<GoogleBooksApiResponse>(response);

                // ViewModel'e dönüştürme ve filtreleme
                var booksFromApi = result.Items?
                    .Where(item =>
                        !string.IsNullOrEmpty(item.Id) &&
                        !string.IsNullOrEmpty(item.VolumeInfo?.Title) &&
                        item.VolumeInfo.Authors != null && item.VolumeInfo.Authors.Any() &&
                        item.VolumeInfo.ImageLinks?.Thumbnail != null
                    )
                    .Select(item => new BookApiViewModel
                    {
                        Id = item.Id,
                        Title = item.VolumeInfo.Title,
                        Author = string.Join(", ", item.VolumeInfo.Authors),
                        Category = item.VolumeInfo.Categories != null && item.VolumeInfo.Categories.Any() ? item.VolumeInfo.Categories.First() : "Genel",
                        ISBN = item.VolumeInfo.IndustryIdentifiers?.FirstOrDefault(id => id.Type == "ISBN_13")?.Identifier
                               ?? item.VolumeInfo.IndustryIdentifiers?.FirstOrDefault(id => id.Type == "ISBN_10")?.Identifier,
                        Publisher = item.VolumeInfo.Publisher ?? "Bilinmiyor",
                        Descripton = item.VolumeInfo.Description ?? "Açıklama bulunamadı",
                        PageCount = item.VolumeInfo.PageCount?.ToString() ?? "Bilinmiyor",
                        Language = item.VolumeInfo.Language ?? "tr",
                        PublishedDate = item.VolumeInfo.PublishedDate,
                        ThumbnailUrl = item.VolumeInfo.ImageLinks.Thumbnail.Replace("http://", "https://")
                    })
                    .ToList() ?? new List<BookApiViewModel>();

                return View(booksFromApi);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"API'ye bağlanırken hata oluştu: {ex.Message}";
                return View(new List<BookApiViewModel>());
            }
        }

        // API'den kitap ekleme
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddFromApi(string bookId, string isbn, string title, string author, string? publisher, string? thumbnailUrl, string? category, string? description)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(author))
            {
                TempData["ErrorMessage"] = "Kitap adı ve yazar bilgisi zorunludur.";
                return RedirectToAction("SearchInApi");
            }

            try
            {
                var cleanIsbn = !string.IsNullOrWhiteSpace(isbn) ? CleanIsbn(isbn) : null;
                var existingBook = await FindExistingBook(title, author, isbn, cleanIsbn);

                if (existingBook != null)
                {
                    existingBook.TotalCopies += 1;
                    existingBook.AvailableCopies += 1;
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"{existingBook.Title} stok sayısı güncellendi.";
                }
                else
                {
                    var newBook = new Book
                    {
                        Title = title.Trim(),
                        Author = author.Trim(),
                        ISBN = !string.IsNullOrWhiteSpace(isbn) ? isbn.Trim() : null,
                        CleanISBN = cleanIsbn,
                        TotalCopies = 1,
                        AvailableCopies = 1,
                        Category = !string.IsNullOrWhiteSpace(category) ? category : "Genel",
                        Publisher = publisher ?? "Bilinmiyor",
                        ThumbnailUrl = thumbnailUrl,
                        Description = description,

                    };

                    _context.Books.Add(newBook);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"{newBook.Title} kütüphaneye eklendi.";
                }

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Hata: {ex.Message}";
                return RedirectToAction("SearchInApi");
            }
        }

        private async Task<Book> FindExistingBook(string title, string author, string isbn, string? cleanIsbn)
        {
            var query = _context.Books.AsQueryable();

            if (!string.IsNullOrWhiteSpace(isbn) && !string.IsNullOrWhiteSpace(cleanIsbn))
            {
                query = query.Where(b => b.ISBN == isbn || b.CleanISBN == cleanIsbn);
            }
            else
            {
                query = query.Where(b => b.Title == title.Trim() && b.Author == author.Trim());
            }

            return await query.FirstOrDefaultAsync();
        }

        private string? CleanIsbn(string? isbn)
        {
            return isbn?.Replace("-", "").Replace(" ", "").Trim();
        }
        // Google Books API modelleri
        public class GoogleBooksApiResponse
        {
            public List<GoogleBookItem> Items { get; set; }
        }

        public class GoogleBookItem
        {
            public string Id { get; set; }
            public VolumeInfo VolumeInfo { get; set; }
        }

        public class VolumeInfo
        {
            public string Title { get; set; }
            public List<string> Authors { get; set; }
            public List<string> Categories { get; set; } // Kategori listesi (opsiyonel, API'den gelmeyebilir)
            public string Publisher { get; set; }
            public string PublishedDate { get; set; }
            public string Description { get; set; } // API'den gelen açıklama
            
            public int? PageCount { get; set; } // Sayfa sayısı (null olabilir)
            public string Language { get; set; }
            public List<IndustryIdentifier> IndustryIdentifiers { get; set; }
            public ImageLinks ImageLinks { get; set; }
        }

        public class IndustryIdentifier
        {
            public string Type { get; set; }
            public string Identifier { get; set; }
        }

        public class ImageLinks
        {
            public string Thumbnail { get; set; }
        }

        // API sonuçları için ViewModel
        public class BookApiViewModel
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Author { get; set; }
            public string Category { get; set; } // Kategori (opsiyonel, API'den gelmeyebilir)
            public string ISBN { get; set; }
            public string Publisher { get; set; }
            public string Descripton { get; set; } // API'den gelen açıklama

            public string PageCount { get; set; } // Sayfa sayısı
            public string Language { get; set; } // Dil
            public string PublishedDate { get; set; }
            public string ThumbnailUrl { get; set; }
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var book = await _context.Books
                .FirstOrDefaultAsync(m => m.BookId == id);

            if (book == null)
            {
                return NotFound();
            }

            // API'den ekstra bilgiler çekmek için (opsiyonel)
            if (!string.IsNullOrEmpty(book.ISBN))
            {
                try
                {
                    var apiUrl = $"https://www.googleapis.com/books/v1/volumes?q=isbn:{book.ISBN}";
                    var response = await _httpClient.GetStringAsync(apiUrl);
                    var result = JsonConvert.DeserializeObject<GoogleBooksApiResponse>(response);

                    if (result.Items?.Count > 0)
                    {
                        var volumeInfo = result.Items[0].VolumeInfo;
                        ViewBag.Description = volumeInfo.Description ?? "Açıklama bulunamadı";
                        ViewBag.PageCount = volumeInfo.PageCount?.ToString() ?? "Bilinmiyor"; // Sayfa sayısı null ise
                        ViewBag.Language = volumeInfo.Language ?? "tr";

                        // DEBUG: Gelen verileri loglayın
                        Console.WriteLine($"API'den gelen veri: {JsonConvert.SerializeObject(volumeInfo)}");
                    }
                }
                catch { /* Hata olursa görmezden gel */ }
            }

            return View(book);
        }
    }

}
    

