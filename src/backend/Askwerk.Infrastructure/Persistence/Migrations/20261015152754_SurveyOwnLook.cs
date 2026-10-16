using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SurveyOwnLook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "look",
                table: "survey",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "look_base",
                table: "survey",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "look",
                table: "survey");

            migrationBuilder.DropColumn(
                name: "look_base",
                table: "survey");
        }
    }
}
