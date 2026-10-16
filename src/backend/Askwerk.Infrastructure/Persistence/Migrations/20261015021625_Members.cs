using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Members : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_sign_in_at",
                table: "app_user",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "member_invitation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    token_protected = table.Column<string>(type: "text", nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member_invitation", x => x.id);
                    table.ForeignKey(
                        name: "FK_member_invitation_tenant_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_member_invitation_tenant_id_email",
                table: "member_invitation",
                columns: new[] { "tenant_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_member_invitation_token_hash",
                table: "member_invitation",
                column: "token_hash",
                unique: true);

            // Joining starts outside any organisation: a definer function finds an invitation's organisation by
            // the token's hash and nothing else; the row is then read inside that organisation (AWK-30).
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE, DELETE ON member_invitation TO askwerk_app;
                ALTER TABLE member_invitation ENABLE ROW LEVEL SECURITY;
                CREATE POLICY member_invitation_tenant ON member_invitation USING (tenant_id::text = current_setting('askwerk.tenant_id', true));

                CREATE FUNCTION member_invitation_tenant(hash text) RETURNS uuid
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT tenant_id FROM member_invitation WHERE token_hash = hash $$;
                REVOKE ALL ON FUNCTION member_invitation_tenant(text) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION member_invitation_tenant(text) TO askwerk_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS member_invitation_tenant(text);");

            migrationBuilder.DropTable(
                name: "member_invitation");

            migrationBuilder.DropColumn(
                name: "last_sign_in_at",
                table: "app_user");
        }
    }
}
