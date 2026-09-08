using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeatureFusion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProductOriginalVersionConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "products");

            migrationBuilder.AddColumn<long>(
                name: "OriginalVersion",
                table: "products",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalVersion",
                table: "products");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "products",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }
    }
}
