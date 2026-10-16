using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstanceEmailSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "smtp_host",
                table: "instance_settings",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "smtp_password",
                table: "instance_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "smtp_port",
                table: "instance_settings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "smtp_sender",
                table: "instance_settings",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "smtp_user_name",
                table: "instance_settings",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "smtp_host",
                table: "instance_settings");

            migrationBuilder.DropColumn(
                name: "smtp_password",
                table: "instance_settings");

            migrationBuilder.DropColumn(
                name: "smtp_port",
                table: "instance_settings");

            migrationBuilder.DropColumn(
                name: "smtp_sender",
                table: "instance_settings");

            migrationBuilder.DropColumn(
                name: "smtp_user_name",
                table: "instance_settings");
        }
    }
}
