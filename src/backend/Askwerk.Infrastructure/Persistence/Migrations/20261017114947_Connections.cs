using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Connections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "survey_ids",
                table: "webhook",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "paused_at",
                table: "oauth_client",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "paused_at",
                table: "api_key",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "connection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    goals = table.Column<string[]>(type: "text[]", nullable: false),
                    match_by = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: true),
                    oauth_client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    webhook_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paused_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paused_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connection", x => x.id);
                    table.ForeignKey(
                        name: "FK_connection_api_key_api_key_id",
                        column: x => x.api_key_id,
                        principalTable: "api_key",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_connection_oauth_client_oauth_client_id",
                        column: x => x.oauth_client_id,
                        principalTable: "oauth_client",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_connection_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_connection_webhook_webhook_id",
                        column: x => x.webhook_id,
                        principalTable: "webhook",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_connection_api_key_id",
                table: "connection",
                column: "api_key_id");

            migrationBuilder.CreateIndex(
                name: "IX_connection_oauth_client_id",
                table: "connection",
                column: "oauth_client_id");

            migrationBuilder.CreateIndex(
                name: "IX_connection_tenant_id",
                table: "connection",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_connection_webhook_id",
                table: "connection",
                column: "webhook_id");

            // Connections per organisation (W36) with row-level security; a paused OAuth client's tokens stop at once.
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE, DELETE ON connection TO askwerk_app;
                ALTER TABLE connection ENABLE ROW LEVEL SECURITY;
                CREATE POLICY connection_tenant ON connection USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                CREATE OR REPLACE FUNCTION access_token_lookup(hash text) RETURNS SETOF access_token
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT t.* FROM access_token t JOIN oauth_client c ON c.id = t.client_id
                        WHERE t.token_hash = hash AND c.revoked_at IS NULL AND c.paused_at IS NULL AND (c.expires_at IS NULL OR c.expires_at > now()) $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION access_token_lookup(hash text) RETURNS SETOF access_token
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT t.* FROM access_token t JOIN oauth_client c ON c.id = t.client_id
                        WHERE t.token_hash = hash AND c.revoked_at IS NULL AND (c.expires_at IS NULL OR c.expires_at > now()) $$;
                """);

            migrationBuilder.DropTable(
                name: "connection");

            migrationBuilder.DropColumn(
                name: "survey_ids",
                table: "webhook");

            migrationBuilder.DropColumn(
                name: "paused_at",
                table: "oauth_client");

            migrationBuilder.DropColumn(
                name: "paused_at",
                table: "api_key");
        }
    }
}
