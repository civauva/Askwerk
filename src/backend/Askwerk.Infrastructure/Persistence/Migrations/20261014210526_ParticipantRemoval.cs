using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ParticipantRemoval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // W13 C: editors remove chosen participants; people with a response stay (the foreign key restricts).
            migrationBuilder.Sql("GRANT DELETE ON participant TO askwerk_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE DELETE ON participant FROM askwerk_app;");
        }
    }
}
