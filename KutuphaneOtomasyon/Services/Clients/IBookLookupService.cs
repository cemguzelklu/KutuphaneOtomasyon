using KutuphaneOtomasyon.Models;
using KutuphaneOtomasyon.Models.Dtos;

namespace KutuphaneOtomasyon.Services
{
    public enum LookupScope { Local, Google, Both }

    public class LookupResultDto
    {
        public Book? Local { get; set; }
        public Models.Dtos.BookDto? Google { get; set; }

        public bool FoundInLocal => Local != null;
        public bool FoundInGoogle => Google != null;
    }

    public interface IBookLookupService
    {
        Task<List<BookDto>> SearchCombinedAsync(
           string query,
           bool includeLocal = true,
           bool includeGoogle = true,
           CancellationToken ct = default);

        Task<BookDto?> LookupByIsbnAsync(
            string isbn,
            bool includeLocal = true,
            bool includeGoogle = true,
            CancellationToken ct = default);
        Task<List<BookDto>> SearchAllAsync(string query, CancellationToken ct = default);
        Task<List<BookDto>> SearchLocalAsync(string query, CancellationToken ct = default);
        Task<BookDto?> GetByCompositeIdAsync(string id, CancellationToken ct = default); // "l:", "g:", "o:"
        Task<List<BookDto>> LookupIsbnFederatedAsync(string isbn, CancellationToken ct = default); // Local + Google + OpenLibrary
        Task<LookupResultDto> LookupIsbnAsync(string isbn, LookupScope scope, CancellationToken ct = default);
    }
}
