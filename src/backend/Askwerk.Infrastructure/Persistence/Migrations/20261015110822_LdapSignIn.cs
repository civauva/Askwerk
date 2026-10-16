using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LdapSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ldap_base_dn",
                table: "tenant_sign_in",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ldap_bind_dn",
                table: "tenant_sign_in",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ldap_bind_password_protected",
                table: "tenant_sign_in",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ldap_ca_certificate",
                table: "tenant_sign_in",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ldap_display_name",
                table: "tenant_sign_in",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ldap_enabled",
                table: "tenant_sign_in",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ldap_group_attribute",
                table: "tenant_sign_in",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "memberOf");

            migrationBuilder.AddColumn<string>(
                name: "ldap_host",
                table: "tenant_sign_in",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ldap_login_attribute",
                table: "tenant_sign_in",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "uid");

            migrationBuilder.AddColumn<string>(
                name: "ldap_object_class",
                table: "tenant_sign_in",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "person");

            migrationBuilder.AddColumn<int>(
                name: "ldap_port",
                table: "tenant_sign_in",
                type: "integer",
                nullable: false,
                defaultValue: 636);

            // The sign-in page asks which way an organisation's people sign in before anyone is signed in: this definer
            // function answers for an organisation short name with single sign-on or a directory, and only that.
            migrationBuilder.Sql("""
                CREATE FUNCTION sso_options(candidate text) RETURNS TABLE(tenant uuid, oidc boolean, ldap boolean, oidc_name text, ldap_name text)
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path = public
                  AS $$ SELECT t.id, s.oidc_enabled, s.ldap_enabled, COALESCE(s.oidc_display_name, t.name), COALESCE(s.ldap_display_name, t.name)
                        FROM tenant t JOIN tenant_sign_in s ON s.tenant_id = t.id
                        WHERE t.slug = candidate AND (s.oidc_enabled OR s.ldap_enabled) $$;
                REVOKE ALL ON FUNCTION sso_options(text) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION sso_options(text) TO askwerk_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sso_options(text);");
            migrationBuilder.DropColumn(
                name: "ldap_base_dn",
                table: "tenant_sign_in");

            migrationBuilder.DropColumn(
                name: "ldap_bind_dn",
                table: "tenant_sign_in");

            migrationBuilder.DropColumn(
                name: "ldap_bind_password_protected",
                table: "tenant_sign_in");

            migrationBuilder.DropColumn(
                name: "ldap_ca_certificate",
                table: "tenant_sign_in");

            migrationBuilder.DropColumn(
                name: "ldap_display_name",
                table: "tenant_sign_in");

            migrationBuilder.DropColumn(
                name: "ldap_enabled",
                table: "tenant_sign_in");

            migrationBuilder.DropColumn(
                name: "ldap_group_attribute",
                table: "tenant_sign_in");

            migrationBuilder.DropColumn(
                name: "ldap_host",
                table: "tenant_sign_in");

            migrationBuilder.DropColumn(
                name: "ldap_login_attribute",
                table: "tenant_sign_in");

            migrationBuilder.DropColumn(
                name: "ldap_object_class",
                table: "tenant_sign_in");

            migrationBuilder.DropColumn(
                name: "ldap_port",
                table: "tenant_sign_in");
        }
    }
}
