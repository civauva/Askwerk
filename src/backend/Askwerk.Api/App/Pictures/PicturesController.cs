// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using Askwerk.Api.Identity;
using Askwerk.Application.Auditing;
using Askwerk.Application.Persistence;
using Askwerk.Application.Storage;
using Askwerk.Application.Telemetry;
using Askwerk.Domain.Assets;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Infrastructure.Persistence;
using Askwerk.Infrastructure.Pictures;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Pictures;

/// <summary>
/// Askwerk's background pictures (W46 B, AWK-74): public-domain photos by topic for a survey's or a theme's background.
/// Using one copies it into the organisation's files, as an upload would.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/pictures")]
public sealed class PicturesController(AskwerkDbContext context, IFileStorage storage, IAuditWriter audit, IUnitOfWork unitOfWork, TimeProvider time, ITraceContext trace) : ControllerBase
{
    private static readonly TenantRole[] Editors = [TenantRole.Owner, TenantRole.Admin, TenantRole.Editor];

    /// <summary>The pictures, by topic.</summary>
    /// <returns>The topics in their order and every picture with its source and licence.</returns>
    [HttpGet]
    [ProducesResponseType<PictureListResponse>(StatusCodes.Status200OK)]
    public ActionResult<PictureListResponse> List() =>
        new PictureListResponse(
            AskwerkPictures.Topics,
            [.. AskwerkPictures.All.Select(picture => new PictureResponse(picture.Id, picture.Topic, picture.Title, picture.Creator, picture.License, picture.Source, picture.Page, picture.Width, picture.Height))]);

    /// <summary>A picture's 480 × 300 thumbnail.</summary>
    /// <param name="id">The picture.</param>
    /// <returns>The WebP; 404 for an unknown picture.</returns>
    [HttpGet("{id}/thumbnail")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Thumbnail(string id) =>
        AskwerkPictures.Find(id) is { } picture ? File(AskwerkPictures.Thumbnail(picture), "image/webp") : NotFound();

    /// <summary>Copies a picture into the organisation's files, for a background.</summary>
    /// <param name="id">The picture.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the new file's id; 403 for viewers and analysts; 404 for an unknown picture.</returns>
    [HttpPost("{id}/use")]
    [ProducesResponseType<UsedPictureResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Use(string id, CancellationToken cancellationToken)
    {
        if (User.TenantId() is not { } tenantId || !await MayEditAsync(tenantId, cancellationToken))
        {
            return Forbid();
        }

        if (AskwerkPictures.Find(id) is not { } picture)
        {
            return NotFound();
        }

        var now = time.GetUtcNow();
        var userId = User.UserId()!.Value;
        var bytes = AskwerkPictures.Image(picture);
        var assetId = Guid.CreateVersion7(now);
        var key = $"{tenantId:N}/{assetId:N}";
        await storage.SaveAsync(key, new MemoryStream(bytes), cancellationToken);
        var asset = Asset.Create(assetId, tenantId, AssetKind.Image, "image/webp", bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)), $"{picture.Id}.webp", key, userId, now, picture.Width, picture.Height);
        context.Assets.Add(asset);
        await audit.WriteAsync(new AuditContent(now, tenantId, AuditActorKind.User, userId.ToString(), "asset.picture-used", "Asset", assetId.ToString(), Diff: null, trace.CurrentTraceId), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Created($"/api/app/assets/{assetId}", new UsedPictureResponse(assetId.ToString()));
    }

    private async Task<bool> MayEditAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var userId = User.UserId();
        var role = await context.Memberships
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => (TenantRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return role is { } known && Editors.Contains(known);
    }
}

/// <summary>Askwerk's pictures.</summary>
/// <param name="Topics">The topics in their order.</param>
/// <param name="Items">Every picture.</param>
public sealed record PictureListResponse(IReadOnlyList<string> Topics, IReadOnlyList<PictureResponse> Items);

/// <summary>One picture.</summary>
/// <param name="Id">Its key.</param>
/// <param name="Topic">Its topic.</param>
/// <param name="Title">Its title at its source.</param>
/// <param name="Creator">Who took it, when known.</param>
/// <param name="License">cc0 or pdm.</param>
/// <param name="Source">Where it was found.</param>
/// <param name="Page">Its page at the source.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public sealed record PictureResponse(string Id, string Topic, string Title, string? Creator, string License, string Source, string Page, int Width, int Height);

/// <summary>The organisation's copy of a picture.</summary>
/// <param name="AssetId">The new file's id.</param>
public sealed record UsedPictureResponse(string AssetId);
