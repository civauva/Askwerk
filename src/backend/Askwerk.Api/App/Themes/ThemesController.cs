// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Api.Identity;
using Askwerk.Application.Auditing;
using Askwerk.Application.Persistence;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Domain.Surveys;
using Askwerk.Domain.Themes;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Themes;

/// <summary>
/// Themes of the organisation chosen in the session (AWK-17, W10 A). Everyone in the organisation sees them; owners
/// and admins change them. Every save runs <see cref="ThemeChecks"/>, and errors refuse it.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/themes")]
public sealed class ThemesController(AskwerkDbContext context, IAuditWriter audit, IUnitOfWork unitOfWork, TimeProvider time, ITraceContext trace) : ControllerBase
{
    private static readonly TenantRole[] Changers = [TenantRole.Owner, TenantRole.Admin];

    /// <summary>The organisation's themes, the default first.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The themes.</returns>
    [HttpGet]
    [ProducesResponseType<ThemeListResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ThemeListResponse>> List(CancellationToken cancellationToken)
    {
        if (User.TenantId() is null)
        {
            return Problem("organisation-not-chosen", StatusCodes.Status409Conflict);
        }

        var themes = await context.Themes.OrderByDescending(theme => theme.IsDefault).ThenBy(theme => theme.CreatedAt).ToListAsync(cancellationToken);

        return new ThemeListResponse([.. themes.Select(Describe)]);
    }

    /// <summary>A new theme; the organisation's first becomes the default.</summary>
    /// <param name="request">Name and tokens.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the id; 400 with the check's findings; 403 for other roles.</returns>
    [HttpPost]
    [ProducesResponseType<CreatedThemeResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ThemeProblem>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(SaveThemeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        if (Checked(request) is not var (name, tokens))
        {
            return Invalid(request);
        }

        var now = time.GetUtcNow();
        var isFirst = !await context.Themes.AnyAsync(cancellationToken);
        var theme = Theme.Create(Guid.CreateVersion7(now), User.TenantId()!.Value, name, tokens, isFirst, User.UserId()!.Value, now);
        context.Themes.Add(theme);
        await AuditAsync(theme, "theme.created", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Created($"/api/app/themes/{theme.Id}", new CreatedThemeResponse(theme.Id));
    }

    /// <summary>Changes a theme; published surveys using it follow at once.</summary>
    /// <param name="id">The theme.</param>
    /// <param name="request">Name and tokens.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400 with the check's findings; 403 for other roles; 404 when not found.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ThemeProblem>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Change(Guid id, SaveThemeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        var theme = await context.Themes.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);

        if (theme is null)
        {
            return NotFound();
        }

        if (Checked(request) is not var (name, tokens))
        {
            return Invalid(request);
        }

        theme.Change(name, tokens, time.GetUtcNow());
        await AuditAsync(theme, "theme.changed", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Makes a theme the organisation's default.</summary>
    /// <param name="id">The theme.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403 for other roles; 404 when not found.</returns>
    [HttpPost("{id:guid}/default")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MakeDefault(Guid id, CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        var themes = await context.Themes.ToListAsync(cancellationToken);
        var chosen = themes.FirstOrDefault(theme => theme.Id == id);

        if (chosen is null)
        {
            return NotFound();
        }

        var now = time.GetUtcNow();

        // The old default steps down first: one default per organisation is a unique index.
        foreach (var other in themes.Where(theme => theme.IsDefault && theme.Id != id))
        {
            other.SetDefault(false, now);
        }

        await context.SaveChangesAsync(cancellationToken);
        chosen.SetDefault(true, now);
        await AuditAsync(chosen, "theme.made-default", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Deletes a theme that is not the default; surveys that used it go back to the default.</summary>
    /// <param name="id">The theme.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 409 for the default; 403 for other roles; 404 when not found.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ThemeProblem>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (await RefusedAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        var theme = await context.Themes.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);

        if (theme is null)
        {
            return NotFound();
        }

        if (theme.IsDefault)
        {
            return Conflict(new ThemeProblem("theme-is-default", [], System.Diagnostics.Activity.Current?.TraceId.ToHexString()));
        }

        var now = time.GetUtcNow();

        foreach (var survey in await context.Surveys.Where(survey => survey.ThemeId == id).ToListAsync(cancellationToken))
        {
            survey.UseTheme(null, now);
        }

        context.Themes.Remove(theme);
        await AuditAsync(theme, "theme.deleted", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private static ThemeResponse Describe(Theme theme) =>
        new(theme.Id, theme.Name, theme.IsDefault, JsonDocument.Parse(theme.Tokens).RootElement);

    /// <summary>Name and tokens as stored, when the name is given and the tokens have no errors.</summary>
    private static (string Name, string Tokens)? Checked(SaveThemeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || ThemeTokensJson.Read(request.Tokens) is not { } tokens)
        {
            return null;
        }

        return ThemeChecks.Check(tokens).Any(issue => issue.Severity == IssueSeverity.Error)
            ? null
            : (request.Name.Trim(), JsonSerializer.Serialize(tokens, SurveyDefinitionJson.Options));
    }

    private BadRequestObjectResult Invalid(SaveThemeRequest request)
    {
        IReadOnlyList<ThemeIssueResponse> issues = string.IsNullOrWhiteSpace(request.Name)
            ? [new ThemeIssueResponse("name-required", "error", "name", null, null)]
            : ThemeTokensJson.Read(request.Tokens) is { } tokens
                ? [.. ThemeChecks.Check(tokens).Select(issue => new ThemeIssueResponse(issue.Code, issue.Severity.ToString().ToLowerInvariant(), issue.Path, issue.Against, issue.Ratio))]
                : [new ThemeIssueResponse("tokens-invalid", "error", "tokens", null, null)];

        return BadRequest(new ThemeProblem("theme-checks-failed", issues, System.Diagnostics.Activity.Current?.TraceId.ToHexString()));
    }

    private ObjectResult Problem(string code, int status) =>
        StatusCode(status, new ThemeProblem(code, [], System.Diagnostics.Activity.Current?.TraceId.ToHexString()));

    /// <summary>Null when the user may change themes in the current organisation; otherwise the answer to give.</summary>
    private async Task<IActionResult?> RefusedAsync(CancellationToken cancellationToken)
    {
        var tenantId = User.TenantId();

        if (tenantId is null)
        {
            return Problem("organisation-not-chosen", StatusCodes.Status409Conflict);
        }

        var userId = User.UserId();
        var role = await context.Memberships
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => (TenantRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return role is { } known && Changers.Contains(known) ? null : Problem("role-not-allowed", StatusCodes.Status403Forbidden);
    }

    private Task AuditAsync(Theme theme, string action, CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new AuditContent(time.GetUtcNow(), theme.TenantId, AuditActorKind.User, User.UserId().ToString()!, action, "Theme", theme.Id.ToString(), Diff: null, trace.CurrentTraceId),
            cancellationToken);
}

/// <summary>Reads theme tokens in the shared JSON format (packages/schema/src/theme.ts).</summary>
public static class ThemeTokensJson
{
    /// <summary>The tokens, or null when the JSON is not a theme.</summary>
    /// <param name="json">The tokens as sent or stored.</param>
    /// <returns>The tokens or null.</returns>
    public static ThemeTokens? Read(JsonElement json)
    {
        try
        {
            return json.Deserialize<ThemeTokens>(SurveyDefinitionJson.Options) is { Colours: not null, Fonts: { Heading: not null, Body: not null } } tokens ? tokens : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads stored tokens.</summary>
    /// <param name="json">The stored JSON.</param>
    /// <returns>The tokens.</returns>
    public static ThemeTokens? Read(string json) => Read(JsonDocument.Parse(json).RootElement);
}

/// <summary>The organisation's themes.</summary>
/// <param name="Items">The default first, then by age.</param>
public sealed record ThemeListResponse(IReadOnlyList<ThemeResponse> Items);

/// <summary>One theme.</summary>
/// <param name="Id">The theme.</param>
/// <param name="Name">Its name.</param>
/// <param name="IsDefault">Surveys without their own theme use it.</param>
/// <param name="Tokens">The tokens (packages/schema format).</param>
public sealed record ThemeResponse(Guid Id, string Name, bool IsDefault, JsonElement Tokens);

/// <summary>A theme to save.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Tokens">The tokens (packages/schema format).</param>
public sealed record SaveThemeRequest(string? Name, JsonElement Tokens);

/// <summary>A created theme.</summary>
/// <param name="Id">Its id.</param>
public sealed record CreatedThemeResponse(Guid Id);

/// <summary>Why a theme was not saved.</summary>
/// <param name="Code">theme-checks-failed, theme-is-default, role-not-allowed or organisation-not-chosen.</param>
/// <param name="Issues">The check's findings.</param>
/// <param name="TraceId">Reference for support.</param>
public sealed record ThemeProblem(string Code, IReadOnlyList<ThemeIssueResponse> Issues, string? TraceId);

/// <summary>One finding of the theme checks.</summary>
/// <param name="Code">Stable code.</param>
/// <param name="Severity">error or warning.</param>
/// <param name="Path">Where, e.g. colours/text.</param>
/// <param name="Against">Contrast: the colour it is read against.</param>
/// <param name="Ratio">Contrast: the ratio found.</param>
public sealed record ThemeIssueResponse(string Code, string Severity, string Path, string? Against, double? Ratio);
