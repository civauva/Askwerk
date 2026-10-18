using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FullAccessContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AWK-67: "Full access" has every scope, also for endpoints added later. Keys and OAuth clients made with it
            // before the contacts scopes existed get them, in the order of ApiScopes.Allowed; no other key changes.
            foreach (var table in new[] { "api_key", "oauth_client" })
            {
                migrationBuilder.Sql($"""
                    UPDATE {table}
                    SET scopes = ARRAY(
                        SELECT allowed.scope
                        FROM unnest(ARRAY['surveys:read', 'surveys:write', 'responses:read', 'responses:write', 'participants:read', 'participants:write', 'contacts:read', 'contacts:write', 'webhooks:manage', 'users:manage']) WITH ORDINALITY AS allowed(scope, position)
                        WHERE allowed.scope = ANY(scopes || ARRAY['contacts:read', 'contacts:write'])
                        ORDER BY allowed.position)
                    WHERE scopes @> ARRAY['surveys:read', 'surveys:write', 'responses:read', 'responses:write', 'participants:read', 'participants:write', 'webhooks:manage']
                      AND NOT scopes && ARRAY['contacts:read', 'contacts:write'];
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "api_key", "oauth_client" })
            {
                migrationBuilder.Sql($"UPDATE {table} SET scopes = array_remove(array_remove(scopes, 'contacts:read'), 'contacts:write') WHERE scopes && ARRAY['contacts:read', 'contacts:write'];");
            }
        }
    }
}
