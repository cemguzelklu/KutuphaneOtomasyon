using KutuphaneOtomasyon.Data;
using KutuphaneOtomasyon.Models;
using Microsoft.AspNetCore.Mvc;
using KutuphaneOtomasyon.ViewModels.Books;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Threading.Tasks;


namespace KutuphaneOtomasyon.Controllers

{
    
    public class BookController : Controller
    {
        private readonly LibraryContext _context;
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly ILogger<BookController> _logger; // Logger ekleyin
        private readonly IMemoryCache _cache;
        public BookController(LibraryContext context, ILogger<BookController> logger, IMemoryCache cache)
        {
            _context = context;
            _logger = logger;
            _cache = cache;
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
        public async Task<IActionResult> SearchInApi(string query, bool returnJson = false)
        {
            if (string.IsNullOrEmpty(query))
            {
                return returnJson
                    ? Json(new List<BookApiViewModel>())
                    : View(new List<BookApiViewModel>());
            }

            try
            {
                var books = await SearchInGoogleBooks(query, 10); // 10 sonuç getir

                if (returnJson)
                {
                    return Json(books);
                }

                return View(books);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google Books API hatası");

                if (returnJson)
                {
                    return Json(new { error = ex.Message });
                }

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
            public string PreviewLink { get; set; }
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
            public string Description { get; set; } // API'den gelen açıklama

            public string PageCount { get; set; } // Sayfa sayısı
            public string Language { get; set; } // Dil
            public string PublishedDate { get; set; }
            public string ThumbnailUrl { get; set; }
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var book = await _context.Books
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BookId == id);

            if (book == null) return NotFound();

            // 1) Bu kitabın tüm ödünç hareketleri (üyelerle birlikte)
            var borrows = await _context.Borrows
                .Where(br => br.BookId == id)
                .Include(br => br.Member)
                .OrderByDescending(br => br.BorrowDate)
                .AsNoTracking()
                .ToListAsync();

            var currentBorrow = borrows.FirstOrDefault(br => br.ReturnDate == null);
            var currentBorrower = currentBorrow?.Member;

            var totalBorrowCount = borrows.Count;
            var lateReturnCount = borrows.Count(br =>
                br.DueDate.HasValue && br.ReturnDate.HasValue && br.ReturnDate.Value > br.DueDate.Value);
            // 2) “Bu kitabı alanların başka aldığı” popüler kitaplar (top 10)
            var memberIds = borrows.Select(b => b.MemberId).Distinct().ToList();

            var alsoBorrowedAgg = await _context.Borrows
                .Where(br => memberIds.Contains(br.MemberId) && br.BookId != id)
                .GroupBy(br => br.BookId)
                .Select(g => new { BookId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync();

            var alsoIds = alsoBorrowedAgg.Select(x => x.BookId).ToList();
            var alsoBooks = await _context.Books
                .Where(b => alsoIds.Contains(b.BookId))
                .AsNoTracking()
                .ToListAsync();

            var alsoBorrowedTop = alsoBorrowedAgg
                .Select(x =>
                {
                    var ab = alsoBooks.First(b => b.BookId == x.BookId);
                    return new AlsoBorrowedVm
                    {
                        BookId = ab.BookId,
                        Title = ab.Title,
                        Author = ab.Author,
                        Count = x.Count,
                        AvailableCopies = ab.AvailableCopies,
                        ThumbnailUrl = ab.ThumbnailUrl
                    };
                })
                .ToList();

            // 3) Basit “benzer kitaplar” (yazar ve kategori)
            var similarByAuthor = new List<Book>();
            if (!string.IsNullOrWhiteSpace(book.Author))
            {
                similarByAuthor = await _context.Books.AsNoTracking()
                    .Where(b => b.BookId != book.BookId && b.Author == book.Author)
                    .OrderBy(b => b.Title)
                    .Take(8)
                    .ToListAsync();
            }

            var similarByCategory = new List<Book>();
            if (!string.IsNullOrWhiteSpace(book.Category))
            {
                similarByCategory = await _context.Books.AsNoTracking()
                    .Where(b => b.BookId != book.BookId && b.Category == book.Category)
                    .OrderBy(b => b.Title)
                    .Take(8)
                    .ToListAsync();
            }

            // 4) Google Books zengin alanları (5 dk cache’le)
            string? gbDesc = null, gbLang = null, gbPage = null;

            if (!string.IsNullOrEmpty(book.ISBN))
            {
                var cacheKey = $"gb:detail:{book.ISBN}";
                if (!_cache.TryGetValue(cacheKey, out (string? desc, string? page, string? lang)? gb))
                {
                    try
                    {
                        var apiUrl = $"https://www.googleapis.com/books/v1/volumes?q=isbn:{book.ISBN}";
                        var response = await _httpClient.GetStringAsync(apiUrl);
                        var result = JsonConvert.DeserializeObject<GoogleBooksApiResponse>(response);

                        if (result?.Items?.Count > 0)
                        {
                            var vi = result.Items[0].VolumeInfo;
                            gbDesc = vi.Description ?? "Açıklama bulunamadı";
                            gbPage = vi.PageCount?.ToString() ?? "Bilinmiyor";
                            gbLang = vi.Language ?? "tr";
                        }

                        _cache.Set(cacheKey, (gbDesc, gbPage, gbLang), TimeSpan.FromMinutes(5));
                    }
                    catch
                    {
                        // sessiz geç
                    }
                }
                else
                {
                    gbDesc = gb.Value.desc;
                    gbPage = gb.Value.page;
                    gbLang = gb.Value.lang;
                }
            }

            // 5) ViewModel’i doldur
            var vm = new BookDetailsVm
            {
                Book = book,
                CurrentBorrow = currentBorrow,
                CurrentBorrower = currentBorrower,
                History = borrows,
                TotalBorrowCount = totalBorrowCount,
                LateReturnCount = lateReturnCount,
                AlsoBorrowedTop = alsoBorrowedTop,
                SimilarByAuthor = similarByAuthor,
                SimilarByCategory = similarByCategory,
                GbDescription = gbDesc,
                GbPageCount = gbPage,
                GbLanguage = gbLang
            };

            return View(vm);
        }


        [HttpGet]
        public async Task<IActionResult> GetPopularBooks()
        {
            const string cacheKey = "popular-books";
            if (!_cache.TryGetValue(cacheKey, out List<BookApiViewModel> popularBooks))
            {
                var popularQueries = new[] { "harry potter", "sapiens", "1984", "dune" };
                var tasks = popularQueries.Select(q => SearchInGoogleBooks(q, 1));
                var results = await Task.WhenAll(tasks);

                popularBooks = results.Where(x => x?.Count > 0).Select(x => x[0]).Take(4).ToList();

                _cache.Set(cacheKey, popularBooks, TimeSpan.FromMinutes(1));
            }
            return Json(popularBooks);
        }

        private async Task<List<BookApiViewModel>> SearchInGoogleBooks(string query, int maxResults = 10)
        {
            try
            {
                var fields =
                    "items(id,volumeInfo(title,authors,categories,publisher,publishedDate,industryIdentifiers,imageLinks/thumbnail,description))";

                var apiUrl =
                    $"https://www.googleapis.com/books/v1/volumes" +
                    $"?q={Uri.EscapeDataString(query)}" +
                    $"&maxResults={maxResults}" +
                    $"&printType=books" +
                    $"&fields={Uri.EscapeDataString(fields)}";

                var response = await _httpClient.GetStringAsync(apiUrl);
                var result = JsonConvert.DeserializeObject<GoogleBooksApiResponse>(response);

                return result?.Items?
                    .Where(item => item?.VolumeInfo != null)
                    .Select(item => new BookApiViewModel
                    {
                        Id = item.Id,
                        Title = item.VolumeInfo.Title,
                        Author = item.VolumeInfo.Authors != null ? string.Join(", ", item.VolumeInfo.Authors) : "Bilinmiyor",
                        Publisher = item.VolumeInfo.Publisher ?? "Bilinmiyor",
                        ISBN = item.VolumeInfo.IndustryIdentifiers?.FirstOrDefault()?.Identifier ?? "Bilinmiyor",
                        PublishedDate = item.VolumeInfo.PublishedDate ?? "Bilinmiyor",
                        ThumbnailUrl = item.VolumeInfo.ImageLinks?.Thumbnail?.Replace("http://", "https://") ?? "/images/book.png",
                        Category = item.VolumeInfo.Categories != null ? string.Join(", ", item.VolumeInfo.Categories) : "Genel",
                        Description = item.VolumeInfo.Description ?? "Açıklama yok"
                    })
                    .ToList() ?? new List<BookApiViewModel>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google Books API hatası");
                return new List<BookApiViewModel>();
            }
        }
        [HttpGet]
        public async Task<IActionResult> GetBookByIdFromApi(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { error = "Geçersiz id" });

            try
            {
                var fields = "id,volumeInfo(title,authors,categories,publisher,publishedDate,description,pageCount,language,industryIdentifiers,imageLinks/thumbnail,previewLink)";
                var url = $"https://www.googleapis.com/books/v1/volumes/{Uri.EscapeDataString(id)}?fields={Uri.EscapeDataString(fields)}";

                var json = await _httpClient.GetStringAsync(url);
                var item = JsonConvert.DeserializeObject<GoogleBookItem>(json);

                if (item?.VolumeInfo == null)
                    return NotFound(new { error = "Detay bulunamadı." });

                var dto = new
                {
                    id = item.Id,
                    title = item.VolumeInfo.Title ?? "Başlık yok",
                    author = item.VolumeInfo.Authors != null ? string.Join(", ", item.VolumeInfo.Authors) : "Bilinmiyor",
                    publisher = item.VolumeInfo.Publisher ?? "Bilinmiyor",
                    isbn = item.VolumeInfo.IndustryIdentifiers?.FirstOrDefault()?.Identifier ?? "Bilinmiyor",
                    publishedDate = item.VolumeInfo.PublishedDate ?? "Bilinmiyor",
                    language = item.VolumeInfo.Language ?? "tr",
                    pageCount = item.VolumeInfo.PageCount?.ToString() ?? "Bilinmiyor",
                    description = item.VolumeInfo.Description ?? "Açıklama yok",
                    thumbnailUrl = item.VolumeInfo.ImageLinks?.Thumbnail?.Replace("http://", "https://") ?? "/images/book.png",
                    category = item.VolumeInfo.Categories != null ? string.Join(", ", item.VolumeInfo.Categories) : "Genel",
                    previewLink = item.VolumeInfo.PreviewLink
                };

                return Json(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetBookByIdFromApi hata");
                return StatusCode(500, new { error = "Detay alınamadı." });
            }
        }


    }

}
    

