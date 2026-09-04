using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeatureFusion.Infrastructure.Migrations
{
    [DbContext(typeof(CatalogDbContext))]
    [Migration("20260904180000_ProductsNameThenPriceIndexes")]
    public partial class ProductsNameThenPriceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_products_name_price_id",
                table: "products",
                columns: new[] { "Name", "Price", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_products_name_price_id_desc",
                table: "products",
                columns: new[] { "Name", "Price", "Id" },
                descending: new[] { true, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_products_name_price_id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_name_price_id_desc",
                table: "products");
        }
    }
}
