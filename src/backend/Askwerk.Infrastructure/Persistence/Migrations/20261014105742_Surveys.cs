using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Surveys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "survey",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    published_version = table.Column<int>(type: "integer", nullable: true),
                    languages = table.Column<string[]>(type: "text[]", nullable: false),
                    default_language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    anonymity_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    settings = table.Column<string>(type: "jsonb", nullable: true, defaultValueSql: "'{}'::jsonb"),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_survey", x => x.id);
                    table.ForeignKey(
                        name: "FK_survey_app_user_created_by",
                        column: x => x.created_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_survey_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_version",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    definition = table.Column<string>(type: "jsonb", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_survey_version", x => x.id);
                    table.ForeignKey(
                        name: "FK_survey_version_app_user_created_by",
                        column: x => x.created_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_survey_version_survey_survey_id",
                        column: x => x.survey_id,
                        principalTable: "survey",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "variable_mapping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    to_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    since_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_variable_mapping", x => x.id);
                    table.ForeignKey(
                        name: "FK_variable_mapping_survey_survey_id",
                        column: x => x.survey_id,
                        principalTable: "survey",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_page",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_survey_page", x => x.id);
                    table.ForeignKey(
                        name: "FK_survey_page_survey_version_survey_version_id",
                        column: x => x.survey_version_id,
                        principalTable: "survey_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_question",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    variable_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    text = table.Column<string>(type: "jsonb", nullable: false),
                    required = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_survey_question", x => x.id);
                    table.ForeignKey(
                        name: "FK_survey_question_survey_page_page_id",
                        column: x => x.page_id,
                        principalTable: "survey_page",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_survey_question_survey_version_survey_version_id",
                        column: x => x.survey_version_id,
                        principalTable: "survey_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_option",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    label = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_survey_option", x => x.id);
                    table.ForeignKey(
                        name: "FK_survey_option_survey_question_question_id",
                        column: x => x.question_id,
                        principalTable: "survey_question",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_survey_created_by",
                table: "survey",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_survey_tenant_id_updated_at",
                table: "survey",
                columns: new[] { "tenant_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_survey_option_question_id_kind_code",
                table: "survey_option",
                columns: new[] { "question_id", "kind", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_survey_page_survey_version_id",
                table: "survey_page",
                column: "survey_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_survey_question_page_id",
                table: "survey_question",
                column: "page_id");

            migrationBuilder.CreateIndex(
                name: "IX_survey_question_survey_version_id_variable_name",
                table: "survey_question",
                columns: new[] { "survey_version_id", "variable_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_survey_version_created_by",
                table: "survey_version",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_survey_version_one_draft",
                table: "survey_version",
                column: "survey_id",
                unique: true,
                filter: "state = 'draft'");

            migrationBuilder.CreateIndex(
                name: "IX_survey_version_survey_id_number",
                table: "survey_version",
                columns: new[] { "survey_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_variable_mapping_survey_id",
                table: "variable_mapping",
                column: "survey_id");

            // Row-level security per organisation; published versions never change (data-model.md).
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE ON survey, survey_version TO askwerk_app;
                GRANT SELECT, INSERT ON survey_page, survey_question, survey_option, variable_mapping TO askwerk_app;

                ALTER TABLE survey ENABLE ROW LEVEL SECURITY;
                CREATE POLICY survey_tenant ON survey
                  USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                ALTER TABLE survey_version ENABLE ROW LEVEL SECURITY;
                CREATE POLICY survey_version_tenant ON survey_version
                  USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                ALTER TABLE survey_page ENABLE ROW LEVEL SECURITY;
                CREATE POLICY survey_page_tenant ON survey_page
                  USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                ALTER TABLE survey_question ENABLE ROW LEVEL SECURITY;
                CREATE POLICY survey_question_tenant ON survey_question
                  USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                ALTER TABLE survey_option ENABLE ROW LEVEL SECURITY;
                CREATE POLICY survey_option_tenant ON survey_option
                  USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                ALTER TABLE variable_mapping ENABLE ROW LEVEL SECURITY;
                CREATE POLICY variable_mapping_tenant ON variable_mapping
                  USING (tenant_id::text = current_setting('askwerk.tenant_id', true));

                CREATE FUNCTION survey_version_frozen() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF OLD.state = 'published' THEN
                    RAISE EXCEPTION 'survey version % is published and cannot change', OLD.id USING ERRCODE = 'restrict_violation';
                  END IF;
                  RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
                END
                $$;
                CREATE TRIGGER survey_version_frozen BEFORE UPDATE OR DELETE ON survey_version
                  FOR EACH ROW EXECUTE FUNCTION survey_version_frozen();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS survey_version_frozen ON survey_version;
                DROP FUNCTION IF EXISTS survey_version_frozen();
                """);

            migrationBuilder.DropTable(
                name: "survey_option");

            migrationBuilder.DropTable(
                name: "variable_mapping");

            migrationBuilder.DropTable(
                name: "survey_question");

            migrationBuilder.DropTable(
                name: "survey_page");

            migrationBuilder.DropTable(
                name: "survey_version");

            migrationBuilder.DropTable(
                name: "survey");
        }
    }
}
