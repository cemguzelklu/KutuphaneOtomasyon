using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;

namespace KutuphaneOtomasyon.Models
{
    public class Borrow
    {
        public int BorrowId { get; set; }
        [Display(Name = "Kitap")]
        public int BookId { get; set; }
        [ValidateNever]
        public Book Book { get; set; }
        [Display(Name = "Üye")]
        public int MemberId { get; set; }
        [ValidateNever]
        public Member Member { get; set; }
        [Display(Name = "Alış Tarihi")]
        public DateTime BorrowDate { get; set; }
        [Display(Name = "İade Tarihi (Gerçek)")]
        public DateTime? ReturnDate { get; set; }
        [Display(Name = "Son Teslim (Planlanan)")]
        public DateTime? DueDate { get; set; }



    }
}
