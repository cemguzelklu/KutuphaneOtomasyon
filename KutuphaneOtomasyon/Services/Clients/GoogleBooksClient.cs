using System.Net.Http.Json;
using System.Text.Json;
using KutuphaneOtomasyon.Models.Dtos;
using KutuphaneOtomasyon.Services.Clients;

namespace KutuphaneOtomasyon.Services.Clients
{
    public class GoogleBooksClient : IGoogleBooksClient
    {
        private readonly HttpClient _http;

        public GoogleBooksClient(HttpClient http)
        {
            _http = http;
            _http.BaseAddress = new Uri("https://www.googleapis.com/books/v1/");
        }

        public async Task<List<BookDto>> SearchAsync(string query, int max = 12, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query)) return new();

            var url = $"volumes?q={Uri.EscapeDataString(query)}&maxResults={Math.Clamp(max, 1, 40)}";
            using var res = await _http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return new();

            using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var list = new List<BookDto>();
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var item in items.EnumerateArray())
            {
                var id = item.GetStringProp("id");
                var info = item.GetObjectProp("volumeInfo"); // <— artık JsonElement (Object)

                var (isbn13, isbn10) = ExtractIsbns(info);
                var title = info.GetStringProp("title");
                var author = string.Join(", ", info.GetArrayStrings("authors"));
                var publisher = info.GetStringProp("publisher");
                var publishedDate = info.GetStringProp("publishedDate");
                var language = info.GetStringProp("language");
                var pageCount = info.GetInt32OrNull("pageCount");
                var categories = info.GetArrayStrings("categories");
                var category = categories.FirstOrDefault() ?? "Genel";
                var description = info.GetStringProp("description");

                var imageLinks = info.GetObjectProp("imageLinks");
                var thumb = imageLinks.GetStringProp("thumbnail");
                if (!string.IsNullOrEmpty(thumb)) thumb = thumb.Replace("http://", "https://");

                list.Add(new BookDto
                {
                    Id = string.IsNullOrEmpty(id) ? "" : $"g:{id}",
                    Isbn13 = isbn13,
                    Isbn10 = isbn10,
                    Title = string.IsNullOrWhiteSpace(title) ? "Başlık yok" : title,
                    Author = string.IsNullOrWhiteSpace(author) ? "Bilinmiyor" : author,
                    Publisher = publisher,
                    Category = category,
                    PublishedDate = publishedDate,
                    Language = language,
                    PageCount = pageCount,
                    ThumbnailUrl = thumb,
                    Description = description,
                    Source = BookSource.Google
                });
            }
            return list;
        }

        public async Task<BookDto?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            // “g:xxxxx” veya ham volumeId gelebilir
            var volId = id.StartsWith("g:") ? id[2..] : id;

            using var res = await _http.GetAsync($"volumes/{Uri.EscapeDataString(volId)}", ct);
            if (!res.IsSuccessStatusCode) return null;

            var doc = await res.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            if (doc is null) return null;

            var root = doc.RootElement;
            var info = root.GetObjectProp("volumeInfo");

            var (isbn13, isbn10) = ExtractIsbns(info);
            var title = info.GetStringProp("title");
            var author = string.Join(", ", info.GetArrayStrings("authors"));
            var publisher = info.GetStringProp("publisher");
            var publishedDate = info.GetStringProp("publishedDate");
            var language = info.GetStringProp("language");
            var pageCount = info.GetInt32OrNull("pageCount");
            var categories = info.GetArrayStrings("categories");
            var category = categories.FirstOrDefault() ?? "Genel";
            var description = info.GetStringProp("description");

            var imageLinks = info.GetObjectProp("imageLinks");
            var thumb = imageLinks.GetStringProp("thumbnail");
            if (!string.IsNullOrEmpty(thumb)) thumb = thumb.Replace("http://", "https://");

            var rootId = root.GetStringProp("id");

            return new BookDto
            {
                Id = string.IsNullOrEmpty(rootId) ? $"g:{volId}" : $"g:{rootId}",
                Isbn13 = isbn13,
                Isbn10 = isbn10,
                Title = string.IsNullOrWhiteSpace(title) ? "Başlık yok" : title,
                Author = string.IsNullOrWhiteSpace(author) ? "Bilinmiyor" : author,
                Publisher = publisher,
                Category = category,
                PublishedDate = publishedDate,
                Language = language,
                PageCount = pageCount,
                ThumbnailUrl = thumb,
                Description = description,
                Source = BookSource.Google
            };
        }

        public async Task<BookDto?> LookupByIsbnAsync(string isbn, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(isbn)) return null;

            var url = $"volumes?q=isbn:{Uri.EscapeDataString(isbn)}&maxResults=5";
            using var res = await _http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return null;

            var doc = await res.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            if (doc is null || !doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return null;

            var want = IsbnHelper.NormalizeIsbn13(isbn) ?? isbn;
            string? firstId = null;

            foreach (var it in items.EnumerateArray())
            {
                var id = it.GetStringProp("id");
                if (string.IsNullOrEmpty(firstId)) firstId = id;

                var info = it.GetObjectProp("volumeInfo");
                var (i13, i10) = ExtractIsbns(info);

                if (!string.IsNullOrEmpty(i13) && i13 == want)
                    return await GetByIdAsync($"g:{id}", ct);
            }

            // net eşleşme yoksa ilk kaydı döndür
            return string.IsNullOrEmpty(firstId) ? null : await GetByIdAsync($"g:{firstId}", ct);
        }

        private static (string? i13, string? i10) ExtractIsbns(JsonElement info)
        {
            if (info.ValueKind != JsonValueKind.Object) return (null, null);

            string? i13 = null, i10 = null;

            if (info.TryGetProperty("industryIdentifiers", out var ids) && ids.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in ids.EnumerateArray())
                {
                    var type = id.GetStringProp("type");
                    var val = id.GetStringProp("identifier");

                    if (type == "ISBN_13")
                        i13 = IsbnHelper.NormalizeIsbn13(val);

                    if (type == "ISBN_10")
                    {
                        var digits = IsbnHelper.OnlyDigits(val ?? "");
                        if (!string.IsNullOrEmpty(digits) && digits.Length == 10)
                            i10 = val;
                    }
                }
            }
            return (i13, i10);
        }
    }

}
