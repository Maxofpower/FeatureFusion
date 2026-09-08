using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FeatureFusion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CatalogStorefront : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_products_sku",
                table: "products");

            // Leftover CatalogInit products have NULL BrandId/CategoryId. EF's AlterColumn
            // backfill uses 0, which is not a real brand/category (FK 23503).
            migrationBuilder.Sql(
                """
                INSERT INTO categories ("Name")
                SELECT 'Uncategorized'
                WHERE EXISTS (SELECT 1 FROM products WHERE "CategoryId" IS NULL)
                  AND NOT EXISTS (SELECT 1 FROM categories);

                INSERT INTO brands ("Name")
                SELECT 'Unknown'
                WHERE EXISTS (SELECT 1 FROM products WHERE "BrandId" IS NULL)
                  AND NOT EXISTS (SELECT 1 FROM brands);

                UPDATE products
                SET "CategoryId" = (SELECT MIN("Id") FROM categories)
                WHERE "CategoryId" IS NULL;

                UPDATE products
                SET "BrandId" = (SELECT MIN("Id") FROM brands)
                WHERE "BrandId" IS NULL;

                UPDATE products
                SET "Sku" = 'SKU-LEGACY-' || "Id"::text
                WHERE "Sku" IS NULL OR BTRIM("Sku") = '';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "products",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "CategoryId",
                table: "products",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "BrandId",
                table: "products",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShortDescription",
                table: "products",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "products",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "categories",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "brands",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "brands",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE products
                SET "Slug" = 'product-' || "Id"::text
                WHERE "Slug" IS NULL OR BTRIM("Slug") = '';

                UPDATE categories
                SET "Slug" = 'category-' || "Id"::text
                WHERE "Slug" IS NULL OR BTRIM("Slug") = '';

                UPDATE brands
                SET "Slug" = 'brand-' || "Id"::text
                WHERE "Slug" IS NULL OR BTRIM("Slug") = '';
                """);

            migrationBuilder.CreateTable(
                name: "product_images",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AltText = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_images_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_specifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_specifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_specifications_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_products_sku",
                table: "products",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_slug",
                table: "products",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_categories_Slug",
                table: "categories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brands_Slug",
                table: "brands",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_images_product_id",
                table: "product_images",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_product_specifications_product_id",
                table: "product_specifications",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_images");

            migrationBuilder.DropTable(
                name: "product_specifications");

            migrationBuilder.DropIndex(
                name: "IX_products_sku",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_slug",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_categories_Slug",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "IX_brands_Slug",
                table: "brands");

            migrationBuilder.DropColumn(
                name: "ShortDescription",
                table: "products");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "products");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "brands");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "brands");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "products",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<int>(
                name: "CategoryId",
                table: "products",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "BrandId",
                table: "products",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.CreateIndex(
                name: "IX_products_sku",
                table: "products",
                column: "Sku",
                unique: true,
                filter: "\"Sku\" IS NOT NULL");
        }
    }
}
