using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KutuphaneOtomasyon.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberJoinedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) nullable ekle (eski satırlar için)
            migrationBuilder.AddColumn<DateTime>(
                name: "JoinedAt",
                table: "Members",              // tablo adın farklıysa düzelt
                type: "datetime2",
                nullable: true);

            // 2) eski kayıtları doldur (en iyisi: ilk ödünç tarihi; yoksa şimdiki UTC)
            migrationBuilder.Sql(@"
        UPDATE M
        SET JoinedAt = COALESCE(B.FirstBorrowDate, GETUTCDATE())
        FROM Members M
        OUTER APPLY (
            SELECT MIN(BorrowDate) AS FirstBorrowDate
            FROM Borrows
            WHERE Borrows.MemberId = M.MemberId
        ) B
        WHERE M.JoinedAt IS NULL;
    ");

            // 3) NOT NULL'a çevir ve default constraint ata
            migrationBuilder.AlterColumn<DateTime>(
                name: "JoinedAt",
                table: "Members",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()",
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JoinedAt",
                table: "Members");
        }

    }
}
