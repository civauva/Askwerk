using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantsUsersMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_user",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    authenticator_key = table.Column<string>(type: "text", nullable: true),
                    recovery_codes = table.Column<string>(type: "text", nullable: true),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership", x => x.id);
                    table.ForeignKey(
                        name: "FK_membership_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_membership_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_user_normalized_email",
                table: "app_user",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_membership_tenant_id",
                table: "membership",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_membership_user_id_tenant_id_workspace_id",
                table: "membership",
                columns: new[] { "user_id", "tenant_id", "workspace_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_slug",
                table: "tenant",
                column: "slug",
                unique: true);

            // Row-level security (agents/data-model.md): the app role sees a tenant's rows only when the
            // request's tenant is set on the connection (askwerk.tenant_id), and a signed-in user without a
            // tenant sees only their own memberships and organisations (askwerk.user_id). Rows are written
            // only for the tenant in context. Both settings come from the authenticated caller, never from input.
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE, DELETE ON tenant, app_user, membership TO askwerk_app;

                ALTER TABLE membership ENABLE ROW LEVEL SECURITY;
                CREATE POLICY membership_access ON membership
                  USING (tenant_id::text = current_setting('askwerk.tenant_id', true)
                         OR user_id::text = current_setting('askwerk.user_id', true))
                  WITH CHECK (tenant_id::text = current_setting('askwerk.tenant_id', true));

                ALTER TABLE tenant ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_access ON tenant
                  USING (id::text = current_setting('askwerk.tenant_id', true)
                         OR EXISTS (SELECT 1 FROM membership m
                                    WHERE m.tenant_id = tenant.id
                                      AND m.user_id::text = current_setting('askwerk.user_id', true)))
                  WITH CHECK (id::text = current_setting('askwerk.tenant_id', true));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS tenant_access ON tenant;
                DROP POLICY IF EXISTS membership_access ON membership;
                """);

            migrationBuilder.DropTable(
                name: "membership");

            migrationBuilder.DropTable(
                name: "app_user");

            migrationBuilder.DropTable(
                name: "tenant");
        }
    }
}
