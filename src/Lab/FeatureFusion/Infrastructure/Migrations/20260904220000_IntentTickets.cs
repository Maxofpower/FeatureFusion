using System;
using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeatureFusion.Infrastructure.Migrations
{
    [DbContext(typeof(CatalogDbContext))]
    [Migration("20260904220000_IntentTickets")]
    public partial class IntentTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "intent_tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CapabilityId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IntentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IntentPayload = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReleasedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExecutionOrderId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_intent_tickets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_intent_tickets_capability_request_key",
                table: "intent_tickets",
                columns: new[] { "CapabilityId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_intent_tickets_status",
                table: "intent_tickets",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "intent_tickets");
        }
    }
}
