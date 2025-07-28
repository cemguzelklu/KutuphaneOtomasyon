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
        [Range(1, int.MaxValue, ErrorMessage = "Toplam kopya sayısı 1 veya daha fazla olmalıdır.")]
        [Required(ErrorMessage = "Kitap sayısı gereklidir.")]
        public int TotalCopies { get; set; }
        [Range(0, int.MaxValue, ErrorMessage = "Mevcut kopya sayısı 0 veya daha fazla olmalıdır.")]
        [Required(ErrorMessage = "Aktif kitap sayısı gereklidir. ")]
        public int AvailableCopies { get; set; }
        [ValidateNever]
        public ICollection<Borrow>? Borrows { get; set; }
    }
}
