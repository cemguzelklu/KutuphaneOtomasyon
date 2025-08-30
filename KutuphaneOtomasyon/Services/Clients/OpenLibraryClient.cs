using System.Net.Http.Json;
using System.Text.Json;
using KutuphaneOtomasyon.Models.Dtos;

namespace KutuphaneOtomasyon.Services.Clients
{
    public class OpenLibraryClient : IOpenLibraryClient
    {
        private readonly HttpClient _http;

        public OpenLibraryClient(HttpClient http)
        {
            _http = http;
            _http.BaseAddress = new Uri("https://openlibrary.org/");
        }

        public async Task<List<BookDto>> SearchAsync(string query, int max = 12, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query)) return new();
            var url = $"search.json?q={Uri.EscapeDataString(query)}&limit={Math.Clamp(max, 1, 50)}";
            using var res = await _http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return new();
            var doc = await res.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            if (doc is null || !doc.RootElement.TryGetProperty("docs", out var docs)) return new();

            var list = new List<BookDto>();
            foreach (var d in docs.EnumerateArray())
            {
                var title = d.GetPropertyOrDefault("title");
                var author = string.Join(", ", d.GetArrayOrEmpty("author_name"));
                var publishers = d.GetArrayOrEmpty("publisher").ToList();
                var publisher = publishers.FirstOrDefault() ?? "";
                var firstYear = d.GetPropertyOrDefault("first_publish_year");
                var languageCodes = d.GetArrayOrEmpty("language");
                var language = languageCodes.FirstOrDefault() ?? "";
                var cat = "Genel";

                // ISBN’ler
                var isbns = d.GetArrayOrEmpty("isbn").ToList();
                var i13 = isbns.FirstOrDefault(x => (x ?? "").Replace("-", "").Length == 13);
                var i10 = isbns.FirstOrDefault(x => (x ?? "").Replace("-", "").Length == 10);

                // Kapak
                var coverId = d.GetPropertyOrDefault("cover_i");
                string thumb = string.IsNullOrEmpty(coverId) ? "" : $"https://covers.openlibrary.org/b/id/{coverId}-M.jpg";

                // id (edition_key varsa onu kullan)
                var edKeys = d.GetArrayOrEmpty("edition_key").ToList();
                var key = edKeys.FirstOrDefault() ?? d.GetPropertyOrDefault("key"); // “/works/..”
                if (string.IsNullOrEmpty(key)) continue;

                list.Add(new BookDto
                {
                    Id = $"o:{key}",
                    Isbn13 = IsbnHelper.NormalizeIsbn13(i13 ?? ""),
                    Isbn10 = (i10 ?? "").Replace("-", ""),
                    Title = title,
                    Author = string.IsNullOrWhiteSpace(author) ? "Bilinmiyor" : author,
                    Publisher = publisher,
                    Category = cat,
                    PublishedDate = string.IsNullOrEmpty(firstYear) ? "" : firstYear,
                    Language = language,
                    PageCount = null,
                    ThumbnailUrl = thumb,
                    Description = "", // search endpoint açıklama vermez
                    Source = BookSource.OpenLibrary
                });
            }
            return list;
        }

        public async Task<BookDto?> LookupByIsbnAsync(string isbn, CancellationToken ct = default)
        {
            var clean = (isbn ?? "").Replace("-", "");
            if (clean.Length is not (10 or 13)) return null;

            // /isbn/{isbn}.json
            using var res = await _http.GetAsync($"isbn/{Uri.EscapeDataString(clean)}.json", ct);
            if (!res.IsSuccessStatusCode) return null;
            var doc = await res.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            var root = doc!.RootElement;

            var title = root.GetPropertyOrDefault("title");
            var publishers = root.GetArrayOrEmpty("publishers").ToList();
            var publisher = publishers.FirstOrDefault() ?? "";
            var publishDate = root.GetPropertyOrDefault("publish_date");
            var numberOfPages = root.GetPropertyOrDefault("number_of_pages");
            int? pageCount = int.TryParse(numberOfPages, out var pc) ? pc : null;

            // kapak (covers: [id])
            string thumb = "";
            if (root.TryGetProperty("covers", out var covers) && covers.ValueKind == JsonValueKind.Array)
            {
                var c = covers.EnumerateArray().FirstOrDefault();
                var id = c.ValueKind == JsonValueKind.Number ? c.GetInt32().ToString() : c.ToString();
                if (!string.IsNullOrEmpty(id))
                    thumb = $"https://covers.openlibrary.org/b/id/{id}-L.jpg";
            }

            // yazar isimlerini almak için ek çağrı gerekebilir; basit tutuyoruz
            var i13 = clean.Length == 13 ? IsbnHelper.NormalizeIsbn13(clean) : null;
            var i10 = clean.Length == 10 ? clean : null;

            return new BookDto
            {
                Id = $"o:{root.GetPropertyOrDefault("key")}", // “/books/OL..M”
                Isbn13 = i13,
                Isbn10 = i10,
                Title = title,
                Author = "",   // basit: boş bırak
                Publisher = publisher,
                Category = "Genel",
                PublishedDate = publishDate,
                Language = "", // basit
                PageCount = pageCount,
                ThumbnailUrl = thumb,
                Description = root.GetPropertyOrDefault("notes"),
                Source = BookSource.OpenLibrary
            };
        }

        public async Task<BookDto?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var key = id.StartsWith("o:") ? id[2..] : id; // "/books/OL..M" veya "/works/.."
            var url = key.TrimStart('/').EndsWith(".json") ? key.TrimStart('/') : $"{key.TrimStart('/')}.json";

            using var res = await _http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return null;
            var doc = await res.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            var root = doc!.RootElement;

            var title = root.GetPropertyOrDefault("title");
            string thumb = "";
            if (root.TryGetProperty("covers", out var covers) && covers.ValueKind == JsonValueKind.Array)
            {
                var c = covers.EnumerateArray().FirstOrDefault();
                var idNum = c.ValueKind == JsonValueKind.Number ? c.GetInt32().ToString() : c.ToString();
                if (!string.IsNullOrEmpty(idNum))
                    thumb = $"https://covers.openlibrary.org/b/id/{idNum}-L.jpg";
            }

            return new BookDto
            {
                Id = $"o:{key}",
                Title = title,
                Author = "",
                Publisher = "",
                Category = "Genel",
                PublishedDate = "",
                Language = "",
                PageCount = null,
                ThumbnailUrl = thumb,
                Description = root.GetPropertyOrDefault("description"),
                Source = BookSource.OpenLibrary
            };
        }
    }
}
