// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text.Json;
using Askwerk.Application.Storage;
using Askwerk.Domain.Assets;
using Askwerk.Domain.Surveys;
using Askwerk.Infrastructure.Persistence;

namespace Askwerk.Infrastructure.Samples;

/// <summary>
/// Makes a survey from a sample or a seeded demo (W26, AWK-36): its pictures as the organisation's files, its own look,
/// published, with its sample answers, and closed when asked.
/// </summary>
public static class SampleSurveyBuilder
{
    /// <summary>Creates the survey; the caller audits and saves through the given steps.</summary>
    /// <param name="context">A context that sees the organisation.</param>
    /// <param name="storage">File storage, for the pictures.</param>
    /// <param name="sample">The sample or demo.</param>
    /// <param name="surveyId">The new survey's id.</param>
    /// <param name="tenantId">The organisation.</param>
    /// <param name="userId">Who creates it.</param>
    /// <param name="title">Its internal name.</param>
    /// <param name="close">Close it after the answers, so its link takes no responses.</param>
    /// <param name="audit">Writes an audit entry for the survey.</param>
    /// <param name="save">Saves the changes so far.</param>
    /// <param name="now">The time.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The survey.</returns>
    public static async Task<Survey> CreateAsync(
        AskwerkDbContext context,
        IFileStorage storage,
        SampleSurvey sample,
        Guid surveyId,
        Guid tenantId,
        Guid userId,
        string title,
        bool close,
        Func<Survey, string, Task> audit,
        Func<Task> save,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(save);
        var assets = new Dictionary<string, Guid>(StringComparer.Ordinal);

        // The pictures become the organisation's own files, as uploads would.
        foreach (var name in SampleSurveys.PictureNames(sample))
        {
            var picture = SampleSurveys.Picture(name);
            var assetId = Guid.CreateVersion7(now);
            var key = $"{tenantId:N}/{assetId:N}";
            await storage.SaveAsync(key, new MemoryStream(picture.Png), cancellationToken);
            context.Assets.Add(Asset.Create(assetId, tenantId, AssetKind.Image, "image/png", picture.Png.LongLength, Convert.ToHexStringLower(SHA256.HashData(picture.Png)), $"{name}.png", key, userId, now, picture.Width, picture.Height));
            assets[name] = assetId;
        }

        var definition = SampleSurveys.DefinitionWith(sample, assets);
        var (survey, draft) = Survey.Create(surveyId, tenantId, title, definition.Languages, definition.DefaultLanguage, userId, now);
        draft.ReplaceDefinition(definition, now);
        survey.DraftSaved(definition, now);
        survey.UseLook(JsonSerializer.Serialize(SampleSurveys.Looks[sample.Look], SurveyDefinitionJson.Options), $"preset:{sample.Look}", now);
        context.Surveys.Add(survey);
        context.SurveyVersions.Add(draft);
        await audit(survey, "survey.created-from-sample");
        await save();

        // As in publishing: the draft is published and stored before the next draft exists.
        var next = survey.Publish(draft, Guid.CreateVersion7(now), userId, now);
        var index = SurveyIndex.Build(draft, () => Guid.CreateVersion7(now));
        context.SurveyPages.AddRange(index.Pages);
        context.SurveyQuestions.AddRange(index.Questions);
        context.SurveyOptions.AddRange(index.Options);
        await audit(survey, "survey.published");
        await save();
        context.SurveyVersions.Add(next);
        AddAnswers(context, sample, definition, survey, draft.Id, index, now);

        if (close)
        {
            survey.Close(now);
            await audit(survey, "survey.closed");
        }

        await save();

        return survey;
    }

    /// <summary>The sample answers, completed over the last four weeks; the same for the same survey id.</summary>
    private static void AddAnswers(AskwerkDbContext context, SampleSurvey sample, SurveyDefinition definition, Survey survey, Guid versionId, SurveyIndex index, DateTimeOffset now)
    {
        var random = new Random(BitConverter.ToInt32(survey.Id.ToByteArray(), 0));
        var questionIds = index.Questions.ToDictionary(question => question.QuestionKey, question => question.Id, StringComparer.Ordinal);
        var types = definition.Pages.SelectMany(page => page.Questions).ToDictionary(question => question.Id, question => question.Type, StringComparer.Ordinal);

        for (var i = 0; i < sample.Responses; i++)
        {
            var (language, answers) = SampleSurveys.Answers(sample, definition, random);
            var started = now.AddMinutes(-random.Next(60, 28 * 24 * 60));
            var response = SurveyResponse.Start(Guid.CreateVersion7(started), survey.TenantId, survey.Id, versionId, language, definition.Pages[^1].Id, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), started);
            context.Responses.Add(response);

            foreach (var (questionId, value) in answers)
            {
                var answer = Answer.Create(Guid.CreateVersion7(started), survey.TenantId, response.Id, questionIds[questionId]);
                answer.Set(types[questionId], value, started);
                context.Answers.Add(answer);
            }

            response.Complete(started.AddMinutes(2 + random.Next(9)));
        }
    }
}
