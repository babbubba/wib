using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WIB.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Store_Normalized_Finalize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "NameNormalized",
                table: "Stores",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stores_NameNormalized",
                table: "Stores",
                column: "NameNormalized",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stores_NameNormalized",
                table: "Stores");

            migrationBuilder.AlterColumn<string>(
                name: "NameNormalized",
                table: "Stores",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);
        }
    }
}
