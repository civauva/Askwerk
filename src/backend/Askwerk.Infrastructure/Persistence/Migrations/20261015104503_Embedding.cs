using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Embedding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "designer_embed_origins",
                table: "tenant",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string[]>(
                name: "embed_origins",
                table: "survey",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.CreateTable(
                name: "embed_session",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    view = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_embed_session", x => x.id);
                    table.ForeignKey(
                        name: "FK_embed_session_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_embed_session_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_embed_session_tenant_id",
                table: "embed_session",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_embed_session_token_hash",
                table: "embed_session",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_embed_session_user_id",
                table: "embed_session",
                column: "user_id");

            // Embedded sessions are per organisation; the bearer token finds its row before the organisation is known,
            // through a definer function that reveals that row only while it works. A survey's own list of websites,
            // when set, replaces the organisation's for its frames; the designer's websites are a list of their own
            // (AWK-13, W21).
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE, DELETE ON embed_session TO askwerk_app;
                ALTER TABLE embed_session ENABLE ROW LEVEL SECURITY;
                CREATE POLICY embed_session_tenant ON embed_session USING (tenant_id::text = current_setting('askwerk.tenant_id', true));

                CREATE FUNCTION embed_session_lookup(hash text) RETURNS SETOF embed_session
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT * FROM embed_session WHERE token_hash = hash AND expires_at > now() $$;
                REVOKE ALL ON FUNCTION embed_session_lookup(text) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION embed_session_lookup(text) TO askwerk_app;

                CREATE FUNCTION designer_embed_origins(organisation uuid) RETURNS text[]
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT designer_embed_origins FROM tenant WHERE id = organisation $$;
                REVOKE ALL ON FUNCTION designer_embed_origins(uuid) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION designer_embed_origins(uuid) TO askwerk_app;

                CREATE OR REPLACE FUNCTION run_embed_origins(candidate text) RETURNS text[]
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT CASE WHEN cardinality(s.embed_origins) > 0 THEN s.embed_origins ELSE t.embed_origins END
                        FROM survey s JOIN tenant t ON t.id = s.tenant_id
                        WHERE (s.code = candidate OR s.id::text = candidate) AND s.deleted_at IS NULL AND s.status IN ('published', 'closed') $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS embed_session_lookup(text);
                DROP FUNCTION IF EXISTS designer_embed_origins(uuid);
                CREATE OR REPLACE FUNCTION run_embed_origins(candidate text) RETURNS text[]
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT t.embed_origins FROM survey s JOIN tenant t ON t.id = s.tenant_id
                        WHERE (s.code = candidate OR s.id::text = candidate) AND s.deleted_at IS NULL AND s.status IN ('published', 'closed') $$;
                """);
            migrationBuilder.DropTable(
                name: "embed_session");

            migrationBuilder.DropColumn(
                name: "designer_embed_origins",
                table: "tenant");

            migrationBuilder.DropColumn(
                name: "embed_origins",
                table: "survey");
        }
    }
}
