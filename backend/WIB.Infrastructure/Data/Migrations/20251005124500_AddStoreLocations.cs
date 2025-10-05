using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WIB.Infrastructure.Data.Migrations
{
    public partial class AddStoreLocations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StoreLocationId",
                table: "Receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StoreLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    PostalCode = table.Column<string>(type: "text", nullable: true),
                    VatNumber = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreLocations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_StoreLocationId",
                table: "Receipts",
                column: "StoreLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreLocations_StoreId",
                table: "StoreLocations",
                column: "StoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_Receipts_StoreLocations_StoreLocationId",
                table: "Receipts",
                column: "StoreLocationId",
                principalTable: "StoreLocations",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Receipts_StoreLocations_StoreLocationId",
                table: "Receipts");

            migrationBuilder.DropTable(
                name: "StoreLocations");

            migrationBuilder.DropIndex(
                name: "IX_Receipts_StoreLocationId",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "StoreLocationId",
                table: "Receipts");
        }
    }
}

