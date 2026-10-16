using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Webhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    events = table.Column<string[]>(type: "text[]", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    secret_protected = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    paused_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failing_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_delivery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    webhook_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    response_status = table.Column<int>(type: "integer", nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_delivery", x => x.id);
                    table.ForeignKey(
                        name: "FK_webhook_delivery_webhook_webhook_id",
                        column: x => x.webhook_id,
                        principalTable: "webhook",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_tenant_id",
                table: "webhook",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_delivery_webhook_id_id",
                table: "webhook_delivery",
                columns: new[] { "webhook_id", "id" });

            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE, DELETE ON webhook, webhook_delivery TO askwerk_app;
                ALTER TABLE webhook ENABLE ROW LEVEL SECURITY;
                CREATE POLICY webhook_tenant ON webhook USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                ALTER TABLE webhook_delivery ENABLE ROW LEVEL SECURITY;
                CREATE POLICY webhook_delivery_tenant ON webhook_delivery USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_delivery");

            migrationBuilder.DropTable(
                name: "webhook");
        }
    }
}
