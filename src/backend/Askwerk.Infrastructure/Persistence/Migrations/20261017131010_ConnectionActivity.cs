using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConnectionActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "duration_ms",
                table: "webhook_delivery",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "integration_activity",
                columns: table => new
                {
                    credential_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    calls = table.Column<int>(type: "integer", nullable: false),
                    refused = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_activity", x => new { x.credential_id, x.day });
                });

            migrationBuilder.CreateIndex(
                name: "IX_integration_activity_tenant_id",
                table: "integration_activity",
                column: "tenant_id");

            // Calls per key or client and day (W36): written by a function that may run before the request has an
            // organisation, so it sets the row's organisation itself; read under row-level security.
            migrationBuilder.Sql("""
                GRANT SELECT ON integration_activity TO askwerk_app;
                ALTER TABLE integration_activity ENABLE ROW LEVEL SECURITY;
                CREATE POLICY integration_activity_tenant ON integration_activity USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                CREATE FUNCTION note_integration_activity(tenant uuid, credential uuid, calls integer, refused integer) RETURNS void
                  LANGUAGE sql VOLATILE SECURITY DEFINER SET search_path = public
                  AS $$ INSERT INTO integration_activity (tenant_id, credential_id, day, calls, refused)
                        VALUES (tenant, credential, (now() AT TIME ZONE 'utc')::date, calls, refused)
                        ON CONFLICT (credential_id, day) DO UPDATE SET calls = integration_activity.calls + EXCLUDED.calls, refused = integration_activity.refused + EXCLUDED.refused $$;
                REVOKE ALL ON FUNCTION note_integration_activity(uuid, uuid, integer, integer) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION note_integration_activity(uuid, uuid, integer, integer) TO askwerk_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS note_integration_activity(uuid, uuid, integer, integer);");

            migrationBuilder.DropTable(
                name: "integration_activity");

            migrationBuilder.DropColumn(
                name: "duration_ms",
                table: "webhook_delivery");
        }
    }
}
