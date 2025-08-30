using KutuphaneOtomasyon.Models.Dtos;

namespace KutuphaneOtomasyon.Services.Clients
{
    public interface IGoogleBooksClient
    {
        Task<List<BookDto>> SearchAsync(string query, int max = 12, CancellationToken ct = default);
        Task<BookDto?> GetByIdAsync(string id, CancellationToken ct = default); // Google volumeId
        Task<BookDto?> LookupByIsbnAsync(string isbn, CancellationToken ct = default);
    }
}
