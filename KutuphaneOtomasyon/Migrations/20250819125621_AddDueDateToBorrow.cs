using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KutuphaneOtomasyon.Migrations
{
    /// <inheritdoc />
    public partial class AddDueDateToBorrow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
       name: "DueDate",
       table: "Borrows",            // tablo adın farklıysa düzelt
       type: "datetime2",
       nullable: true);

            // Eski kayıtlar için varsayılan politika: 15 gün
            // (SQL Server varsayıldı; sağlayıcın farklıysa uyarlayalım)
            migrationBuilder.Sql(@"
        UPDATE b
        SET DueDate = DATEADD(day, 15, CAST(BorrowDate as date))
        FROM Borrows b
        WHERE b.DueDate IS NULL
    ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
       name: "DueDate",
       table: "Borrows");
        }
    }
}
