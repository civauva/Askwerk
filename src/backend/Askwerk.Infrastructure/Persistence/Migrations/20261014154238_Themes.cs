using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Themes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "theme_id",
                table: "survey",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "asset",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    content_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    original_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asset", x => x.id);
                    table.ForeignKey(
                        name: "FK_asset_app_user_created_by",
                        column: x => x.created_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_asset_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theme",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tokens = table.Column<string>(type: "jsonb", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_theme", x => x.id);
                    table.ForeignKey(
                        name: "FK_theme_app_user_created_by",
                        column: x => x.created_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_theme_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_survey_theme_id",
                table: "survey",
                column: "theme_id");

            migrationBuilder.CreateIndex(
                name: "IX_asset_created_by",
                table: "asset",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_asset_tenant_id",
                table: "asset",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_theme_created_by",
                table: "theme",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_theme_tenant_id_default",
                table: "theme",
                column: "tenant_id",
                unique: true,
                filter: "is_default");

            migrationBuilder.AddForeignKey(
                name: "FK_survey_theme_theme_id",
                table: "survey",
                column: "theme_id",
                principalTable: "theme",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // Row-level security per organisation (data-model.md). Themes can be deleted; uploaded files stay.
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE, DELETE ON theme TO askwerk_app;
                GRANT SELECT, INSERT ON asset TO askwerk_app;

                ALTER TABLE theme ENABLE ROW LEVEL SECURITY;
                CREATE POLICY theme_tenant ON theme
                  USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                ALTER TABLE asset ENABLE ROW LEVEL SECURITY;
                CREATE POLICY asset_tenant ON asset
                  USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_survey_theme_theme_id",
                table: "survey");

            migrationBuilder.DropTable(
                name: "asset");

            migrationBuilder.DropTable(
                name: "theme");

            migrationBuilder.DropIndex(
                name: "IX_survey_theme_id",
                table: "survey");

            migrationBuilder.DropColumn(
                name: "theme_id",
                table: "survey");
        }
    }
}
