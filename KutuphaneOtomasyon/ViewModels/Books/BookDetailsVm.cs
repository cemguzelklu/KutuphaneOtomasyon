using System.Collections.Generic;
using KutuphaneOtomasyon.Models;

namespace KutuphaneOtomasyon.ViewModels.Books
{
    public class BookDetailsVm
    {
        public Book Book { get; set; } = default!;

        // Şu an kimde?
        public Borrow? CurrentBorrow { get; set; }
        public Member? CurrentBorrower { get; set; }

        // Geçmiş
        public List<Borrow> History { get; set; } = new();
        public int TotalBorrowCount { get; set; }
        public int LateReturnCount { get; set; }

        // “Bu kitabı alanların başka aldığı”
        public List<AlsoBorrowedVm> AlsoBorrowedTop { get; set; } = new();

        // Benzer kitaplar
        public List<Book> SimilarByAuthor { get; set; } = new();
        public List<Book> SimilarByCategory { get; set; } = new();

        // Google Books zengin alanları (opsiyonel)
        public string? GbDescription { get; set; }
        public string? GbPageCount { get; set; }
        public string? GbLanguage { get; set; }
    }

    public class AlsoBorrowedVm
    {
        public int BookId { get; set; }
        public string Title { get; set; } = "";
        public string? Author { get; set; }
        public int Count { get; set; }
        public int AvailableCopies { get; set; }
        public string? ThumbnailUrl { get; set; }
    }
}
