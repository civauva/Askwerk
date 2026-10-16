// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Application.Setup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Askwerk.Api.App.Setup;

/// <summary>What anyone may know about the installation: its name, shown above the sign-in form (AWK-16 hi-fi M1).</summary>
[ApiController]
[Route("api/app/instance")]
[ApiExplorerSettings(GroupName = "app")]
[AllowAnonymous]
public sealed class InstanceController(IInstanceSettingsRepository settings) : ControllerBase
{
    /// <summary>The installation's public facts.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The name, or null before setup.</returns>
    [HttpGet]
    [ProducesResponseType<InstanceResponse>(StatusCodes.Status200OK)]
    public async Task<InstanceResponse> Get(CancellationToken cancellationToken) =>
        new((await settings.FindAsync(cancellationToken))?.Name);
}

/// <summary>Public facts of the installation.</summary>
/// <param name="Name">Instance name; null before setup.</param>
public sealed record InstanceResponse(string? Name);
