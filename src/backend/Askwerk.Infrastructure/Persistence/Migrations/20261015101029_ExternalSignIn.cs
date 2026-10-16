using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExternalSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "external_identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_identity", x => x.id);
                    table.ForeignKey(
                        name: "FK_external_identity_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_external_identity_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "handover_token_use",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jti = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_handover_token_use", x => new { x.tenant_id, x.jti });
                });

            migrationBuilder.CreateTable(
                name: "tenant_sign_in",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handover_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    handover_issuer = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    handover_keys = table.Column<string[]>(type: "text[]", nullable: false),
                    handover_jwks_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    provisioning_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    system_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    role_rules = table.Column<string>(type: "jsonb", nullable: false),
                    default_role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_sign_in", x => x.tenant_id);
                    table.ForeignKey(
                        name: "FK_tenant_sign_in_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_external_identity_tenant_id_external_id",
                table: "external_identity",
                columns: new[] { "tenant_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_identity_tenant_id_user_id",
                table: "external_identity",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_identity_user_id",
                table: "external_identity",
                column: "user_id");

            // Row-level security per organisation. A hand-over names its organisation in aud; the settings and keys of
            // that organisation are then read inside it, so no definer function is needed (AWK-12, W22).
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE ON tenant_sign_in TO askwerk_app;
                GRANT SELECT, INSERT, UPDATE ON external_identity TO askwerk_app;
                GRANT SELECT, INSERT, DELETE ON handover_token_use TO askwerk_app;
                ALTER TABLE tenant_sign_in ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_sign_in_tenant ON tenant_sign_in USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                ALTER TABLE external_identity ENABLE ROW LEVEL SECURITY;
                CREATE POLICY external_identity_tenant ON external_identity USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                ALTER TABLE handover_token_use ENABLE ROW LEVEL SECURITY;
                CREATE POLICY handover_token_use_tenant ON handover_token_use USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_identity");

            migrationBuilder.DropTable(
                name: "handover_token_use");

            migrationBuilder.DropTable(
                name: "tenant_sign_in");
        }
    }
}
