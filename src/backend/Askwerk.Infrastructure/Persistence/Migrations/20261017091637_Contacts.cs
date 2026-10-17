using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Contacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_reminders",
                table: "survey",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "reminder_days",
                table: "survey",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "reminder_max",
                table: "survey",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "reminder_revision",
                table: "survey",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "contact_id",
                table: "participant",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_id",
                table: "participant",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fields",
                table: "participant",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<bool>(
                name: "reminders_off",
                table: "participant",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "contact",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    fields = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    groups = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact", x => x.id);
                    table.ForeignKey(
                        name: "FK_contact_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_participant_contact_id",
                table: "participant",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "IX_participant_survey_id_external_id",
                table: "participant",
                columns: new[] { "survey_id", "external_id" });

            migrationBuilder.CreateIndex(
                name: "IX_contact_tenant_id_email",
                table: "contact",
                columns: new[] { "tenant_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contact_tenant_id_external_id",
                table: "contact",
                columns: new[] { "tenant_id", "external_id" },
                unique: true,
                filter: "external_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_participant_contact_contact_id",
                table: "participant",
                column: "contact_id",
                principalTable: "contact",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // The address book per organisation (W35), with row-level security like every other table.
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE, DELETE ON contact TO askwerk_app;
                ALTER TABLE contact ENABLE ROW LEVEL SECURITY;
                CREATE POLICY contact_tenant ON contact USING (tenant_id::text = current_setting('askwerk.tenant_id', true));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_participant_contact_contact_id",
                table: "participant");

            migrationBuilder.DropTable(
                name: "contact");

            migrationBuilder.DropIndex(
                name: "IX_participant_contact_id",
                table: "participant");

            migrationBuilder.DropIndex(
                name: "IX_participant_survey_id_external_id",
                table: "participant");

            migrationBuilder.DropColumn(
                name: "auto_reminders",
                table: "survey");

            migrationBuilder.DropColumn(
                name: "reminder_days",
                table: "survey");

            migrationBuilder.DropColumn(
                name: "reminder_max",
                table: "survey");

            migrationBuilder.DropColumn(
                name: "reminder_revision",
                table: "survey");

            migrationBuilder.DropColumn(
                name: "contact_id",
                table: "participant");

            migrationBuilder.DropColumn(
                name: "external_id",
                table: "participant");

            migrationBuilder.DropColumn(
                name: "fields",
                table: "participant");

            migrationBuilder.DropColumn(
                name: "reminders_off",
                table: "participant");
        }
    }
}
