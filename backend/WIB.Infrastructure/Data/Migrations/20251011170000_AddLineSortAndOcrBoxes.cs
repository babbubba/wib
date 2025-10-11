using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WIB.Infrastructure.Data;

namespace WIB.Infrastructure.Data.Migrations
{
    [DbContext(typeof(WibDbContext))]
    [Migration("20251011170000_AddLineSortAndOcrBoxes")]
    public partial class AddLineSortAndOcrBoxes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortIndex",
                table: "ReceiptLines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(name: "OcrX", table: "ReceiptLines", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "OcrY", table: "ReceiptLines", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "OcrW", table: "ReceiptLines", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "OcrH", table: "ReceiptLines", type: "integer", nullable: true);

            migrationBuilder.AddColumn<int>(name: "OcrStoreX", table: "Receipts", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "OcrStoreY", table: "Receipts", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "OcrStoreW", table: "Receipts", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "OcrStoreH", table: "Receipts", type: "integer", nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SortIndex", table: "ReceiptLines");
            migrationBuilder.DropColumn(name: "OcrX", table: "ReceiptLines");
            migrationBuilder.DropColumn(name: "OcrY", table: "ReceiptLines");
            migrationBuilder.DropColumn(name: "OcrW", table: "ReceiptLines");
            migrationBuilder.DropColumn(name: "OcrH", table: "ReceiptLines");
            migrationBuilder.DropColumn(name: "OcrStoreX", table: "Receipts");
            migrationBuilder.DropColumn(name: "OcrStoreY", table: "Receipts");
            migrationBuilder.DropColumn(name: "OcrStoreW", table: "Receipts");
            migrationBuilder.DropColumn(name: "OcrStoreH", table: "Receipts");
        }
    }
}

