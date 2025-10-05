using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WIB.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPredictionsToReceiptLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PredictedCategoryId",
                table: "ReceiptLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PredictedTypeId",
                table: "ReceiptLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PredictionConfidence",
                table: "ReceiptLines",
                type: "numeric(3,2)",
                precision: 3,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PredictedCategoryId",
                table: "ReceiptLines");

            migrationBuilder.DropColumn(
                name: "PredictedTypeId",
                table: "ReceiptLines");

            migrationBuilder.DropColumn(
                name: "PredictionConfidence",
                table: "ReceiptLines");
        }
    }
}
