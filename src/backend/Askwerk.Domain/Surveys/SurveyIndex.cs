// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;

namespace Askwerk.Domain.Surveys;

/// <summary>A page of a published version, derived from its definition for querying and reporting (data-model.md).</summary>
/// <param name="Id">Row id.</param>
/// <param name="TenantId">The organisation.</param>
/// <param name="SurveyVersionId">The published version.</param>
/// <param name="PageKey">The page's id in the definition.</param>
/// <param name="Position">0-based order.</param>
/// <param name="Title">Title as a translation map (JSON), if any.</param>
public sealed record SurveyPageIndex(Guid Id, Guid TenantId, Guid SurveyVersionId, string PageKey, int Position, string? Title);

/// <summary>A question of a published version.</summary>
/// <param name="Id">Row id.</param>
/// <param name="TenantId">The organisation.</param>
/// <param name="SurveyVersionId">The published version.</param>
/// <param name="PageId">The page row.</param>
/// <param name="QuestionKey">The question's id in the definition.</param>
/// <param name="Position">0-based order within the page.</param>
/// <param name="VariableName">Stable variable name, unique per version.</param>
/// <param name="Type">Question type (kebab-case, as in the definition).</param>
/// <param name="Text">Text as a translation map (JSON).</param>
/// <param name="Required">Whether an answer is required.</param>
public sealed record SurveyQuestionIndex(Guid Id, Guid TenantId, Guid SurveyVersionId, Guid PageId, string QuestionKey, int Position, string VariableName, string Type, string Text, bool Required);

/// <summary>An answer option or matrix row of a published question.</summary>
/// <param name="Id">Row id.</param>
/// <param name="TenantId">The organisation.</param>
/// <param name="QuestionId">The question row.</param>
/// <param name="Kind">"option", "row", or "other" for "Other, please specify".</param>
/// <param name="Position">0-based order.</param>
/// <param name="Code">Stable code used in exports.</param>
/// <param name="Label">Label as a translation map (JSON).</param>
public sealed record SurveyOptionIndex(Guid Id, Guid TenantId, Guid QuestionId, string Kind, int Position, string Code, string Label);

/// <summary>A variable renamed in a published version; exports map old data through it (data-model.md, rule 2).</summary>
/// <param name="Id">Row id.</param>
/// <param name="TenantId">The organisation.</param>
/// <param name="SurveyId">The survey.</param>
/// <param name="FromName">Name before.</param>
/// <param name="ToName">Name since <paramref name="SinceVersion"/>.</param>
/// <param name="SinceVersion">First version with the new name.</param>
public sealed record VariableMapping(Guid Id, Guid TenantId, Guid SurveyId, string FromName, string ToName, int SinceVersion);

/// <summary>The rows derived from a published version.</summary>
/// <param name="Pages">Pages.</param>
/// <param name="Questions">Questions.</param>
/// <param name="Options">Options and matrix rows.</param>
public sealed record SurveyIndex(IReadOnlyList<SurveyPageIndex> Pages, IReadOnlyList<SurveyQuestionIndex> Questions, IReadOnlyList<SurveyOptionIndex> Options)
{
    // The labels a yes/no question shows when the survey sets none.
    private static readonly TranslatedText DefaultYes = new(new Dictionary<string, string> { ["de"] = "Ja", ["en"] = "Yes" });
    private static readonly TranslatedText DefaultNo = new(new Dictionary<string, string> { ["de"] = "Nein", ["en"] = "No" });

    /// <summary>Derives the index of a published version.</summary>
    /// <param name="version">The version, published.</param>
    /// <param name="newId">Makes row ids.</param>
    /// <returns>The rows.</returns>
    public static SurveyIndex Build(SurveyVersion version, Func<Guid> newId)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(newId);
        var definition = version.ReadDefinition();
        var pages = new List<SurveyPageIndex>();
        var questions = new List<SurveyQuestionIndex>();
        var options = new List<SurveyOptionIndex>();
        string Json<T>(T value) => JsonSerializer.Serialize(value, SurveyDefinitionJson.Options);

        foreach (var (page, pagePosition) in definition.Pages.Select((page, index) => (page, index)))
        {
            var pageRow = new SurveyPageIndex(newId(), version.TenantId, version.Id, page.Id, pagePosition, page.Title is null ? null : Json(page.Title));
            pages.Add(pageRow);

            foreach (var (question, position) in page.Questions.Select((question, index) => (question, index)))
            {
                var type = JsonSerializer.Serialize(question.Type, SurveyDefinitionJson.Options).Trim('"');
                var questionRow = new SurveyQuestionIndex(newId(), version.TenantId, version.Id, pageRow.Id, question.Id, position, question.Variable, type, Json(question.Text), question.Required);
                questions.Add(questionRow);
                options.AddRange((question.Options ?? []).Select((option, index) => new SurveyOptionIndex(newId(), version.TenantId, questionRow.Id, "option", index, option.Code, Json(option.Label))));
                options.AddRange((question.Rows ?? []).Select((row, index) => new SurveyOptionIndex(newId(), version.TenantId, questionRow.Id, "row", index, row.Code, Json(row.Label))));

                if (question.Other is { } other)
                {
                    options.Add(new SurveyOptionIndex(newId(), version.TenantId, questionRow.Id, "other", question.Options?.Count ?? 0, other.Code, Json(other.Label)));
                }

                if (question.Type == QuestionType.YesNo)
                {
                    var yes = question.Yes ?? new YesNoAnswer("1");
                    var no = question.No ?? new YesNoAnswer("0");
                    options.Add(new SurveyOptionIndex(newId(), version.TenantId, questionRow.Id, "option", 0, yes.Code, Json(yes.Label ?? DefaultYes)));
                    options.Add(new SurveyOptionIndex(newId(), version.TenantId, questionRow.Id, "option", 1, no.Code, Json(no.Label ?? DefaultNo)));
                }
            }
        }

        return new SurveyIndex(pages, questions, options);
    }
}
