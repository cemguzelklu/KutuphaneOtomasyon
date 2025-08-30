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
        public DbSet<AiLog> AiLogs { get; set; } = null!;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<AiLog>(entity =>
            {
                entity.ToTable("AiLogs");
                entity.HasKey(x => x.AiLogId);

                // Küçük indeksler:
                entity.HasIndex(x => x.CreatedAtUtc);
                entity.HasIndex(x => x.MemberId);
                entity.HasIndex(x => x.Action);
                entity.HasIndex(x => x.Success);

                // (İsteğe bağlı) uzunluk doğrulamaları fluent olarak da tekrar edilebilir
                entity.Property(x => x.Action).HasMaxLength(32);
                entity.Property(x => x.Provider).HasMaxLength(32);
                entity.Property(x => x.Model).HasMaxLength(64);
                entity.Property(x => x.Endpoint).HasMaxLength(64);
                entity.Property(x => x.ErrorType).HasMaxLength(64);
                entity.Property(x => x.ErrorCode).HasMaxLength(64);
            });
        }
    }
    
}
