using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApiKeysAndExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_key",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prefix = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    secret_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_key", x => x.id);
                    table.ForeignKey(
                        name: "FK_api_key_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "export",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by_api_key_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    done = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<int>(type: "integer", nullable: false),
                    file_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    size = table.Column<long>(type: "bigint", nullable: true),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export", x => x.id);
                    table.ForeignKey(
                        name: "FK_export_api_key_requested_by_api_key_id",
                        column: x => x.requested_by_api_key_id,
                        principalTable: "api_key",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_export_survey_survey_id",
                        column: x => x.survey_id,
                        principalTable: "survey",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_api_key_prefix",
                table: "api_key",
                column: "prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_api_key_tenant_id",
                table: "api_key",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_export_requested_by_api_key_id_idempotency_key",
                table: "export",
                columns: new[] { "requested_by_api_key_id", "idempotency_key" });

            migrationBuilder.CreateIndex(
                name: "IX_export_survey_id_created_at",
                table: "export",
                columns: new[] { "survey_id", "created_at" });

            // Row-level security per organisation. api_key_lookup sees across organisations to find one key by its
            // prefix before the organisation is known (the Public API's authentication), and reveals nothing else.
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE ON api_key TO askwerk_app;
                GRANT SELECT, INSERT, UPDATE ON export TO askwerk_app;
                ALTER TABLE api_key ENABLE ROW LEVEL SECURITY;
                CREATE POLICY api_key_tenant ON api_key USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                ALTER TABLE export ENABLE ROW LEVEL SECURITY;
                CREATE POLICY export_tenant ON export USING (tenant_id::text = current_setting('askwerk.tenant_id', true));

                CREATE FUNCTION api_key_lookup(candidate text) RETURNS SETOF api_key
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT * FROM api_key WHERE prefix = candidate $$;
                REVOKE ALL ON FUNCTION api_key_lookup(text) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION api_key_lookup(text) TO askwerk_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS api_key_lookup(text);");

            migrationBuilder.DropTable(
                name: "export");

            migrationBuilder.DropTable(
                name: "api_key");
        }
    }
}
