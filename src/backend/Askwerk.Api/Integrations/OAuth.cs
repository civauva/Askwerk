// SPDX-License-Identifier: AGPL-3.0-only
using System.Text;
using System.Text.Json.Serialization;
using Askwerk.Domain.Integrations;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.Integrations;

/// <summary>
/// Public API: OAuth 2.0 client credentials (AWK-32, C32 B). A client exchanges its id and secret for an opaque access
/// token that works 15 minutes and is sent like an API key (docs/api/oauth.md).
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = PublicApi.Group)]
[AllowAnonymous]
[Route("api/v1/oauth/token")]
public sealed class OAuthTokenController(AskwerkDbContext context, DbSessionContext session, TimeProvider time) : ControllerBase
{
    /// <summary>Gives an access token for the client credentials grant.</summary>
    /// <remarks>
    /// application/x-www-form-urlencoded: grant_type=client_credentials, the client's id and secret (in the body or as
    /// HTTP Basic), and optionally scope (space-separated, a subset of the client's). Errors follow RFC 6749.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The token.</returns>
    /// <response code="200">The access token.</response>
    /// <response code="400">invalid_request, unsupported_grant_type or invalid_scope.</response>
    /// <response code="401">invalid_client: unknown client, wrong secret, revoked or expired.</response>
    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    [Produces("application/json")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<TokenError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<TokenError>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Token(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var form = await Request.ReadFormAsync(cancellationToken);

        if (form["grant_type"] != "client_credentials")
        {
            return Error(StatusCodes.Status400BadRequest, form["grant_type"].Count == 0 ? "invalid_request" : "unsupported_grant_type", "Use grant_type=client_credentials.");
        }

        var (clientId, secret) = Credentials(form["client_id"].ToString(), form["client_secret"].ToString());

        if (clientId.Length == 0)
        {
            return Error(StatusCodes.Status401Unauthorized, "invalid_client", "Send client_id and client_secret, in the body or as HTTP Basic.");
        }

        var now = time.GetUtcNow();
        var client = await context.OAuthClients.FromSql($"SELECT * FROM oauth_client_lookup({clientId})").AsNoTracking().SingleOrDefaultAsync(cancellationToken);

        if (client is null || !client.Matches(secret) || !client.IsUsable(now))
        {
            return Error(StatusCodes.Status401Unauthorized, "invalid_client", "Unknown client, wrong secret, or the client was revoked or has expired.");
        }

        var asked = form["scope"].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (asked.Any(scope => !client.Scopes.Contains(scope)))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_scope", "Ask only for scopes the client has.");
        }

        var scopes = asked.Length == 0 ? client.Scopes : [.. client.Scopes.Where(asked.Contains)];

        // The token is stored inside the client's organisation, so row-level security accepts it.
        session.TenantId = client.TenantId;
        session.UserId = null;
        await context.Database.CloseConnectionAsync();
        var (token, value) = AccessToken.Issue(Guid.CreateVersion7(now), client, scopes, now);
        context.AccessTokens.Add(token);
        await context.SaveChangesAsync(cancellationToken);
        await context.OAuthClients.Where(row => row.Id == client.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.LastUsedAt, now), cancellationToken);

        return Ok(new TokenResponse(value, "Bearer", (int)AccessToken.Lifetime.TotalSeconds, string.Join(' ', scopes)));
    }

    private (string ClientId, string Secret) Credentials(string bodyId, string bodySecret)
    {
        var header = Request.Headers.Authorization.ToString();

        if (header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var pair = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim())).Split(':', 2);

                if (pair.Length == 2)
                {
                    return (Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1]));
                }
            }
            catch (FormatException)
            {
                return ("", "");
            }
        }

        return (bodyId.Trim(), bodySecret);
    }

    private ObjectResult Error(int status, string error, string description)
    {
        if (status == StatusCodes.Status401Unauthorized)
        {
            Response.Headers.WWWAuthenticate = "Basic realm=\"askwerk\"";
        }

        return StatusCode(status, new TokenError(error, description));
    }
}

/// <summary>An access token (RFC 6749, section 5.1).</summary>
/// <param name="AccessToken">The token (awt_…); send it as Authorization: Bearer.</param>
/// <param name="TokenType">Always Bearer.</param>
/// <param name="ExpiresIn">Seconds it works: 900.</param>
/// <param name="Scope">Its scopes, space-separated.</param>
public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("scope")] string Scope);

/// <summary>A token error (RFC 6749, section 5.2).</summary>
/// <param name="Error">invalid_request, invalid_client, unsupported_grant_type or invalid_scope.</param>
/// <param name="ErrorDescription">What to do.</param>
public sealed record TokenError(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string ErrorDescription);
