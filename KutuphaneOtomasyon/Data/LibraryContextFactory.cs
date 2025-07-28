using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KutuphaneOtomasyon.Data
{
    public class LibraryContextFactory:IDesignTimeDbContextFactory<LibraryContext>
    {
        public LibraryContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<LibraryContext>();
            optionsBuilder.UseSqlServer("Server=localhost\\SQLEXPRESS;Database=KutuphaneOtomasyon;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True");
            return new LibraryContext(optionsBuilder.Options);
        }
    }
    
    
}
