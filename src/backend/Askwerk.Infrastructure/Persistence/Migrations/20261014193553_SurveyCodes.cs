using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SurveyCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "code",
                table: "survey",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            // Existing surveys get a code as well: eight characters of a hash, with 0 and 1 mapped into the alphabet.
            // The runner finds a published survey by its code through run_survey_id, which reveals nothing else.
            migrationBuilder.Sql("""
                UPDATE survey SET code = substr(translate(md5(random()::text || id::text), '01', 'gh'), 1, 8);

                CREATE FUNCTION run_survey_id(candidate text) RETURNS uuid
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT id FROM survey WHERE code = candidate AND deleted_at IS NULL AND status IN ('published', 'closed') $$;
                REVOKE ALL ON FUNCTION run_survey_id(text) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION run_survey_id(text) TO askwerk_app;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_survey_code",
                table: "survey",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS run_survey_id(text);");

            migrationBuilder.DropIndex(
                name: "IX_survey_code",
                table: "survey");

            migrationBuilder.DropColumn(
                name: "code",
                table: "survey");
        }
    }
}
