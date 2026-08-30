using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeatureFusion.Infrastructure.Migrations
{
    [DbContext(typeof(CatalogDbContext))]
    [Migration("20260829151500_ProductsKeysetIndexes")]
    public partial class ProductsKeysetIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_products_created_at_id",
                table: "products",
                columns: new[] { "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_products_name_id",
                table: "products",
                columns: new[] { "Name", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_products_price_id",
                table: "products",
                columns: new[] { "Price", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_products_created_at_id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_name_id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_price_id",
                table: "products");
        }
    }
}
