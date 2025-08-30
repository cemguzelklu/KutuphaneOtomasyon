using KutuphaneOtomasyon.Models.Dtos;

namespace KutuphaneOtomasyon.Services.Clients
{
    public interface IOpenLibraryClient
    {
        Task<List<BookDto>> SearchAsync(string query, int max = 12, CancellationToken ct = default);
        Task<BookDto?> LookupByIsbnAsync(string isbn, CancellationToken ct = default);
        Task<BookDto?> GetByIdAsync(string id, CancellationToken ct = default); // “o:{editionKey|work key}”
    }
}
