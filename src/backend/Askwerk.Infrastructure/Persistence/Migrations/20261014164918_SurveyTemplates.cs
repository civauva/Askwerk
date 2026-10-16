using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SurveyTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "survey_template",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    definition = table.Column<string>(type: "jsonb", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_survey_template", x => x.id);
                    table.ForeignKey(
                        name: "FK_survey_template_app_user_created_by",
                        column: x => x.created_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_survey_template_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_survey_template_created_by",
                table: "survey_template",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_survey_template_tenant_id",
                table: "survey_template",
                column: "tenant_id");

            // Row-level security per organisation (data-model.md).
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, DELETE ON survey_template TO askwerk_app;
                ALTER TABLE survey_template ENABLE ROW LEVEL SECURITY;
                CREATE POLICY survey_template_tenant ON survey_template
                  USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "survey_template");
        }
    }
}
