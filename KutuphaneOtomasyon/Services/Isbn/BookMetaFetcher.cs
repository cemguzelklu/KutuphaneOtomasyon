using KutuphaneOtomasyon.Data;
using KutuphaneOtomasyon.Models.Dtos;
using KutuphaneOtomasyon.Services.Clients;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

public class BookMetaFetcher : IBookMetaFetcher
{
    private readonly HttpClient _http;
    private readonly LibraryContext _db;
    private readonly IGoogleBooksClient _google;
    public BookMetaFetcher(HttpClient http,LibraryContext db, IGoogleBooksClient google)
    {
        _db = db;
        _google = google;
        _http = http;
    }


    public async Task<BookMeta> PeekAsync(string isbn13)
        => await FetchFromGoogle(isbn13) ?? await FetchFromOpenLibrary(isbn13);

    public async Task<BookMeta> FetchAsync(string isbn13) => await PeekAsync(isbn13);

    private record GoogleBooksResponse(Item[] items);
    private record Item(VolumeInfo volumeInfo);
    private record VolumeInfo(string title, string[] authors, string publisher, string publishedDate, int? pageCount, ImageLinks imageLinks);
    private record ImageLinks(string thumbnail);

    private async Task<BookMeta> FetchFromGoogle(string isbn13)
    {
        var url = $"https://www.googleapis.com/books/v1/volumes?q=isbn:{isbn13}";
        var json = await _http.GetFromJsonAsync<GoogleBooksResponse>(url);
        var v = json?.items?.FirstOrDefault()?.volumeInfo;
        if (v == null) return null;
        return new BookMeta
        {
            Title = v.title,
            Author = v.authors?.FirstOrDefault(),
            Publisher = v.publisher,
            PublishedYear = v.publishedDate,
            PageCount = v.pageCount ?? 0,
            CoverUrl = v.imageLinks?.thumbnail
        };
    }

    private record OpenLibraryBook(string title, string by_statement, string[] publishers, string publish_date, int? number_of_pages);

    private async Task<BookMeta> FetchFromOpenLibrary(string isbn13)
    {
        var url = $"https://openlibrary.org/isbn/{isbn13}.json";
        try
        {
            var ol = await _http.GetFromJsonAsync<OpenLibraryBook>(url);
            if (ol == null) return null;
            return new BookMeta
            {
                Title = ol.title,
                Author = ol.by_statement,
                Publisher = ol.publishers?.FirstOrDefault(),
                PublishedYear = ol.publish_date,
                PageCount = ol.number_of_pages ?? 0,
                CoverUrl = $"https://covers.openlibrary.org/b/isbn/{isbn13}-L.jpg"
            };
        }
        catch { return null; }
    }
    public async Task<BookDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        if (id.StartsWith("db:"))
        {
            if (!int.TryParse(id[3..], out var bookId)) return null;
            var b = await _db.Books.AsNoTracking().FirstOrDefaultAsync(x => x.BookId == bookId, ct);
            return b is null ? null : new BookDto
            {
                Id = id,
                Title = b.Title ?? "",
                Author = b.Author ?? "Bilinmiyor",
                Publisher = b.Publisher ?? "Bilinmiyor",
                Category = string.IsNullOrWhiteSpace(b.Category) ? "Genel" : b.Category,
                Description = b.Description ?? "",
                Language = b.Language ?? "tr",
                PublishedDate = b.PublishedDate ?? "",
                ThumbnailUrl = b.ThumbnailUrl,
                Isbn13 = IsbnHelper.NormalizeIsbn13(b.ISBN ?? b.CleanISBN ?? ""),
                Source = BookSource.Local
            };
        }

        if (id.StartsWith("g:") || id.Length > 0)
        {
            // “g:” ile gelmese de Google’a düşer
            return await _google.GetByIdAsync(id, ct);
        }

        return null;
    }
}
