using KutuphaneOtomasyon.ViewModels.Members;
using Microsoft.EntityFrameworkCore;

namespace KutuphaneOtomasyon.Services.Recommendations
{
    public class RecommendationService : IRecommendationService
    {
        private readonly Data.LibraryContext _db;
        public RecommendationService(Data.LibraryContext db) => _db = db;

        public List<AiSuggestionVm> RecommendForMember(int memberId, int take = 8)
        {
            var history = _db.Borrows.Include(x => x.Book)
                                     .Where(x => x.MemberId == memberId)
                                     .ToList();

            var likedAuthors = history.Where(h => !string.IsNullOrWhiteSpace(h.Book.Author))
                                      .GroupBy(h => h.Book.Author)
                                      .OrderByDescending(g => g.Count())
                                      .Select(g => g.Key!)
                                      .Take(3).ToList();

            var likedCats = history.Where(h => !string.IsNullOrWhiteSpace(h.Book.Category))
                                   .GroupBy(h => h.Book.Category)
                                   .OrderByDescending(g => g.Count())
                                   .Select(g => g.Key!)
                                   .Take(3).ToList();

            var already = history.Select(h => h.BookId).ToHashSet();

            var pool = _db.Books
                .Where(b => b.AvailableCopies > 0 && !already.Contains(b.BookId))
                .Where(b => likedAuthors.Contains(b.Author!) || likedCats.Contains(b.Category!))
                .Take(take * 3)
                .ToList();

            return pool
                .OrderByDescending(b => likedAuthors.Contains(b.Author ?? "") ? 1 : 0)
                .ThenByDescending(b => likedCats.Contains(b.Category ?? "") ? 1 : 0)
                .Take(take)
                .Select(b => new AiSuggestionVm
                {
                    BookId = b.BookId,
                    Title = b.Title,
                    Author = b.Author,
                    ThumbnailUrl = b.ThumbnailUrl,
                    AvailableCopies = b.AvailableCopies,
                    Reason = likedAuthors.Contains(b.Author ?? "")
                        ? $"Sık okuduğunuz yazar: {b.Author}"
                        : $"İlgili kategori: {b.Category}"
                })
                .ToList();
        }
    }
}
