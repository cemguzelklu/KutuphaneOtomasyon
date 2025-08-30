using KutuphaneOtomasyon.Data;
using KutuphaneOtomasyon.Models;
using KutuphaneOtomasyon.Models.Dtos;
using KutuphaneOtomasyon.Models.Mappings;
using KutuphaneOtomasyon.Services;
using KutuphaneOtomasyon.ViewModels.Books;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace KutuphaneOtomasyon.Controllers
{
    public class BookController : Controller
    {
        private readonly LibraryContext _context;
        private readonly ILogger<BookController> _logger;
        private readonly IMemoryCache _cache;
        private readonly IBookLookupService _lookup;
        private readonly IBookMetaFetcher _bookMetaFetcher; // yeni eklenen servis

        public BookController(
            LibraryContext context,
            ILogger<BookController> logger,
            IMemoryCache cache,
            IBookLookupService lookup,
            IBookMetaFetcher bookMetaFetcher)
        {
            _context = context;
            _logger = logger;
            _cache = cache;
            _lookup = lookup;
            _bookMetaFetcher = bookMetaFetcher;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (HttpContext.Session.GetString("IsAdmin") != "true")
            {
                context.Result = RedirectToAction("Login", "Auth");
            }
            base.OnActionExecuting(context);
        }

        // ========== CRUD ==========
        public async Task<IActionResult> Index()
        {
            var books = await _context.Books.AsNoTracking().ToListAsync();
            return View(books);
        }

        public IActionResult Create() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Book book)
        {
            if (book.AvailableCopies > book.TotalCopies)
                ModelState.AddModelError("AvailableCopies", "Mevcut kitap sayısı, toplam kitap sayısını geçemez.");

            // ISBN normalize (CleanISBN)
            if (!string.IsNullOrWhiteSpace(book.ISBN))
                book.CleanISBN = IsbnHelper.NormalizeIsbn13(book.ISBN) ?? book.ISBN?.Replace("-", "").Replace(" ", "");

            if (!ModelState.IsValid) return View(book);

            _context.Books.Add(book);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Update(int? id)
        {
            if (id == null) return NotFound();
            var book = await _context.Books.FindAsync(id);
            if (book == null) return NotFound();
            return View(book);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(int id, Book book)
        {
            if (id != book.BookId) return NotFound();
            if (book.AvailableCopies > book.TotalCopies)
                ModelState.AddModelError("AvailableCopies", "Mevcut kitap sayısı, toplam kitap sayısını geçemez.");

            if (!ModelState.IsValid) return View(book);

            // ISBN normalize (CleanISBN)
            if (!string.IsNullOrWhiteSpace(book.ISBN))
                book.CleanISBN = IsbnHelper.NormalizeIsbn13(book.ISBN) ?? book.ISBN?.Replace("-", "").Replace(" ", "");

            try
            {
                _context.Update(book);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Books.Any(e => e.BookId == id)) return NotFound();
                throw;
            }
            return RedirectToAction(nameof(Index));
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

        // ========== DETAY ==========
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var book = await _context.Books
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BookId == id);

            if (book == null) return NotFound();

            // Ödünç geçmişi
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

            // “Bu kitabı alanların aldığı” top 10
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

            // ViewModel
            var vm = new BookDetailsVm
            {
                Book = book,
                CurrentBorrow = currentBorrow,
                CurrentBorrower = currentBorrower,
                History = borrows,
                TotalBorrowCount = totalBorrowCount,
                LateReturnCount = lateReturnCount,
                AlsoBorrowedTop = alsoBorrowedTop,
                // Aşağıdaki üç alanı eskiden Google’dan çekiyordun; istersen IBookLookupService ile zenginleştirebiliriz.
                GbDescription = null,
                GbPageCount = null,
                GbLanguage = null
            };

            return View(vm);
        }

        // ========== API / MODAL ==========

        // Arama sayfası (görünüm)
        public IActionResult SearchInApi() => View();

        // Arama sonuçları (JSON veya View) — Artık federated
        [HttpGet]
        public async Task<IActionResult> SearchInApi(string query, bool returnJson = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return returnJson ? Json(new List<object>()) : View(new List<object>());

            try
            {
                var list = await _lookup.SearchCombinedAsync(query, includeLocal: true, includeGoogle: true);

                // frontend’in beklediği şekle projeksiyon
                var vm = list.Select(x => new {
                    id = x.Id,
                    title = x.Title,
                    author = x.Author,
                    publisher = x.Publisher ?? "Bilinmiyor",
                    isbn = x.Isbn13 ?? x.Isbn10 ?? "",
                    publishedDate = x.PublishedDate ?? "Bilinmiyor",
                    language = x.Language ?? "tr",
                    pageCount = x.PageCount?.ToString() ?? "",
                    category = string.IsNullOrWhiteSpace(x.Category) ? "Genel" : x.Category,
                    thumbnailUrl = string.IsNullOrWhiteSpace(x.ThumbnailUrl) ? "/images/book.png" : x.ThumbnailUrl,
                    description = string.IsNullOrWhiteSpace(x.Description) ? "Açıklama yok" : x.Description,
                    source = x.Source.ToString()
                }).ToList();

                return returnJson ? Json(vm) : View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SearchInApi hata");
                if (returnJson) return Json(new { error = ex.Message });
                TempData["ErrorMessage"] = $"Arama sırasında hata oluştu: {ex.Message}";
                return View(new List<object>());
            }
        }

        // Popüler kitaplar — basit örnek (federated)
        [HttpGet]
        public async Task<IActionResult> GetPopularBooks(CancellationToken ct)
        {
            const string cacheKey = "popular-books-v2";
            if (!_cache.TryGetValue(cacheKey, out List<object>? cached))
            {
                var list = await _lookup.SearchAllAsync("subject:fiction", ct);
                cached = list.Take(12).Select(b => new
                {
                    id = b.Id,
                    title = b.Title ?? "Başlık yok",
                    author = string.IsNullOrWhiteSpace(b.Author) ? "Bilinmiyor" : b.Author,
                    publisher = string.IsNullOrWhiteSpace(b.Publisher) ? "Bilinmiyor" : b.Publisher,
                    category = string.IsNullOrWhiteSpace(b.Category) ? "Genel" : b.Category,
                    isbn = b.Isbn13 ?? b.Isbn10 ?? "",
                    publishedDate = b.PublishedDate,
                    thumbnailUrl = string.IsNullOrWhiteSpace(b.ThumbnailUrl) ? "/images/book.png" : b.ThumbnailUrl,
                    description = string.IsNullOrWhiteSpace(b.Description) ? "Açıklama yok" : b.Description
                } as object).ToList();

                _cache.Set(cacheKey, cached, TimeSpan.FromMinutes(3));
            }

            return Json(cached);
        }

        // Kart tıklanınca detay (id: “l:12”, “g:...”, “o:...”)
        [HttpGet]
        public async Task<IActionResult> GetBookByIdFromApi(string id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { error = "Geçersiz id" });

            try
            {
                var dto = await _bookMetaFetcher.GetByIdAsync(id);
                if (dto is null)
                    return NotFound(new { error = "Detay bulunamadı." });

                var payload = new
                {
                    id = dto.Id,
                    title = dto.Title,
                    author = dto.Author,
                    publisher = dto.Publisher ?? "Bilinmiyor",
                    isbn = dto.Isbn13 ?? dto.Isbn10 ?? "",
                    publishedDate = dto.PublishedDate ?? "Bilinmiyor",
                    language = dto.Language ?? "tr",
                    pageCount = dto.PageCount?.ToString() ?? "Bilinmiyor",
                    description = string.IsNullOrWhiteSpace(dto.Description) ? "Açıklama yok" : dto.Description,
                    thumbnailUrl = string.IsNullOrWhiteSpace(dto.ThumbnailUrl) ? "/images/book.png" : dto.ThumbnailUrl,
                    category = string.IsNullOrWhiteSpace(dto.Category) ? "Genel" : dto.Category,
                };

                return Json(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetBookByIdFromApi hata");
                return StatusCode(500, new { error = "Detay alınamadı." });
            }
        }


            // Karttaki “Kütüphaneye Ekle” (formun gönderdiği alan adlarını korudum)
            [HttpPost]
            [ValidateAntiForgeryToken]
            public async Task<IActionResult> AddFromApi(
            string bookId,
            string isbn,
            string title,
            string author,
            string? publisher,
            string? thumbnailUrl,
            string? category,
            string? description)
            {
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(author))
                {
                    TempData["ErrorMessage"] = "Kitap adı ve yazar bilgisi zorunludur.";
                    return RedirectToAction("SearchInApi");
                }

                try
                {
                    var cleanIsbn = !string.IsNullOrWhiteSpace(isbn)
                        ? (IsbnHelper.NormalizeIsbn13(isbn) ?? isbn.Replace("-", "").Replace(" ", ""))
                        : null;

                    // Aynı ISBN varsa stok arttır
                    Book? existingBook = null;
                    if (!string.IsNullOrWhiteSpace(isbn) && !string.IsNullOrWhiteSpace(cleanIsbn))
                    {
                        existingBook = await _context.Books
                            .FirstOrDefaultAsync(b => b.ISBN == isbn || b.CleanISBN == cleanIsbn);
                    }
                    else
                    {
                        existingBook = await _context.Books
                            .FirstOrDefaultAsync(b => b.Title == title.Trim() && b.Author == author.Trim());
                    }

                    if (existingBook != null)
                    {
                        existingBook.TotalCopies += 1;
                        existingBook.AvailableCopies += 1;
                        await _context.SaveChangesAsync();
                        TempData["SuccessMessage"] = $"{existingBook.Title} stok sayısı güncellendi.";
                    }
                    else
                    {
                        var entity = new Book
                        {
                            Title = title.Trim(),
                            Author = author.Trim(),
                            ISBN = string.IsNullOrWhiteSpace(isbn) ? null : isbn.Trim(),
                            CleanISBN = cleanIsbn,
                            TotalCopies = 1,
                            AvailableCopies = 1,
                            Category = string.IsNullOrWhiteSpace(category) ? "Genel" : category,
                            Publisher = string.IsNullOrWhiteSpace(publisher) ? "Bilinmiyor" : publisher,
                            ThumbnailUrl = thumbnailUrl,
                            Description = description
                        };

                        _context.Books.Add(entity);
                        await _context.SaveChangesAsync();
                        TempData["SuccessMessage"] = $"{entity.Title} kütüphaneye eklendi.";
                    }

                    return RedirectToAction("Index");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AddFromApi hata");
                    TempData["ErrorMessage"] = $"Hata: {ex.Message}";
                    return RedirectToAction("SearchInApi");
                }
            }
        [HttpGet]
        public async Task<IActionResult> LookupIsbn(string isbn, string scope = "both")
        {
            if (string.IsNullOrWhiteSpace(isbn))
                return BadRequest(new { error = "ISBN boş olamaz." });

            var sc = scope?.ToLowerInvariant() switch
            {
                "local" => LookupScope.Local,
                "google" => LookupScope.Google,
                _ => LookupScope.Both
            };

            try
            {
                var res = await _lookup.LookupIsbnAsync(isbn, sc);

                // JSON: yerel + google tek pakette
                return Json(new
                {
                    local = res.Local == null ? null : new
                    {
                        id = res.Local.BookId,
                        title = res.Local.Title,
                        author = res.Local.Author,
                        isbn = res.Local.ISBN ?? res.Local.CleanISBN,
                        category = res.Local.Category,
                        available = res.Local.AvailableCopies,
                        total = res.Local.TotalCopies,
                        thumbnailUrl = string.IsNullOrWhiteSpace(res.Local.ThumbnailUrl) ? "/images/book.png" : res.Local.ThumbnailUrl,
                        publisher = res.Local.Publisher
                    },
                    google = res.Google // BookDto zaten UI’nin beklediği alanları içeriyor
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LookupIsbn hata");
                return StatusCode(500, new { error = "ISBN sorgusunda hata oluştu." });
            }
        }
        public IActionResult ScanPhone(string token)
        {
            ViewBag.Token = token;
            return View();
        }
    }
}