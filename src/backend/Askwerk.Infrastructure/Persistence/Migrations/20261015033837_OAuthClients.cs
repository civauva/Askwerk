using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OAuthClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "oauth_client",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    client_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    secret_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oauth_client", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "access_token",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_token", x => x.id);
                    table.ForeignKey(
                        name: "FK_access_token_oauth_client_client_id",
                        column: x => x.client_id,
                        principalTable: "oauth_client",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_access_token_client_id",
                table: "access_token",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "IX_access_token_token_hash",
                table: "access_token",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_oauth_client_client_id",
                table: "oauth_client",
                column: "client_id",
                unique: true);

            // Row-level security per organisation. The token endpoint finds a client by its public id, and the Public
            // API finds a token by its hash, before the organisation is known; both definer functions reveal that row only,
            // and a token of a revoked or expired client is not found.
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE ON oauth_client TO askwerk_app;
                GRANT SELECT, INSERT, DELETE ON access_token TO askwerk_app;
                ALTER TABLE oauth_client ENABLE ROW LEVEL SECURITY;
                CREATE POLICY oauth_client_tenant ON oauth_client USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                ALTER TABLE access_token ENABLE ROW LEVEL SECURITY;
                CREATE POLICY access_token_tenant ON access_token USING (tenant_id::text = current_setting('askwerk.tenant_id', true));

                CREATE FUNCTION oauth_client_lookup(candidate text) RETURNS SETOF oauth_client
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT * FROM oauth_client WHERE client_id = candidate $$;
                REVOKE ALL ON FUNCTION oauth_client_lookup(text) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION oauth_client_lookup(text) TO askwerk_app;

                CREATE FUNCTION access_token_lookup(hash text) RETURNS SETOF access_token
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT t.* FROM access_token t JOIN oauth_client c ON c.id = t.client_id
                        WHERE t.token_hash = hash AND c.revoked_at IS NULL AND (c.expires_at IS NULL OR c.expires_at > now()) $$;
                REVOKE ALL ON FUNCTION access_token_lookup(text) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION access_token_lookup(text) TO askwerk_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS access_token_lookup(text); DROP FUNCTION IF EXISTS oauth_client_lookup(text);");

            migrationBuilder.DropTable(
                name: "access_token");

            migrationBuilder.DropTable(
                name: "oauth_client");
        }
    }
}
