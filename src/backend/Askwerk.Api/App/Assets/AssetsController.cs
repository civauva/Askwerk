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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Askwerk.Api.App.Assets;

/// <summary>
/// Files the organisation uploads for its looks and surveys (AWK-17): images and fonts. The type is told from the
/// content, never from the name; the bytes go to file storage, the checked facts into the asset table.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "app")]
[Authorize(Policy = Sessions.SignedIn)]
[Route("api/app/assets")]
public sealed class AssetsController(AskwerkDbContext context, IFileStorage storage, IImageProcessor images, IAuditWriter audit, IUnitOfWork unitOfWork, TimeProvider time, ITraceContext trace) : ControllerBase
{
    private static readonly TenantRole[] Uploaders = [TenantRole.Owner, TenantRole.Admin, TenantRole.Editor];

    /// <summary>Uploads an image (PNG, JPEG, WebP, up to 5 MB) or a font (WOFF2, WOFF, TTF, OTF, up to 2 MB).</summary>
    /// <param name="file">The file.</param>
    /// <param name="kind">image or font.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with what was stored; 400 at the field; 403 for viewers and analysts.</returns>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [ProducesResponseType<AssetResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Upload(IFormFile? file, [FromForm] string? kind, CancellationToken cancellationToken)
    {
        if (User.TenantId() is not { } tenantId || !await MayUploadAsync(tenantId, cancellationToken))
        {
            return Forbid();
        }

        var assetKind = kind == "font" ? AssetKind.Font : AssetKind.Image;

        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError("file", "file-required");
            return ValidationProblem(ModelState);
        }

        if (file.Length > AssetTypes.MaxBytes(assetKind))
        {
            ModelState.AddModelError("file", "file-too-large");
            return ValidationProblem(ModelState);
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var contentType = AssetTypes.Detect(assetKind, bytes.AsSpan(0, Math.Min(bytes.Length, 16)));

        // An image must also decode: a right header on broken or hostile bytes is refused.
        var size = assetKind == AssetKind.Image && contentType is not null ? images.Measure(bytes) : null;

        if (contentType is null || (assetKind == AssetKind.Image && size is null))
        {
            ModelState.AddModelError("file", "file-type-not-allowed");
            return ValidationProblem(ModelState);
        }

        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        var key = $"{tenantId:N}/{id:N}";
        buffer.Position = 0;
        await storage.SaveAsync(key, buffer, cancellationToken);
        var asset = Asset.Create(id, tenantId, assetKind, contentType, bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)), file.FileName, key, User.UserId()!.Value, now, size?.Width, size?.Height);
        context.Assets.Add(asset);
        await audit.WriteAsync(new AuditContent(now, tenantId, AuditActorKind.User, User.UserId().ToString()!, "asset.uploaded", "Asset", asset.Id.ToString(), Diff: null, trace.CurrentTraceId), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Created($"/api/app/assets/{asset.Id}", Describe(asset));
    }

    /// <summary>An uploaded file, for everyone in the organisation; images also as a smaller copy.</summary>
    /// <param name="id">The file.</param>
    /// <param name="w">Images: the width the page needs; the next of 480, 960, 1600 or 2400 is served.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The bytes; 404 when not found.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, [FromQuery] int? w, CancellationToken cancellationToken)
    {
        var asset = await context.Assets.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);

        if (asset is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=31536000, immutable";

        return await ServeAsync(asset, w, storage, images, cancellationToken);
    }

    /// <summary>
    /// The bytes of a stored file, or of a smaller copy made once and kept next to it. Ids never change their content,
    /// so browsers may keep them.
    /// </summary>
    /// <param name="asset">The file.</param>
    /// <param name="wanted">Images: the width the page needs.</param>
    /// <param name="storage">File storage.</param>
    /// <param name="images">Resizes images.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The file, or 404 when its bytes are gone.</returns>
    internal static async Task<IActionResult> ServeAsync(Asset asset, int? wanted, IFileStorage storage, IImageProcessor images, CancellationToken cancellationToken)
    {
        var copyWidth = wanted is { } width and > 0 ? asset.CopyWidthFor(width) : null;

        if (copyWidth is not { } copy)
        {
            var original = storage.OpenRead(asset.StorageKey);

            return original is null ? new NotFoundResult() : new FileStreamResult(original, asset.ContentType);
        }

        var key = $"{asset.StorageKey}-w{copy}";

        if (storage.OpenRead(key) is not { } cached)
        {
            await using var source = storage.OpenRead(asset.StorageKey);

            if (source is null)
            {
                return new NotFoundResult();
            }

            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            await storage.SaveAsync(key, new MemoryStream(images.Resize(buffer.ToArray(), copy, asset.ContentType)), cancellationToken);
            cached = storage.OpenRead(key)!;
        }

        return new FileStreamResult(cached, asset.ContentType);
    }

    internal static AssetResponse Describe(Asset asset) =>
        new(asset.Id, asset.Kind.ToString().ToLowerInvariant(), asset.ContentType, asset.Size, asset.OriginalName, asset.Width, asset.Height);

    private async Task<bool> MayUploadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var userId = User.UserId();
        var role = await context.Memberships
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => (TenantRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return role is { } known && Uploaders.Contains(known);
    }
}

/// <summary>A stored file.</summary>
/// <param name="Id">Its id, used in themes and surveys.</param>
/// <param name="Kind">image or font.</param>
/// <param name="ContentType">Media type found from the content.</param>
/// <param name="Size">Bytes.</param>
/// <param name="Name">Name as uploaded.</param>
/// <param name="Width">Images: width in pixels.</param>
/// <param name="Height">Images: height in pixels.</param>
public sealed record AssetResponse(Guid Id, string Kind, string ContentType, long Size, string Name, int? Width, int? Height);
