using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WIB.Infrastructure.Data;

#nullable disable

namespace WIB.Infrastructure.Data.Migrations
{
    [DbContext(typeof(WibDbContext))]
    [Migration("20251011150000_AddPricePerKg")]
    public partial class AddPricePerKg : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PricePerKg",
                table: "PriceHistories",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightKg",
                table: "ReceiptLines",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PricePerKg",
                table: "ReceiptLines",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PricePerKg",
                table: "PriceHistories");

            migrationBuilder.DropColumn(
                name: "WeightKg",
                table: "ReceiptLines");

            migrationBuilder.DropColumn(
                name: "PricePerKg",
                table: "ReceiptLines");
        }
    }
}
