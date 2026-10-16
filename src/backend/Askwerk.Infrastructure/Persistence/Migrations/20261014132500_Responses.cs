using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Responses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SEQUENCE response_change_seq;");

            migrationBuilder.CreateTable(
                name: "response",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    page_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resume_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    change_seq = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('response_change_seq')"),
                    collector_id = table.Column<Guid>(type: "uuid", nullable: true),
                    participant_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_response", x => x.id);
                    table.ForeignKey(
                        name: "FK_response_survey_survey_id",
                        column: x => x.survey_id,
                        principalTable: "survey",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_response_survey_version_survey_version_id",
                        column: x => x.survey_version_id,
                        principalTable: "survey_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "answer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    response_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number_value = table.Column<decimal>(type: "numeric", nullable: true),
                    text_value = table.Column<string>(type: "text", nullable: true),
                    date_value = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    option_codes = table.Column<string[]>(type: "text[]", nullable: true),
                    detail = table.Column<string>(type: "jsonb", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_answer", x => x.id);
                    table.ForeignKey(
                        name: "FK_answer_response_response_id",
                        column: x => x.response_id,
                        principalTable: "response",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_answer_survey_question_question_id",
                        column: x => x.question_id,
                        principalTable: "survey_question",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_answer_question_id",
                table: "answer",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "IX_answer_response_id_question_id",
                table: "answer",
                columns: new[] { "response_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_response_resume_token_hash",
                table: "response",
                column: "resume_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_response_survey_id_change_seq",
                table: "response",
                columns: new[] { "survey_id", "change_seq" });

            migrationBuilder.CreateIndex(
                name: "IX_response_survey_version_id",
                table: "response",
                column: "survey_version_id");

            // Row-level security per organisation. Respondents are anonymous: the runner learns a survey's organisation
            // from run_survey_tenant, which answers only for published or closed surveys and reveals nothing else.
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE ON response TO askwerk_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON answer TO askwerk_app;
                GRANT USAGE ON SEQUENCE response_change_seq TO askwerk_app;

                ALTER TABLE response ENABLE ROW LEVEL SECURITY;
                CREATE POLICY response_tenant ON response
                  USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                ALTER TABLE answer ENABLE ROW LEVEL SECURITY;
                CREATE POLICY answer_tenant ON answer
                  USING (tenant_id::text = current_setting('askwerk.tenant_id', true));

                CREATE FUNCTION response_changed() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  NEW.change_seq := nextval('response_change_seq');
                  RETURN NEW;
                END
                $$;
                CREATE TRIGGER response_changed BEFORE UPDATE ON response
                  FOR EACH ROW EXECUTE FUNCTION response_changed();

                CREATE FUNCTION run_survey_tenant(candidate uuid) RETURNS uuid
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT tenant_id FROM survey WHERE id = candidate AND deleted_at IS NULL AND status IN ('published', 'closed') $$;
                REVOKE ALL ON FUNCTION run_survey_tenant(uuid) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION run_survey_tenant(uuid) TO askwerk_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS run_survey_tenant(uuid);
                DROP TRIGGER IF EXISTS response_changed ON response;
                DROP FUNCTION IF EXISTS response_changed();
                """);

            migrationBuilder.DropTable(
                name: "answer");

            migrationBuilder.DropTable(
                name: "response");
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS response_change_seq;");
        }
    }
}
