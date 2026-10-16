using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Domains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "domain",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hostname = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    verification_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    cname_found = table.Column<bool>(type: "boolean", nullable: false),
                    txt_found = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_domain", x => x.id);
                    table.ForeignKey(
                        name: "FK_domain_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_domain_hostname",
                table: "domain",
                column: "hostname",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_domain_one_primary",
                table: "domain",
                column: "tenant_id",
                unique: true,
                filter: "is_primary");

            // Row-level security per organisation. Two functions see across organisations and reveal nothing else:
            // whether Askwerk serves a hostname (Caddy's ask), and which domains still wait for DNS (the checks).
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE, DELETE ON domain TO askwerk_app;
                ALTER TABLE domain ENABLE ROW LEVEL SECURITY;
                CREATE POLICY domain_tenant ON domain
                  USING (tenant_id::text = current_setting('askwerk.tenant_id', true));

                CREATE FUNCTION domain_is_served(candidate text) RETURNS boolean
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT EXISTS (SELECT 1 FROM domain WHERE hostname = candidate AND status = 'verified') $$;
                REVOKE ALL ON FUNCTION domain_is_served(text) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION domain_is_served(text) TO askwerk_app;

                CREATE FUNCTION pending_domains() RETURNS TABLE (id uuid, tenant_id uuid)
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT id, tenant_id FROM domain WHERE status = 'pending' $$;
                REVOKE ALL ON FUNCTION pending_domains() FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION pending_domains() TO askwerk_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS domain_is_served(text); DROP FUNCTION IF EXISTS pending_domains();");

            migrationBuilder.DropTable(
                name: "domain");
        }
    }
}
