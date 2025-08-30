using KutuphaneOtomasyon.Models;
using KutuphaneOtomasyon.Models.Dtos;

namespace KutuphaneOtomasyon.Models.Mappings
{
    public static class BookMappings
    {
        // DB'deki Book → Dışarı göndereceğimiz DTO
        public static BookDto ToDto(this Book b)
        {
            // ISBN'leri belirle
            var raw = b.CleanISBN ?? b.ISBN;
            string i13 = IsbnHelper.NormalizeIsbn13(raw);
            string i10 = (i13 != null) ? IsbnHelper.ToIsbn10(i13) : (IsbnHelper.OnlyDigits(raw)?.Length == 10 ? IsbnHelper.OnlyDigits(raw) : null);

            return new BookDto
            {
                Id = b.BookId.ToString(),
                Isbn13 = i13,
                Isbn10 = i10,
                Title = b.Title,
                Author = b.Author,
                Publisher = b.Publisher,
                Category = b.Category,
                PublishedDate = b.PublishedDate,
                Language = b.Language,
                PageCount = null,                 // DB'de yoksa boş bırak
                ThumbnailUrl = b.ThumbnailUrl,
                Description = b.Description,
                Source = BookSource.Local
            };
        }

        // Dış kaynaktan gelen DTO → DB'ye eklenecek Book (insert için)
        public static Book ToEntityForInsert(this BookDto dto)
        {
            // ISBN'i 13'e normalize etmeyi tercih ediyoruz (barkodla uyumlu)
            var i13 = IsbnHelper.NormalizeIsbn13(dto.Isbn13 ?? dto.Isbn10);
            var clean10 = dto.Isbn10 != null ? IsbnHelper.OnlyDigits(dto.Isbn10) : null;

            return new Book
            {
                Title = dto.Title ?? "Başlıksız",
                Author = dto.Author ?? "Bilinmiyor",
                Category = string.IsNullOrWhiteSpace(dto.Category) ? "Genel" : dto.Category,

                // Mevcut model alanlarına göre doldur
                ISBN = i13 ?? clean10,       // tek alanın varsa buraya yaz
                CleanISBN = i13 ?? clean10,  // digits-only tutmak istersen

                TotalCopies = 1,
                AvailableCopies = 1,

                Description = dto.Description,
                Language = dto.Language,
                Publisher = dto.Publisher,
                PublishedDate = dto.PublishedDate,
                ThumbnailUrl = dto.ThumbnailUrl
            };
        }
    }
}
