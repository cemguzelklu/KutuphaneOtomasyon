using KutuphaneOtomasyon.Models.Dtos;
using System.Threading.Tasks;

public interface IBookMetaFetcher
{
    Task<BookMeta> PeekAsync(string isbn13);  // varsa getir (kaydetmeden)
    Task<BookMeta> FetchAsync(string isbn13); // indir ve dön
    Task<BookDto?> GetByIdAsync(string id, CancellationToken ct = default);
}

public class BookMeta
{
    public string Title { get; set; }
    public string Author { get; set; }
    public string Publisher { get; set; }
    public string PublishedYear { get; set; }
    public int PageCount { get; set; }
    public string CoverUrl { get; set; }
}
