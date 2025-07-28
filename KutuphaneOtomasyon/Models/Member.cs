using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;

namespace KutuphaneOtomasyon.Models
{
    public class Member
    {
        public int MemberId { get; set; }
        [Required(ErrorMessage = "Üye ismi gereklidir.")]
        public string Name { get; set; }
        [Required(ErrorMessage = "Üye e-postası gereklidir.")]

        public string Email { get; set; }

        [Required]
        public MemberTypeEnum MemberType { get; set; }

        [ValidateNever]
        public ICollection<Borrow> Borrows { get; set; }

    }
    public enum MemberTypeEnum
    {
        Öğrenci,
        Akademisyen,
        DışKullanıcı
    }

}
