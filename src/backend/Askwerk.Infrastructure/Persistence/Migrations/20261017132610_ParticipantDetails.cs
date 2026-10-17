using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ParticipantDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bounce_reason",
                table: "participant",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reminder_log",
                table: "participant",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "invitation",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bounce_reason",
                table: "participant");

            migrationBuilder.DropColumn(
                name: "reminder_log",
                table: "participant");

            migrationBuilder.DropColumn(
                name: "source",
                table: "invitation");
        }
    }
}
