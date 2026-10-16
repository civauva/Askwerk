using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OidcSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "oidc_authority",
                table: "tenant_sign_in",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "oidc_client_id",
                table: "tenant_sign_in",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "oidc_client_secret_protected",
                table: "tenant_sign_in",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "oidc_display_name",
                table: "tenant_sign_in",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "oidc_enabled",
                table: "tenant_sign_in",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // /sso/{slug} starts single sign-on before anyone is signed in: this definer function finds the organisation
            // of a short name, and only one that has single sign-on switched on (AWK-12, W22 step 2).
            migrationBuilder.Sql("""
                CREATE FUNCTION oidc_tenant_by_slug(candidate text) RETURNS uuid
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT t.id FROM tenant t JOIN tenant_sign_in s ON s.tenant_id = t.id WHERE t.slug = candidate AND s.oidc_enabled $$;
                REVOKE ALL ON FUNCTION oidc_tenant_by_slug(text) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION oidc_tenant_by_slug(text) TO askwerk_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS oidc_tenant_by_slug(text);");
            migrationBuilder.DropColumn(
                name: "oidc_authority",
                table: "tenant_sign_in");

            migrationBuilder.DropColumn(
                name: "oidc_client_id",
                table: "tenant_sign_in");

            migrationBuilder.DropColumn(
                name: "oidc_client_secret_protected",
                table: "tenant_sign_in");

            migrationBuilder.DropColumn(
                name: "oidc_display_name",
                table: "tenant_sign_in");

            migrationBuilder.DropColumn(
                name: "oidc_enabled",
                table: "tenant_sign_in");
        }
    }
}
