using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Invitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invitation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    send = table.Column<bool>(type: "boolean", nullable: false),
                    send_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deadline = table.Column<DateOnly>(type: "date", nullable: false),
                    remind_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invitation", x => x.id);
                    table.ForeignKey(
                        name: "FK_invitation_survey_survey_id",
                        column: x => x.survey_id,
                        principalTable: "survey",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    done_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job", x => x.id);
                    table.ForeignKey(
                        name: "FK_job_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "unsubscribe",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_unsubscribe", x => x.id);
                    table.ForeignKey(
                        name: "FK_unsubscribe_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "participant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    bounced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reminders = table.Column<int>(type: "integer", nullable: false),
                    reminded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_participant", x => x.id);
                    table.ForeignKey(
                        name: "FK_participant_invitation_invitation_id",
                        column: x => x.invitation_id,
                        principalTable: "invitation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_participant_survey_survey_id",
                        column: x => x.survey_id,
                        principalTable: "survey",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_response_participant_id",
                table: "response",
                column: "participant_id");

            migrationBuilder.CreateIndex(
                name: "IX_invitation_survey_id",
                table: "invitation",
                column: "survey_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_due",
                table: "job",
                column: "run_at",
                filter: "done_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_job_tenant_id",
                table: "job",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_participant_invitation_id",
                table: "participant",
                column: "invitation_id");

            migrationBuilder.CreateIndex(
                name: "IX_participant_survey_id_email",
                table: "participant",
                columns: new[] { "survey_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_participant_token",
                table: "participant",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_unsubscribe_tenant_id_email",
                table: "unsubscribe",
                columns: new[] { "tenant_id", "email" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_response_participant_participant_id",
                table: "response",
                column: "participant_id",
                principalTable: "participant",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Row-level security per organisation. Two functions see across organisations and reveal nothing else:
            // the job workers claim due jobs (FOR UPDATE SKIP LOCKED), and an unsubscribe link finds its organisation.
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE ON invitation, participant TO askwerk_app;
                GRANT SELECT, INSERT ON unsubscribe TO askwerk_app;
                GRANT SELECT, INSERT, UPDATE ON job TO askwerk_app;
                ALTER TABLE invitation ENABLE ROW LEVEL SECURITY;
                CREATE POLICY invitation_tenant ON invitation USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                ALTER TABLE participant ENABLE ROW LEVEL SECURITY;
                CREATE POLICY participant_tenant ON participant USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                ALTER TABLE unsubscribe ENABLE ROW LEVEL SECURITY;
                CREATE POLICY unsubscribe_tenant ON unsubscribe USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                ALTER TABLE job ENABLE ROW LEVEL SECURITY;
                CREATE POLICY job_tenant ON job USING (tenant_id::text = current_setting('askwerk.tenant_id', true));

                CREATE FUNCTION claim_jobs(max_jobs int, lease_seconds int) RETURNS TABLE (id uuid, tenant_id uuid)
                  LANGUAGE sql VOLATILE SECURITY DEFINER SET search_path = public
                  AS $$ UPDATE job SET locked_until = now() + make_interval(secs => lease_seconds), attempts = job.attempts + 1
                        WHERE job.id IN (SELECT j.id FROM job j
                                         WHERE j.done_at IS NULL AND j.run_at <= now() AND (j.locked_until IS NULL OR j.locked_until < now())
                                         ORDER BY j.run_at LIMIT max_jobs FOR UPDATE SKIP LOCKED)
                        RETURNING job.id, job.tenant_id $$;
                REVOKE ALL ON FUNCTION claim_jobs(int, int) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION claim_jobs(int, int) TO askwerk_app;

                CREATE FUNCTION run_participant_tenant(candidate text) RETURNS uuid
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT tenant_id FROM participant WHERE token = candidate $$;
                REVOKE ALL ON FUNCTION run_participant_tenant(text) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION run_participant_tenant(text) TO askwerk_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS claim_jobs(int, int); DROP FUNCTION IF EXISTS run_participant_tenant(text);");

            migrationBuilder.DropForeignKey(
                name: "FK_response_participant_participant_id",
                table: "response");

            migrationBuilder.DropTable(
                name: "job");

            migrationBuilder.DropTable(
                name: "participant");

            migrationBuilder.DropTable(
                name: "unsubscribe");

            migrationBuilder.DropTable(
                name: "invitation");

            migrationBuilder.DropIndex(
                name: "IX_response_participant_id",
                table: "response");
        }
    }
}
