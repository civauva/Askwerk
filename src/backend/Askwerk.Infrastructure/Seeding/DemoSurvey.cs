// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Askwerk.Application.Storage;
using Askwerk.Domain.Assets;
using Askwerk.Domain.Auditing;
using Askwerk.Domain.Identity;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Auditing;
using Askwerk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Askwerk.Infrastructure.Seeding;

/// <summary>
/// The demo survey (AWK-23): every organisation gets one published survey that shows every question type, in German
/// and English. It is created once per organisation, with an id derived from the organisation's, so a restart or a
/// second instance never adds another.
/// </summary>
public static class DemoSurvey
{
    /// <summary>The survey's internal name in the list.</summary>
    public const string Title = "Demo: Customer satisfaction 2026";

    private const int PictureWidth = 1600;
    private const int PictureHeight = 600;

    /// <summary>The definition from the embedded demo-survey.json; the image block shows the given picture.</summary>
    /// <param name="pictureId">The demo picture's file; a placeholder when not given.</param>
    /// <returns>The survey definition.</returns>
    public static SurveyDefinition Definition(Guid? pictureId = null)
    {
        using var stream = typeof(DemoSurvey).Assembly.GetManifestResourceStream("Askwerk.Infrastructure.Seeding.demo-survey.json")
            ?? throw new InvalidOperationException("The demo survey is not embedded.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd().Replace("{demo-image}", (pictureId ?? Guid.Empty).ToString(), StringComparison.Ordinal);

        return JsonSerializer.Deserialize<SurveyDefinition>(json, SurveyDefinitionJson.Options)
            ?? throw new InvalidOperationException("The demo survey is empty.");
    }

    /// <summary>The demo picture: a diagonal gradient in the Werkstatt signal colours, as PNG.</summary>
    /// <returns>The PNG bytes.</returns>
    public static byte[] Picture()
    {
        using var surface = SKSurface.Create(new SKImageInfo(PictureWidth, PictureHeight));
        using var paint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(PictureWidth, PictureHeight), [new SKColor(0xfb, 0xe6, 0xda), new SKColor(0xd2, 0x44, 0x0f), new SKColor(0x8f, 0x2c, 0x06)], SKShaderTileMode.Clamp),
        };
        surface.Canvas.DrawRect(0, 0, PictureWidth, PictureHeight, paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }

    /// <summary>The demo survey's id in an organisation: always the same, so it is created only once.</summary>
    /// <param name="tenantId">The organisation.</param>
    /// <returns>A stable id.</returns>
    public static Guid IdFor(Guid tenantId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"askwerk.demo-survey:{tenantId:N}"))[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80); // version 8: custom
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant

        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>Publishes the demo survey, and the six richer demos (AWK-36), in every organisation that has none yet, as its first owner.</summary>
    /// <param name="ownerConnectionString">The database owner's connection: it sees every organisation.</param>
    /// <param name="storage">File storage, for the demo picture.</param>
    /// <param name="time">The clock.</param>
    /// <param name="traceId">The current trace, for the audit log.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>How many organisations got the demo survey.</returns>
    public static async Task<int> EnsureAsync(string ownerConnectionString, IFileStorage storage, TimeProvider time, string? traceId, CancellationToken cancellationToken)
    {
        var created = await EnsureFirstAsync(ownerConnectionString, storage, time, traceId, cancellationToken);

        // The six richer demos come with it (AWK-36).
        await DemoSurveys.EnsureAsync(ownerConnectionString, storage, time, traceId, cancellationToken);

        return created;
    }

    private static async Task<int> EnsureFirstAsync(string ownerConnectionString, IFileStorage storage, TimeProvider time, string? traceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(storage);
        await using var context = AskwerkDbContext.Create(ownerConnectionString);
        var owners = await context.Memberships
            .Where(membership => membership.Role == TenantRole.Owner)
            .GroupBy(membership => membership.TenantId)
            .Select(group => new { TenantId = group.Key, UserId = group.OrderBy(membership => membership.CreatedAt).Select(membership => membership.UserId).First() })
            .ToListAsync(cancellationToken);
        var wanted = owners.ToDictionary(owner => IdFor(owner.TenantId));
        var existing = await context.Surveys.Where(survey => wanted.Keys.Contains(survey.Id)).Select(survey => survey.Id).ToListAsync(cancellationToken);
        var missing = wanted.Where(pair => !existing.Contains(pair.Key)).ToList();

        if (missing.Count == 0)
        {
            return 0;
        }

        var audit = new EfAuditWriter(context);
        var picture = Picture();
        // Stores the audit entries chained, in the same transaction as the survey.
        var unitOfWork = new EfUnitOfWork(context);

        foreach (var (surveyId, owner) in missing)
        {
            var now = time.GetUtcNow();

            // The image block shows a picture made here, stored as the organisation's own file.
            var assetId = Guid.CreateVersion7(now);
            var key = $"{owner.TenantId:N}/{assetId:N}";
            await storage.SaveAsync(key, new MemoryStream(picture), cancellationToken);
            context.Assets.Add(Asset.Create(assetId, owner.TenantId, AssetKind.Image, "image/png", picture.LongLength, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(picture)), "askwerk-demo.png", key, owner.UserId, now, PictureWidth, PictureHeight));
            var definition = Definition(assetId);
            var (survey, draft) = Survey.Create(surveyId, owner.TenantId, Title, definition.Languages, definition.DefaultLanguage, owner.UserId, now);
            draft.ReplaceDefinition(definition, now);
            survey.DraftSaved(definition, now);
            context.Surveys.Add(survey);
            context.SurveyVersions.Add(draft);
            await WriteAuditAsync(audit, survey, "survey.created", now, traceId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            // As in publishing: the draft is published and stored before the next draft exists.
            var next = survey.Publish(draft, Guid.CreateVersion7(now), owner.UserId, now);
            var index = SurveyIndex.Build(draft, () => Guid.CreateVersion7(now));
            context.SurveyPages.AddRange(index.Pages);
            context.SurveyQuestions.AddRange(index.Questions);
            context.SurveyOptions.AddRange(index.Options);
            await WriteAuditAsync(audit, survey, "survey.published", now, traceId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            context.SurveyVersions.Add(next);
            await context.SaveChangesAsync(cancellationToken);
        }

        return missing.Count;
    }

    private static Task WriteAuditAsync(EfAuditWriter audit, Survey survey, string action, DateTimeOffset now, string? traceId, CancellationToken cancellationToken) =>
        audit.WriteAsync(new AuditContent(now, survey.TenantId, AuditActorKind.System, "demo-survey", action, "Survey", survey.Id.ToString(), Diff: null, traceId), cancellationToken);
}
