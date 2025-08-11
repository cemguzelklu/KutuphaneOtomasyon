using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;

namespace KutuphaneOtomasyon.Models
{
    public class Book
    {
        public int BookId { get; set; }
        [Required(ErrorMessage = "Kitap başlığı gereklidir.")]
        public string Title { get; set; }
        [Required(ErrorMessage = "Yazar adı gereklidir.")]
        public string Author { get; set; }
        [Required(ErrorMessage = "Kategori gereklidir.")]
        public string Category { get; set; }


        // ISBN ekleyelim (API'den gelecek)z
        public string? ISBN { get; set; }
        public string? CleanISBN { get; set; } // Yeni eklenen alan

        [Range(1, int.MaxValue, ErrorMessage = "Toplam kopya sayısı 1 veya daha fazla olmalıdır.")]
        [Required(ErrorMessage = "Kitap sayısı gereklidir.")]
        public int TotalCopies { get; set; }
        [Range(0, int.MaxValue, ErrorMessage = "Mevcut kopya sayısı 0 veya daha fazla olmalıdır.")]
        [Required(ErrorMessage = "Aktif kitap sayısı gereklidir. ")]
        public int AvailableCopies { get; set; }

        // API'den gelecek opsiyonel alanlar
        public string? Description { get; set; }
        public string? Language { get; set; }
        public string? Publisher { get; set; }
        public string? PublishedDate { get; set; }
        public string? ThumbnailUrl { get; set; } // Kitap kapağı URL'si

        [ValidateNever]
        public ICollection<Borrow>? Borrows { get; set; }
    }
}
