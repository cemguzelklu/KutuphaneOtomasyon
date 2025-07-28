using KutuphaneOtomasyon.Models;
using Microsoft.EntityFrameworkCore;

namespace KutuphaneOtomasyon.Data
{
    public class LibraryContext : DbContext
    {
        public LibraryContext(DbContextOptions<LibraryContext> options) : base(options)
        {
        }

        public DbSet<Book> Books { get; set; }
        public DbSet<Member> Members { get; set; }
        public DbSet<Borrow> Borrows { get; set; }

    }
    
}
