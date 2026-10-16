using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstanceSettingsAndSlugCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instance_settings",
                columns: table => new
                {
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    base_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    default_language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instance_settings", x => x.instance_id);
                    table.ForeignKey(
                        name: "FK_instance_settings_app_user_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_instance_settings_instance_instance_id",
                        column: x => x.instance_id,
                        principalTable: "instance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_instance_settings_owner_user_id",
                table: "instance_settings",
                column: "owner_user_id");

            // tenant_slug_taken answers only whether a slug is in use anywhere. It runs with the owner's rights
            // (SECURITY DEFINER), because row-level security hides other tenants from the app role.
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE ON instance_settings TO askwerk_app;

                CREATE FUNCTION tenant_slug_taken(candidate text) RETURNS boolean
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT EXISTS (SELECT 1 FROM tenant WHERE slug = candidate) $$;
                REVOKE ALL ON FUNCTION tenant_slug_taken(text) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION tenant_slug_taken(text) TO askwerk_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tenant_slug_taken(text);");

            migrationBuilder.DropTable(
                name: "instance_settings");
        }
    }
}
