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
        public DbSet<AiRecommendationHistory> AiRecommendationHistories { get; set; } = default!;
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
            modelBuilder.Entity<AiRecommendationHistory>(e =>
            {
                e.ToTable("AiRecommendationHistories");
                e.Property(x => x.Title).HasMaxLength(256).IsRequired();
                e.Property(x => x.Author).HasMaxLength(128);
                e.Property(x => x.Source).HasMaxLength(32);
                e.Property(x => x.Score).HasColumnType("decimal(9,4)");
                e.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
                e.HasIndex(x => x.CreatedAt);
                e.HasIndex(x => x.MemberId);
            });
            modelBuilder.Entity<Book>()
        .HasIndex(b => b.ISBN)
        .HasDatabaseName("IX_Books_ISBN");

            modelBuilder.Entity<Book>()
                .HasIndex(b => b.CleanISBN)
                .HasDatabaseName("IX_Books_CleanISBN");

            modelBuilder.Entity<Book>()
                .HasIndex(b => new { b.Title, b.Author })
                .HasDatabaseName("IX_Books_Title_Author");
        }
    }
    
}
