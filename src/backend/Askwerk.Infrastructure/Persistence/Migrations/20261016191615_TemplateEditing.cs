using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TemplateEditing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "survey_id",
                table: "survey_template",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_survey_template_survey_id",
                table: "survey_template",
                column: "survey_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_survey_template_survey_survey_id",
                table: "survey_template",
                column: "survey_id",
                principalTable: "survey",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // The editing survey (W32) is set on a template's first edit, and its name follows the survey's title.
            migrationBuilder.Sql("GRANT UPDATE (survey_id, name) ON survey_template TO askwerk_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE UPDATE (survey_id, name) ON survey_template FROM askwerk_app;");

            migrationBuilder.DropForeignKey(
                name: "FK_survey_template_survey_survey_id",
                table: "survey_template");

            migrationBuilder.DropIndex(
                name: "IX_survey_template_survey_id",
                table: "survey_template");

            migrationBuilder.DropColumn(
                name: "survey_id",
                table: "survey_template");
        }
    }
}
