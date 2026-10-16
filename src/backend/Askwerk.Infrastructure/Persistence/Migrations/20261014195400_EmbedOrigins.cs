using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmbedOrigins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "embed_origins",
                table: "tenant",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            // Survey pages learn which websites may frame them before anyone signs in: by the survey's code or id,
            // published or closed surveys only, nothing else revealed.
            migrationBuilder.Sql("""
                CREATE FUNCTION run_embed_origins(candidate text) RETURNS text[]
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT t.embed_origins FROM survey s JOIN tenant t ON t.id = s.tenant_id
                        WHERE (s.code = candidate OR s.id::text = candidate) AND s.deleted_at IS NULL AND s.status IN ('published', 'closed') $$;
                REVOKE ALL ON FUNCTION run_embed_origins(text) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION run_embed_origins(text) TO askwerk_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS run_embed_origins(text);");

            migrationBuilder.DropColumn(
                name: "embed_origins",
                table: "tenant");
        }
    }
}
