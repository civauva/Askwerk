// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Askwerk.Api.App.Assets;
using Askwerk.Api.App.Themes;
using Askwerk.Application.Storage;
using Askwerk.Domain.Participants;
using Askwerk.Domain.Surveys;
using Askwerk.Domain.Themes;
using Askwerk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Run;

/// <summary>
/// The runner's side of the App API (AWK-6, W8): respondents open a published survey by its link, answer page by
/// page and finish. No account is needed; a resume token in an HttpOnly cookie, scoped to this survey, finds the
/// response again. The survey's organisation comes from <c>run_survey_tenant</c>, so row-level security still applies.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[AllowAnonymous]
[Route("api/app/run/{surveyId:guid}")]
public sealed class RunController(AskwerkDbContext context, DbSessionContext session, TimeProvider time, Askwerk.Api.Identity.HandoverKeys handoverKeys) : ControllerBase
{
    private static readonly TimeSpan ResumeFor = TimeSpan.FromDays(30);
    private static readonly TimeSpan AnsweredFor = TimeSpan.FromDays(365);

    /// <summary>The published survey and, in a browser that started it, the response so far.</summary>
    /// <param name="surveyId">The survey from the link.</param>
    /// <param name="t">A personal link's token (AWK-7): its first opening is recorded.</param>
    /// <param name="settings">The instance's time zone, for the closing date.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The survey for respondents; 404 when it is not published.</returns>
    [HttpGet]
    [ProducesResponseType<RunSurveyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunSurveyResponse>> Open(Guid surveyId, [FromQuery] string? t, [FromServices] Askwerk.Application.Setup.IInstanceSettingsRepository settings, CancellationToken cancellationToken)
    {
        var found = await FindAsync(surveyId, cancellationToken);

        if (found is not var (survey, version, organisation))
        {
            return NotFound();
        }

        if (await ParticipantAsync(surveyId, t, cancellationToken) is { OpenedAt: null } participant)
        {
            participant.Opened(time.GetUtcNow());
            await context.SaveChangesAsync(cancellationToken);
        }

        var response = await CurrentResponseAsync(surveyId, cancellationToken);
        var definition = version.ReadDefinition();
        var now = time.GetUtcNow();
        var hostLook = await context.Tenants.Where(tenant => tenant.Id == survey.TenantId).Select(tenant => tenant.HostLook).SingleOrDefaultAsync(cancellationToken);

        return new RunSurveyResponse(
            new RunSurvey(
                survey.Id,
                organisation,
                survey.IsCollecting(now) ? "open" : "closed",
                JsonDocument.Parse(version.Definition).RootElement,
                SurveyThemes.ToJson(await SurveyThemes.EffectiveAsync(context, survey, cancellationToken)),
                survey.ClosesAt <= now ? survey.ClosesAt : null,
                (await settings.FindAsync(cancellationToken))?.TimeZone ?? "UTC",
                survey.OneResponsePerPerson),
            response is null ? null : new RunResponse(Lower(response.Status), response.PageKey, response.Language, await AnswersOfAsync(response, definition, cancellationToken)),
            Askwerk.Api.App.Domains.HostLook.Read(hostLook));
    }

    /// <summary>The survey a short link names (AWK-7): /s/{code}. Only published or closed surveys are found.</summary>
    /// <param name="code">The code from the link.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The survey's id; 404 otherwise.</returns>
    [HttpGet("~/api/app/run/code/{code}")]
    [ProducesResponseType<RunCodeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunCodeResponse>> Code(string code, CancellationToken cancellationToken)
    {
        var surveyId = code.Length > 16
            ? null
            : await context.Database.SqlQuery<Guid?>($"SELECT run_survey_id({code.ToLowerInvariant()}) AS \"Value\"").SingleAsync(cancellationToken);

        return surveyId is { } id ? new RunCodeResponse(id) : NotFound();
    }

    /// <summary>A file the survey shows (its theme's logo, images and fonts, its own header image); any other file is not found.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="assetId">The file.</param>
    /// <param name="w">Images: the width the page needs.</param>
    /// <param name="storage">File storage.</param>
    /// <param name="images">Resizes images.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The bytes; 404 when the survey does not show it.</returns>
    [HttpGet("assets/{assetId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Asset(Guid surveyId, Guid assetId, [FromQuery] int? w, [FromServices] IFileStorage storage, [FromServices] IImageProcessor images, CancellationToken cancellationToken)
    {
        var found = await FindAsync(surveyId, cancellationToken);

        if (found is not var (survey, version, _))
        {
            return NotFound();
        }

        var shown = DefaultTheme.AssetsOf(await SurveyThemes.EffectiveAsync(context, survey, cancellationToken))
            .Concat(ImagesOf(version.ReadDefinition()))
            .ToList();

        if (!shown.Contains(assetId.ToString()) && !shown.Contains(assetId.ToString("N")))
        {
            return NotFound();
        }

        var asset = await context.Assets.FirstOrDefaultAsync(row => row.Id == assetId, cancellationToken);

        if (asset is null)
        {
            return NotFound();
        }

        // The id never changes its content; the theme may stop using it, so caches keep it a day.
        Response.Headers.CacheControl = "public, max-age=86400";

        return await AssetsController.ServeAsync(asset, w, storage, images, cancellationToken);
    }

    /// <summary>Starts a response on the first page and gives the browser its resume token.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="request">The language the respondent chose.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the first page; 404 when not published; 409 when closed.</returns>
    [HttpPost("response")]
    [ProducesResponseType<RunResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<RunProblem>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start(Guid surveyId, StartResponseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var found = await FindAsync(surveyId, cancellationToken);

        if (found is not var (survey, version, _))
        {
            return NotFound();
        }

        if (!survey.IsCollecting(time.GetUtcNow()))
        {
            return Refused("closed");
        }

        // One response per person (W24): a browser that finished does not start again.
        if (survey.OneResponsePerPerson && await CurrentResponseAsync(surveyId, cancellationToken) is { Status: ResponseStatus.Complete })
        {
            return Refused("already-answered");
        }

        var participant = await ParticipantAsync(surveyId, request.Token, cancellationToken);
        var identified = request.Identity is null ? null : await IdentifiedAsync(survey, request.Identity, cancellationToken);

        if (request.Identity is not null && identified is null)
        {
            return StatusCode(StatusCodes.Status401Unauthorized, new RunProblem("identity-invalid", System.Diagnostics.Activity.Current?.TraceId.ToHexString()));
        }

        if (participant is not null && await context.Responses.AnyAsync(row => row.ParticipantId == participant.Id && row.Status == ResponseStatus.Complete, cancellationToken))
        {
            return Refused("already-answered");
        }

        var definition = version.ReadDefinition();
        var language = request.Language is { } chosen && definition.Languages.Contains(chosen) ? chosen : definition.DefaultLanguage;
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = time.GetUtcNow();
        var response = SurveyResponse.Start(Guid.CreateVersion7(now), survey.TenantId, survey.Id, version.Id, language, definition.Pages[0].Id, Hash(token), now);

        if (participant is not null)
        {
            response.StartedBy(participant.Id);
        }

        if (identified is not null)
        {
            response.IdentifiedBy(identified.Subject, identified.Email);
        }

        context.Responses.Add(response);
        await context.SaveChangesAsync(cancellationToken);
        Response.Cookies.Append(CookieName(surveyId), token, ResumeCookie(surveyId, ResumeFor));

        return StatusCode(StatusCodes.Status201Created, new RunResponse(Lower(response.Status), response.PageKey, response.Language, new Dictionary<string, AnswerValue>()));
    }

    /// <summary>Saves one page after checking every answer on it; the response continues on the next page.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="pageId">The page's id in the definition.</param>
    /// <param name="request">Answers by question id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400 with the problems per question; 409 when not started or already complete.</returns>
    [HttpPut("response/pages/{pageId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<RunProblem>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SavePage(Guid surveyId, string pageId, SavePageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var found = await FindAsync(surveyId, cancellationToken);

        if (found is not var (survey, version, _))
        {
            return NotFound();
        }

        var response = await CurrentResponseAsync(surveyId, cancellationToken);

        if (response is null)
        {
            return Refused("not-started");
        }

        if (response.Status == ResponseStatus.Complete)
        {
            return Refused("already-completed");
        }

        // Unfinished responses stop too once the survey closed (W24).
        if (!survey.IsCollecting(time.GetUtcNow()))
        {
            return Refused("closed");
        }

        var definition = version.ReadDefinition();
        var pageIndex = definition.Pages.ToList().FindIndex(page => page.Id == pageId);

        if (pageIndex < 0)
        {
            return NotFound();
        }

        var answers = request.Answers ?? new Dictionary<string, AnswerValue>();
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var questions = definition.Pages[pageIndex].Questions.Where(question => question.Type is not (QuestionType.TextBlock or QuestionType.Image)).ToList();

        // Logic (AWK-31): only the questions shown with the answers so far are checked and stored; the way decides the next page.
        var known = await AnswersOfAsync(response, definition, cancellationToken);

        foreach (var question in definition.Pages[pageIndex].Questions)
        {
            known.Remove(question.Id);

            if (answers.TryGetValue(question.Id, out var given))
            {
                known[question.Id] = given;
            }
        }

        var way = SurveyLogic.Route(definition, known);
        var here = way.ToList().FindIndex(routed => routed.Page.Id == pageId);
        var shown = here < 0 ? definition.Pages[pageIndex].Questions : way[here].Questions;
        var nextPageId = here >= 0 && here + 1 < way.Count ? way[here + 1].Page.Id : pageId;

        foreach (var (questionId, problem) in AnswerRules.CheckPage(definition.Pages[pageIndex] with { Questions = shown }, answers, today))
        {
            ModelState.AddModelError(questionId, problem);
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var now = time.GetUtcNow();
        var index = await QuestionIndexAsync(version.Id, cancellationToken);
        var stored = await context.Answers.Where(answer => answer.ResponseId == response.Id).ToDictionaryAsync(answer => answer.QuestionId, cancellationToken);

        foreach (var question in questions)
        {
            var questionId = index[question.Id];
            var value = shown.Contains(question) ? answers.GetValueOrDefault(question.Id) : null;
            stored.TryGetValue(questionId, out var existing);

            if (value is null || value.IsEmpty)
            {
                if (existing is not null)
                {
                    context.Answers.Remove(existing);
                }

                continue;
            }

            if (existing is null)
            {
                existing = Answer.Create(Guid.CreateVersion7(now), response.TenantId, response.Id, questionId);
                context.Answers.Add(existing);
            }

            existing.Set(question.Type, value, now);
        }

        response.PageSaved(nextPageId, now);
        await context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Finishes the response once every required question has an answer.</summary>
    /// <param name="surveyId">The survey.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 400 naming the questions still missing; 409 when not started or already complete.</returns>
    [HttpPost("response/complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<RunProblem>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(Guid surveyId, CancellationToken cancellationToken)
    {
        var found = await FindAsync(surveyId, cancellationToken);

        if (found is not var (survey, version, _))
        {
            return NotFound();
        }

        var response = await CurrentResponseAsync(surveyId, cancellationToken);

        if (response is null)
        {
            return Refused("not-started");
        }

        if (response.Status == ResponseStatus.Complete)
        {
            return Refused("already-completed");
        }

        if (!survey.IsCollecting(time.GetUtcNow()))
        {
            return Refused("closed");
        }

        // Only the questions on the respondent's way are required (AWK-31).
        var definition = version.ReadDefinition();
        var answers = await AnswersOfAsync(response, definition, cancellationToken);

        foreach (var question in SurveyLogic.Route(definition, answers).SelectMany(page => page.Questions).Where(question => question.Required && !answers.ContainsKey(question.Id)))
        {
            ModelState.AddModelError(question.Id, "required");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        response.Complete(time.GetUtcNow());
        await Askwerk.Api.Integrations.WebhookService.RaiseAsync(context, response.TenantId, "response.completed", new { surveyId, responseId = response.Id, version = version.Number, completedAt = time.GetUtcNow() }, time.GetUtcNow(), cancellationToken, surveyId);
        await context.SaveChangesAsync(cancellationToken);

        // One response per person (W24): the browser keeps its finished response a year, so it is not asked again.
        if (survey.OneResponsePerPerson)
        {
            Response.Cookies.Append(CookieName(surveyId), Request.Cookies[CookieName(surveyId)]!, ResumeCookie(surveyId, AnsweredFor));
        }

        return NoContent();
    }

    /// <summary>
    /// The resume token's cookie, scoped to this survey. Embedded on another website (AWK-7) the survey runs in a
    /// third-party frame: over HTTPS the cookie is SameSite=None and partitioned (CHIPS), so it works there and only there.
    /// </summary>
    private CookieOptions ResumeCookie(Guid surveyId, TimeSpan maxAge)
    {
        var cookie = new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = Request.IsHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Path = $"/api/app/run/{surveyId}",
            MaxAge = maxAge,
            IsEssential = true,
        };

        if (Request.IsHttps)
        {
            cookie.Extensions.Add("Partitioned");
        }

        return cookie;
    }

    /// <summary>Every image the definition shows: header image, image blocks and question images, picture choices.</summary>
    private static IEnumerable<string> ImagesOf(SurveyDefinition definition)
    {
        var questions = definition.Pages.SelectMany(page => page.Questions).ToList();

        return new[] { definition.HeaderImage?.Asset }
            .Concat(questions.Select(question => question.Image?.Asset))
            .Concat(questions.SelectMany(question => question.Options ?? []).Select(option => option.Image?.Asset))
            .OfType<string>()
            .Where(asset => asset.Length > 0);
    }

    /// <summary>Who an unsubscribe link belongs to (AWK-7): the organisation and the address, partly hidden.</summary>
    /// <param name="token">The token from the link.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page's text; 404 for an unknown link.</returns>
    [HttpGet("~/api/app/run/unsubscribe/{token}")]
    [ProducesResponseType<UnsubscribeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UnsubscribeResponse>> UnsubscribePage(string token, CancellationToken cancellationToken)
    {
        if (await UnsubscribeParticipantAsync(token, cancellationToken) is not { } participant)
        {
            return NotFound();
        }

        var organisation = await context.Tenants.Where(tenant => tenant.Id == participant.TenantId).Select(tenant => tenant.Name).SingleAsync(cancellationToken);
        var done = await context.Unsubscribes.AnyAsync(row => row.Email == participant.Email, cancellationToken);

        return new UnsubscribeResponse(organisation, Masked(participant.Email), participant.Language, done);
    }

    /// <summary>No more invitations from this organisation to this address (W12 note b); doing it twice is fine.</summary>
    /// <param name="token">The token from the link.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 404 for an unknown link.</returns>
    [HttpPost("~/api/app/run/unsubscribe/{token}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unsubscribe(string token, CancellationToken cancellationToken)
    {
        if (await UnsubscribeParticipantAsync(token, cancellationToken) is not { } participant)
        {
            return NotFound();
        }

        if (!await context.Unsubscribes.AnyAsync(row => row.Email == participant.Email, cancellationToken))
        {
            context.Unsubscribes.Add(Domain.Participants.Unsubscribe.Create(Guid.CreateVersion7(time.GetUtcNow()), participant.TenantId, participant.Email, time.GetUtcNow()));
            await context.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    /// <summary>"a***@acme.at": enough to recognise one's own address, not enough to learn someone else's.</summary>
    private static string Masked(string email) => email.IndexOf('@', StringComparison.Ordinal) is > 0 and var at ? $"{email[0]}***{email[at..]}" : "***";

    /// <summary>The invited person of a personal link to this survey; null without one. The survey's organisation is set.</summary>
    private async Task<Participant?> ParticipantAsync(Guid surveyId, string? token, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(token) || token.Length > 64
            ? null
            : await context.Participants.FirstOrDefaultAsync(row => row.Token == token && row.SurveyId == surveyId, cancellationToken);

    private async Task<Participant?> UnsubscribeParticipantAsync(string token, CancellationToken cancellationToken)
    {
        if (token.Length > 64)
        {
            return null;
        }

        var tenantId = await context.Database.SqlQuery<Guid?>($"SELECT run_participant_tenant({token}) AS \"Value\"").SingleAsync(cancellationToken);

        if (tenantId is null)
        {
            return null;
        }

        session.TenantId = tenantId;

        return await context.Participants.SingleAsync(row => row.Token == token, cancellationToken);
    }

    private static string Lower<TEnum>(TEnum value)
        where TEnum : struct, Enum => value.ToString().ToLowerInvariant();

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string CookieName(Guid surveyId) => $"askwerk.run.{surveyId:N}";

    private ObjectResult Refused(string code) =>
        StatusCode(StatusCodes.Status409Conflict, new RunProblem(code, System.Diagnostics.Activity.Current?.TraceId.ToHexString()));

    /// <summary>The survey, its latest published version and the organisation's name; null when it is not published.</summary>
    private async Task<(Survey Survey, SurveyVersion Version, string Organisation)?> FindAsync(Guid surveyId, CancellationToken cancellationToken)
    {
        var tenantId = await context.Database.SqlQuery<Guid?>($"SELECT run_survey_tenant({surveyId}) AS \"Value\"").SingleAsync(cancellationToken);

        if (tenantId is null)
        {
            return null;
        }

        // From here on every query runs in the survey's organisation (row-level security).
        session.TenantId = tenantId;
        var survey = await context.Surveys.SingleAsync(row => row.Id == surveyId, cancellationToken);
        var version = await context.SurveyVersions.SingleAsync(row => row.SurveyId == surveyId && row.Number == survey.PublishedVersion, cancellationToken);
        var organisation = await context.Tenants.Where(tenant => tenant.Id == tenantId).Select(tenant => new { tenant.Name, tenant.Status }).SingleAsync(cancellationToken);

        // A suspended or deleted organisation's surveys answer as unknown (AWK-38).
        return organisation.Status == Askwerk.Domain.Identity.TenantStatus.Active ? (survey, version, organisation.Name) : null;
    }

    /// <summary>
    /// A host product's token naming the respondent (AWK-13, W21): a hand-over token of the survey's organisation with
    /// the claim survey, used once.
    /// </summary>
    private async Task<Askwerk.Api.Identity.HandoverClaims?> IdentifiedAsync(Survey survey, string token, CancellationToken cancellationToken)
    {
        if (token.Length > 16_000 || Askwerk.Api.Identity.HandoverTokens.TenantOf(token) != survey.TenantId)
        {
            return null;
        }

        var settings = await context.TenantSignIns.AsNoTracking().FirstOrDefaultAsync(row => row.TenantId == survey.TenantId, cancellationToken);
        var now = time.GetUtcNow();

        if (settings is not { HandoverEnabled: true, HandoverIssuer: { } issuer }
            || Askwerk.Api.Identity.HandoverTokens.Check(token, survey.TenantId, issuer, await handoverKeys.OfAsync(settings, cancellationToken), now) is not { } claims
            || claims.Survey != survey.Id
            || await context.HandoverTokenUses.AnyAsync(row => row.TenantId == survey.TenantId && row.Jti == claims.Jti, cancellationToken))
        {
            return null;
        }

        context.HandoverTokenUses.Add(new Askwerk.Domain.Identity.HandoverTokenUse(survey.TenantId, claims.Jti, claims.ExpiresAt));

        return claims;
    }

    private async Task<SurveyResponse?> CurrentResponseAsync(Guid surveyId, CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(CookieName(surveyId), out var token) || string.IsNullOrEmpty(token))
        {
            return null;
        }

        var hash = Hash(token);

        return await context.Responses.SingleOrDefaultAsync(response => response.SurveyId == surveyId && response.ResumeTokenHash == hash, cancellationToken);
    }

    /// <summary>Question id in the definition → question row of the version's index.</summary>
    private Task<Dictionary<string, Guid>> QuestionIndexAsync(Guid versionId, CancellationToken cancellationToken) =>
        context.SurveyQuestions.Where(question => question.SurveyVersionId == versionId).ToDictionaryAsync(question => question.QuestionKey, question => question.Id, cancellationToken);

    private async Task<Dictionary<string, AnswerValue>> AnswersOfAsync(SurveyResponse response, SurveyDefinition definition, CancellationToken cancellationToken)
    {
        var index = await QuestionIndexAsync(response.SurveyVersionId, cancellationToken);
        var questions = definition.Pages.SelectMany(page => page.Questions).ToDictionary(question => index[question.Id]);
        var answers = await context.Answers.Where(answer => answer.ResponseId == response.Id).ToListAsync(cancellationToken);

        return answers.Where(answer => questions.ContainsKey(answer.QuestionId))
            .ToDictionary(answer => questions[answer.QuestionId].Id, answer => answer.ToValue(questions[answer.QuestionId].Type));
    }
}

/// <summary>What the runner gets when it opens a link.</summary>
/// <param name="Survey">The survey for respondents.</param>
/// <param name="Response">The response so far in this browser, if any.</param>
/// <param name="HostLook">The organisation's host look (W37), for a survey shown by &lt;askwerk-survey look="host"&gt;.</param>
public sealed record RunSurveyResponse(RunSurvey Survey, RunResponse? Response, Askwerk.Api.App.Domains.HostLook? HostLook = null);

/// <summary>A survey as respondents see it: no internal title, only what the definition shows.</summary>
/// <param name="Id">The survey.</param>
/// <param name="Organisation">The organisation's name, shown in the header.</param>
/// <param name="State">open or closed.</param>
/// <param name="Definition">The published definition.</param>
/// <param name="Theme">The look: the survey's theme, the organisation's default, or Werkstatt (AWK-17).</param>
/// <param name="ClosedAt">When it closed by its closing date (W24); null otherwise.</param>
/// <param name="TimeZone">The instance's time zone, for showing that date.</param>
/// <param name="OneResponsePerPerson">Whether a browser that finished may not answer again (W24).</param>
public sealed record RunSurvey(Guid Id, string Organisation, string State, JsonElement Definition, JsonElement Theme, DateTimeOffset? ClosedAt = null, string TimeZone = "UTC", bool OneResponsePerPerson = true);

/// <summary>A response in progress or finished.</summary>
/// <param name="Status">partial or complete.</param>
/// <param name="PageId">The page to continue on.</param>
/// <param name="Language">The language answered in.</param>
/// <param name="Answers">Answers so far, by question id.</param>
public sealed record RunResponse(string Status, string PageId, string Language, IReadOnlyDictionary<string, AnswerValue> Answers);

/// <summary>Starts a response.</summary>
/// <param name="Language">The language the respondent chose; the default language when not offered.</param>
/// <param name="Token">A personal link's token (AWK-7): the response stays with that person.</param>
/// <param name="Identity">A host product's hand-over token naming the respondent (AWK-13): the response stays with them.</param>
public sealed record StartResponseRequest(string? Language, string? Token = null, string? Identity = null);

/// <summary>An unsubscribe link's page.</summary>
/// <param name="Organisation">Who sends the invitations.</param>
/// <param name="Email">The address, partly hidden.</param>
/// <param name="Language">The person's language, if known.</param>
/// <param name="Done">Already unsubscribed.</param>
public sealed record UnsubscribeResponse(string Organisation, string Email, string? Language, bool Done);

/// <summary>One page of answers.</summary>
/// <param name="Answers">By question id.</param>
public sealed record SavePageRequest(IReadOnlyDictionary<string, AnswerValue>? Answers);

/// <summary>Why the runner refused.</summary>
/// <param name="Code">closed, not-started or already-completed.</param>
/// <param name="TraceId">Reference for support.</param>
public sealed record RunProblem(string Code, string? TraceId);

/// <summary>The survey behind a short link.</summary>
/// <param name="SurveyId">Its id, for the run API.</param>
public sealed record RunCodeResponse(Guid SurveyId);
