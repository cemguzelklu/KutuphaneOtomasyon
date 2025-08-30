using KutuphaneOtomasyon.Data;
using KutuphaneOtomasyon.Models;
using KutuphaneOtomasyon.Models.Dtos;
using KutuphaneOtomasyon.Models.Mappings;
using KutuphaneOtomasyon.Services.Clients;
using Microsoft.EntityFrameworkCore;

namespace KutuphaneOtomasyon.Services
{
    public class BookLookupService : IBookLookupService 
    {
        private readonly LibraryContext _db; // senin DbContext adın neyse onu kullan
        private readonly IGoogleBooksClient _google;
        private readonly IOpenLibraryClient _open;

        public BookLookupService(LibraryContext db, IGoogleBooksClient google, IOpenLibraryClient open)
        {
            _db = db;
            _google = google;
            _open = open;
        }

        public async Task<List<BookDto>> SearchAllAsync(string query, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query)) return new();

            // ISBN mi?
            var qDigits = IsbnHelper.OnlyDigits(query);
            var looksIsbn = qDigits.Length is 10 or 13;

            if (looksIsbn)
            {
                // ISBN ise üç kaynaktan tekil sonuçlar (düz liste)
                return await LookupIsbnFederatedAsync(qDigits, ct);
            }

            // Genel arama → paralel
            var localTask = SearchLocalAsync(query, ct);
            var googleTask = _google.SearchAsync(query, 16, ct);
            var openTask = _open.SearchAsync(query, 16, ct);

            await Task.WhenAll(localTask, googleTask, openTask);

            // Basit birleştirme + ISBN’e göre uniq
            var all = new List<BookDto>();
            all.AddRange(localTask.Result);
            all.AddRange(googleTask.Result);
            all.AddRange(openTask.Result);

            return DistinctByIsbnOrTitleAuthor(all);
        }

        public async Task<List<BookDto>> SearchLocalAsync(string query, CancellationToken ct = default)
        {
            query = query.Trim();
            var qLower = query.ToLowerInvariant();

            var qd = IsbnHelper.OnlyDigits(query);

            var data = await _db.Books
                .AsNoTracking()
                .Where(b =>
                    b.Title.ToLower().Contains(qLower) ||
                    b.Author.ToLower().Contains(qLower) ||
                    (!string.IsNullOrEmpty(qd) && ((b.CleanISBN ?? "").Contains(qd) || (b.ISBN ?? "").Contains(qd)))
                )
                .Take(40)
                .ToListAsync(ct);

            var list = data.Select(b =>
            {
                var dto = b.ToDto();
                dto.Id = $"l:{b.BookId}";
                return dto;
            }).ToList();

            return list;
        }

        public async Task<List<BookDto>> LookupIsbnFederatedAsync(string isbn, CancellationToken ct = default)
        {
            var normalized = IsbnHelper.NormalizeIsbn13(isbn) ?? IsbnHelper.OnlyDigits(isbn);

            // DB
            var local = await _db.Books.AsNoTracking()
                .Where(b => (b.CleanISBN ?? IsbnHelper.OnlyDigits(b.ISBN)).Contains(normalized))
                .Take(3).ToListAsync(ct);

            var localDtos = local.Select(b => { var d = b.ToDto(); d.Id = $"l:{b.BookId}"; return d; }).ToList();

            // External
            var gTask = _google.LookupByIsbnAsync(normalized, ct);
            var oTask = _open.LookupByIsbnAsync(normalized, ct);
            await Task.WhenAll(gTask, oTask);

            var list = new List<BookDto>();
            list.AddRange(localDtos);
            if (gTask.Result is not null) list.Add(gTask.Result);
            if (oTask.Result is not null) list.Add(oTask.Result);

            return DistinctByIsbnOrTitleAuthor(list);
        }

        public async Task<BookDto?> GetByCompositeIdAsync(string id, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            if (id.StartsWith("l:"))
            {
                if (!int.TryParse(id[2..], out var bookId)) return null;
                var b = await _db.Books.AsNoTracking().FirstOrDefaultAsync(x => x.BookId == bookId, ct);
                if (b == null) return null;
                var dto = b.ToDto(); dto.Id = id; return dto;
            }

            if (id.StartsWith("g:")) return await _google.GetByIdAsync(id, ct);
            if (id.StartsWith("o:")) return await _open.GetByIdAsync(id, ct);

            // prefix yoksa Google varsay
            return await _google.GetByIdAsync($"g:{id}", ct);
        }

        private static List<BookDto> DistinctByIsbnOrTitleAuthor(IEnumerable<BookDto> src)
        {
            // önce ISBN-13, sonra ISBN-10, yoksa Title+Author’a göre uniq
            return src
                .GroupBy(x => x.Isbn13 ?? x.Isbn10 ?? $"{x.Title}|{x.Author}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }
        public async Task<List<BookDto>> SearchCombinedAsync(
           string query, bool includeLocal = true, bool includeGoogle = true, CancellationToken ct = default)
        {
            var results = new List<BookDto>();
            var key = Canon(query);

            if (includeLocal)
            {
                // basit başlık/yazar/ISBN taraması
                var local = await _db.Books
                    .AsNoTracking()
                    .Where(b =>
                        (b.Title != null && b.Title.Contains(query)) ||
                        (b.Author != null && b.Author.Contains(query)) ||
                        (b.ISBN != null && b.ISBN == query) ||
                        (b.CleanISBN != null && b.CleanISBN == IsbnHelper.NormalizeIsbn13(query)))
                    .Take(50)
                    .ToListAsync(ct);

                results.AddRange(local.Select(ToDto));
            }

            if (includeGoogle)
            {
                var g = await _google.SearchAsync(query, 12, ct);
                results.AddRange(g);
            }

            // de-dup (ISBN13 öncelikli, yoksa title+author kanoniği)
            var dedup = results
                .GroupBy(x => !string.IsNullOrEmpty(x.Isbn13) ? $"i:{x.Isbn13}" : $"t:{Canon(x.Title)}|{Canon(x.Author)}")
                .Select(g =>
                {
                    // aynı kitap birden fazla kaynaktan gelmişse yerel kaydı öne al
                    var localFirst = g.FirstOrDefault(x => x.Source == BookSource.Local);
                    return localFirst ?? g.First();
                })
                .ToList();

            return dedup;
        }
        public async Task<BookDto?> LookupByIsbnAsync(
            string isbn, bool includeLocal = true, bool includeGoogle = true, CancellationToken ct = default)
        {
            var norm = IsbnHelper.NormalizeIsbn13(isbn) ?? isbn;

            if (includeLocal)
            {
                var b = await _db.Books
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.ISBN == isbn || x.ISBN == norm || x.CleanISBN == norm, ct);

                if (b != null) return ToDto(b);
            }

            if (includeGoogle)
            {
                var g = await _google.LookupByIsbnAsync(isbn, ct);
                if (g != null) return g;
            }

            return null;
        }
        public async Task<LookupResultDto> LookupIsbnAsync(string isbn, LookupScope scope, CancellationToken ct = default)
        {
            var result = new LookupResultDto();
            // normalize ISBN-13 (13 hane değilse yine de “only digits” ile fallback)
            var i13 = IsbnHelper.NormalizeIsbn13(isbn) ?? IsbnHelper.OnlyDigits(isbn);

            if (scope != LookupScope.Google)
            {
                result.Local = await _db.Books
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b =>
                        (b.CleanISBN != null && b.CleanISBN == i13) ||
                        (b.ISBN != null && b.ISBN == i13),
                        ct);
            }

            if (scope != LookupScope.Local)
            {
                // önce 13 dene, bulamazsa ham ISBN’i de bir dene
                result.Google = await _google.LookupByIsbnAsync(i13 ?? isbn, ct)
                                 ?? await _google.LookupByIsbnAsync(isbn, ct);
            }

            return result;
        }

        private static BookDto ToDto(Book b) => new BookDto
        {
            Id = $"db:{b.BookId}",
            Title = b.Title ?? "",
            Author = b.Author ?? "Bilinmiyor",
            Publisher = b.Publisher ?? "Bilinmiyor",
            Category = string.IsNullOrWhiteSpace(b.Category) ? "Genel" : b.Category,
            Description = b.Description ?? "",
            Language = b.Language ?? "tr",
            PublishedDate = b.PublishedDate ?? "",
            ThumbnailUrl = b.ThumbnailUrl,
            Isbn13 = IsbnHelper.NormalizeIsbn13(b.ISBN ?? b.CleanISBN ?? ""),
            Isbn10 = null,
            PageCount = null,
            Source = BookSource.Local
        };

        private static string Canon(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var t = s.Normalize().ToLowerInvariant();
            t = t.Replace('İ', 'i').Replace('I', 'i').Replace('ı', 'i');
            return new string(t.Where(ch => char.IsLetterOrDigit(ch) || ch == ' ').ToArray()).Trim();
        }
    }
}
