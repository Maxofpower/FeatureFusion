using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeatureFusion.Infrastructure.Migrations
{
    [DbContext(typeof(CatalogDbContext))]
    [Migration("20260829164800_ProductsKeysetDirectionIndexes")]
    public partial class ProductsKeysetDirectionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_products_created_at_id_desc",
                table: "products",
                columns: new[] { "CreatedAt", "Id" },
                descending: new[] { true, false });

            migrationBuilder.CreateIndex(
                name: "IX_products_name_id_desc",
                table: "products",
                columns: new[] { "Name", "Id" },
                descending: new[] { true, false });

            migrationBuilder.CreateIndex(
                name: "IX_products_price_id_desc",
                table: "products",
                columns: new[] { "Price", "Id" },
                descending: new[] { true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_products_created_at_id_desc",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_name_id_desc",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_price_id_desc",
                table: "products");
        }
    }
}
