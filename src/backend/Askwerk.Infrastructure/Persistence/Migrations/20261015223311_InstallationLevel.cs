using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstallationLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "tenant",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "tenant",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "active");

            migrationBuilder.AddColumn<bool>(
                name: "is_installation_admin",
                table: "app_user",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Whoever set up the installation administers it (AWK-38).
            migrationBuilder.Sql("UPDATE app_user SET is_installation_admin = true WHERE id IN (SELECT owner_user_id FROM instance_settings);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "tenant");

            migrationBuilder.DropColumn(
                name: "status",
                table: "tenant");

            migrationBuilder.DropColumn(
                name: "is_installation_admin",
                table: "app_user");
        }
    }
}
