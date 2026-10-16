// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Api.Configuration;
using Askwerk.Api.Startup;
using Microsoft.AspNetCore.Mvc;

namespace Askwerk.Api.App.Status;

/// <summary>What the placeholder page shows: version and whether the database is ready (App API).</summary>
[ApiController]
[Route("api/app/status")]
[ApiExplorerSettings(GroupName = "app")]
public sealed class StatusController(AskwerkSettings settings, DatabaseState state, IConfiguration configuration) : ControllerBase
{
    /// <summary>Returns the running version, start time and database state.</summary>
    /// <returns>The status; always 200, also while the database is not ready.</returns>
    [HttpGet]
    [ProducesResponseType<StatusResponse>(StatusCodes.Status200OK)]
    public ActionResult<StatusResponse> Get() => new StatusResponse(
        Version: settings.Version,
        Commit: configuration["ASKWERK_COMMIT"],
        StartedAt: state.StartedAt,
        Database: state.IsReady ? "ready" : "notReady");
}

/// <summary>Status of this Askwerk process.</summary>
/// <param name="Version">Semantic version, e.g. 0.1.0.</param>
/// <param name="Commit">Short git commit the image was built from, if known.</param>
/// <param name="StartedAt">When the process started (UTC).</param>
/// <param name="Database"><c>ready</c> or <c>notReady</c>.</param>
public sealed record StatusResponse(string Version, string? Commit, DateTimeOffset StartedAt, string Database);
