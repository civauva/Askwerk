using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "view",
                table: "app_user",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "simple");

            // New people start in Simple (W45, AWK-71); everyone who is here already keeps seeing every place.
            migrationBuilder.Sql("UPDATE app_user SET view = 'advanced';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "view",
                table: "app_user");
        }
    }
}
