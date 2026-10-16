// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.RegularExpressions;

namespace Askwerk.Domain.Surveys;

/// <summary>Severity of a publish-check finding.</summary>
public enum IssueSeverity
{
    /// <summary>Blocks publishing.</summary>
    Error,

    /// <summary>Shown prominently; does not block publishing.</summary>
    Warning,
}

/// <summary>One finding of the publish checks.</summary>
/// <param name="Code">Stable code, e.g. "translation-missing".</param>
/// <param name="Severity">Whether it blocks publishing.</param>
/// <param name="Path">Slash path into the definition, e.g. "pages/0/questions/1/text".</param>
/// <param name="Language">The language concerned, for translation findings.</param>
public sealed record DefinitionIssue(string Code, IssueSeverity Severity, string Path, string? Language = null);

/// <summary>A variable renamed since the last published version; it becomes a VariableMapping (data-model.md, rule 2).</summary>
/// <param name="QuestionId">The question whose variable changed.</param>
/// <param name="From">Name in the published version.</param>
/// <param name="To">Name in the draft.</param>
public sealed record VariableRename(string QuestionId, string From, string To);

/// <summary>The checks that run before publishing (M3 in AWK-5). Twin of packages/schema/src/definition-checks.ts; both run the same corpus.</summary>
public static partial class DefinitionChecks
{
    /// <summary>Question types whose answers are chosen from options; they need at least two.</summary>
    private static readonly HashSet<QuestionType> ChoiceTypes = [QuestionType.SingleChoice, QuestionType.MultipleChoice, QuestionType.Dropdown, QuestionType.Ranking, QuestionType.Matrix];

    /// <summary>Question types that may offer "Other, please specify".</summary>
    private static readonly HashSet<QuestionType> OtherTypes = [QuestionType.SingleChoice, QuestionType.MultipleChoice, QuestionType.Dropdown];

    /// <summary>Returns all findings in document order.</summary>
    /// <param name="definition">The draft definition.</param>
    /// <param name="previous">The last published definition, if any.</param>
    /// <returns>Findings; empty when the survey can be published as is.</returns>
    public static IReadOnlyList<DefinitionIssue> Check(SurveyDefinition definition, SurveyDefinition? previous = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var issues = new List<DefinitionIssue>();
        void Error(string code, string path) => issues.Add(new DefinitionIssue(code, IssueSeverity.Error, path));

        // A required text must exist in the default language; any text that exists must exist in every survey language.
        void CheckText(TranslatedText? text, string path, bool required)
        {
            if (!required && definition.Languages.All(language => text?.Has(language) != true))
            {
                return;
            }

            if (text?.Has(definition.DefaultLanguage) != true)
            {
                issues.Add(new DefinitionIssue("text-missing", IssueSeverity.Error, path, definition.DefaultLanguage));
                return;
            }

            foreach (var language in definition.Languages.Where(language => language != definition.DefaultLanguage && !text.Has(language)))
            {
                issues.Add(new DefinitionIssue("translation-missing", IssueSeverity.Warning, path, language));
            }
        }

        // Codes are unique within their list; each item's label is a required text.
        void CheckCodedList(IReadOnlyList<AnswerOption>? items, string path, string duplicateCode)
        {
            var seenCodes = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 0; index < (items?.Count ?? 0); index++)
            {
                if (!seenCodes.Add(items![index].Code))
                {
                    Error(duplicateCode, $"{path}/{index}/code");
                }

                CheckText(items[index].Label, $"{path}/{index}/label", required: true);
            }
        }

        if (!definition.Languages.Contains(definition.DefaultLanguage))
        {
            Error("default-language-missing", "defaultLanguage");
        }

        if (definition.Pages.All(page => page.Questions.Count == 0))
        {
            Error("survey-empty", "pages");
        }

        var seenVariables = new HashSet<string>(StringComparer.Ordinal);
        var questionVariables = QuestionsOf(definition).Select(question => question.Variable).ToHashSet(StringComparer.Ordinal);
        var publishedOwners = QuestionsOf(previous).ToDictionary(question => question.Variable, question => question.Id, StringComparer.Ordinal);

        for (var pageIndex = 0; pageIndex < definition.Pages.Count; pageIndex++)
        {
            var page = definition.Pages[pageIndex];
            CheckText(page.Title, $"pages/{pageIndex}/title", required: false);

            for (var questionIndex = 0; questionIndex < page.Questions.Count; questionIndex++)
            {
                var question = page.Questions[questionIndex];
                var path = $"pages/{pageIndex}/questions/{questionIndex}";

                if (!VariableNamePattern().IsMatch(question.Variable))
                {
                    Error("variable-invalid", $"{path}/variable");
                }
                else if (seenVariables.Contains(question.Variable))
                {
                    Error("variable-duplicate", $"{path}/variable");
                }
                else if (publishedOwners.TryGetValue(question.Variable, out var owner) && owner != question.Id)
                {
                    Error("variable-reused", $"{path}/variable");
                }

                seenVariables.Add(question.Variable);

                if (question.Type is QuestionType.TextBlock or QuestionType.Image && question.Required)
                {
                    Error("required-not-allowed", $"{path}/required");
                }

                // An image block's text is its caption, which may be left out.
                CheckText(question.Text, $"{path}/text", required: question.Type != QuestionType.Image);
                CheckText(question.Help, $"{path}/help", required: false);

                if (question.Type == QuestionType.Image && question.Image is null)
                {
                    Error("image-missing", $"{path}/image");
                }
                else if (question.Image is { } image)
                {
                    if (string.IsNullOrWhiteSpace(image.Asset))
                    {
                        Error("image-missing", $"{path}/image/asset");
                    }

                    CheckText(image.Alt, $"{path}/image/alt", required: true);
                }

                if (question.Type == QuestionType.Rating && (question.Scale is null || question.Scale.Min >= question.Scale.Max))
                {
                    Error("scale-invalid", $"{path}/scale");
                }
                else if (question.Type == QuestionType.Rating)
                {
                    var steps = question.Scale!.Max - question.Scale.Min + 1;

                    if (question.Display == RatingDisplay.Faces && steps is not (3 or 5))
                    {
                        Error("faces-need-three-or-five-steps", $"{path}/scale");
                    }
                    else if (question.Display != RatingDisplay.Faces && steps > (question.Display is RatingDisplay.Stars or RatingDisplay.Dots ? 10 : 11))
                    {
                        Error("rating-steps-too-many", $"{path}/scale");
                    }
                }

                if (question.Type == QuestionType.Slider)
                {
                    var scale = question.Scale;
                    var step = scale?.Step ?? 1;

                    if (scale is null || scale.Min >= scale.Max || step <= 0 || (scale.Max - scale.Min) % step != 0)
                    {
                        Error("slider-range-invalid", $"{path}/scale");
                    }
                }

                CheckText(question.Scale?.MinLabel, $"{path}/scale/minLabel", required: false);
                CheckText(question.Scale?.MaxLabel, $"{path}/scale/maxLabel", required: false);

                if (ChoiceTypes.Contains(question.Type) && (question.Options?.Count ?? 0) < 2)
                {
                    Error("options-missing", $"{path}/options");
                }

                CheckCodedList(question.Options, $"{path}/options", "option-code-duplicate");

                for (var index = 0; index < (question.Options?.Count ?? 0); index++)
                {
                    if (question.Options![index].Image is { } picture && string.IsNullOrWhiteSpace(picture.Asset))
                    {
                        Error("image-missing", $"{path}/options/{index}/image");
                    }
                }

                if (question.Other is { } other)
                {
                    if (!OtherTypes.Contains(question.Type))
                    {
                        Error("other-not-allowed", $"{path}/other");
                    }
                    else
                    {
                        if (question.Options?.Any(option => option.Code == other.Code) == true)
                        {
                            Error("other-code-taken", $"{path}/other/code");
                        }

                        if (!VariableNamePattern().IsMatch(other.Variable))
                        {
                            Error("variable-invalid", $"{path}/other/variable");
                        }
                        else if (seenVariables.Contains(other.Variable) || questionVariables.Contains(other.Variable))
                        {
                            Error("other-variable-taken", $"{path}/other/variable");
                        }
                        else
                        {
                            // Reported once, here: the question that owns the name keeps it.
                            seenVariables.Add(other.Variable);
                        }

                        CheckText(other.Label, $"{path}/other/label", required: true);
                    }
                }

                if (question.Type == QuestionType.Matrix && (question.Rows?.Count ?? 0) == 0)
                {
                    Error("rows-missing", $"{path}/rows");
                }

                CheckCodedList(question.Rows, $"{path}/rows", "row-code-duplicate");

                if (question.Type == QuestionType.YesNo)
                {
                    if ((question.Yes?.Code ?? "1") == (question.No?.Code ?? "0"))
                    {
                        Error("yes-no-codes-equal", $"{path}/no/code");
                    }

                    CheckText(question.Yes?.Label, $"{path}/yes/label", required: false);
                    CheckText(question.No?.Label, $"{path}/no/label", required: false);
                }

                if (question.Type == QuestionType.Date)
                {
                    var pattern = (question.Mode ?? DateMode.Date) switch
                    {
                        DateMode.DateTime => DateTimePattern(),
                        DateMode.Time => TimePattern(),
                        _ => DatePattern(),
                    };
                    var earliestValid = question.Earliest is null || pattern.IsMatch(question.Earliest);
                    var latestValid = question.Latest is null || pattern.IsMatch(question.Latest);

                    if (!earliestValid)
                    {
                        Error("date-value-invalid", $"{path}/earliest");
                    }

                    if (!latestValid)
                    {
                        Error("date-value-invalid", $"{path}/latest");
                    }

                    if (earliestValid && latestValid && question.Earliest is { } earliest && question.Latest is { } latest
                        && earliest != "today" && latest != "today" && string.CompareOrdinal(earliest, latest) > 0)
                    {
                        Error("date-range-invalid", $"{path}/latest");
                    }

                    if (question.MinuteStep is { } minuteStep && (minuteStep < 1 || minuteStep > 60 || 60 % minuteStep != 0))
                    {
                        Error("minute-step-invalid", $"{path}/minuteStep");
                    }
                }

                CheckText(question.Placeholder, $"{path}/placeholder", required: false);
            }
        }

        // A welcome page that is switched off is never shown, so its texts are not checked.
        // Logic (AWK-31): conditions use earlier questions and their codes; jumps go forward to a page that exists.
        var positions = new Dictionary<string, (int Page, int Index, Question Question)>();

        for (var pageIndex = 0; pageIndex < definition.Pages.Count; pageIndex++)
        {
            for (var index = 0; index < definition.Pages[pageIndex].Questions.Count; index++)
            {
                positions.TryAdd(definition.Pages[pageIndex].Questions[index].Id, (pageIndex, index, definition.Pages[pageIndex].Questions[index]));
            }
        }

        static IReadOnlyList<string> CodesOf(Question question) =>
            question.Type == QuestionType.YesNo
                ? [question.Yes?.Code ?? "1", question.No?.Code ?? "0"]
                : [.. (question.Options ?? []).Select(option => option.Code), .. question.Other is { } other ? [other.Code] : Array.Empty<string>()];

        void CheckGroup(LogicGroup? group, string path, Func<(int Page, int Index), bool> earlier)
        {
            for (var conditionIndex = 0; conditionIndex < (group?.Conditions.Count ?? 0); conditionIndex++)
            {
                var condition = group!.Conditions[conditionIndex];
                var at = $"{path}/conditions/{conditionIndex}";

                if (!positions.TryGetValue(condition.Question, out var found))
                {
                    Error("logic-question-missing", $"{at}/question");
                    continue;
                }

                if (!earlier((found.Page, found.Index)))
                {
                    Error("logic-question-later", $"{at}/question");
                    continue;
                }

                var codes = CodesOf(found.Question);

                if (codes.Count > 0)
                {
                    if (condition.Value is not null && !codes.Contains(condition.Value))
                    {
                        Error("logic-option-missing", $"{at}/value");
                    }

                    for (var valueIndex = 0; valueIndex < (condition.Values?.Count ?? 0); valueIndex++)
                    {
                        if (!codes.Contains(condition.Values![valueIndex]))
                        {
                            Error("logic-option-missing", $"{at}/values/{valueIndex}");
                        }
                    }
                }
            }
        }

        for (var pageIndex = 0; pageIndex < definition.Pages.Count; pageIndex++)
        {
            var page = definition.Pages[pageIndex];
            var current = pageIndex;
            CheckGroup(page.ShowWhen, $"pages/{pageIndex}/showWhen", at => at.Page < current);

            for (var index = 0; index < page.Questions.Count; index++)
            {
                var questionIndex = index;
                CheckGroup(page.Questions[index].ShowWhen, $"pages/{pageIndex}/questions/{index}/showWhen", at => at.Page < current || (at.Page == current && at.Index < questionIndex));
            }

            if (page.After is { } after)
            {
                CheckGroup(after.When, $"pages/{pageIndex}/after/when", at => at.Page <= current);

                if (after.GoTo is not null)
                {
                    var target = definition.Pages.ToList().FindIndex(each => each.Id == after.GoTo);

                    if (target < 0)
                    {
                        Error("logic-page-missing", $"pages/{pageIndex}/after/goTo");
                    }
                    else if (target <= pageIndex)
                    {
                        Error("logic-jump-backward", $"pages/{pageIndex}/after/goTo");
                    }
                }
            }
        }

        if (definition.Welcome?.Enabled != false)
        {
            CheckText(definition.Welcome?.Title, "welcome/title", required: false);
            CheckText(definition.Welcome?.Text, "welcome/text", required: false);
        }
        CheckText(definition.EndPage?.Text, "endPage/text", required: false);

        if (definition.HeaderImage is { } headerImage)
        {
            CheckText(headerImage.Alt, "headerImage/alt", required: true);
        }

        return issues;
    }

    /// <summary>Questions present in both versions whose variable name changed, in the order of the draft.</summary>
    /// <param name="previous">The last published definition.</param>
    /// <param name="definition">The draft definition.</param>
    /// <returns>The renames to record as variable mappings.</returns>
    public static IReadOnlyList<VariableRename> VariableRenames(SurveyDefinition previous, SurveyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(definition);
        var publishedNames = QuestionsOf(previous).ToDictionary(question => question.Id, question => question.Variable, StringComparer.Ordinal);

        return [.. QuestionsOf(definition)
            .Where(question => publishedNames.TryGetValue(question.Id, out var from) && from != question.Variable)
            .Select(question => new VariableRename(question.Id, publishedNames[question.Id], question.Variable))];
    }

    private static IEnumerable<Question> QuestionsOf(SurveyDefinition? definition) =>
        definition?.Pages.SelectMany(page => page.Questions) ?? [];

    /// <summary>Variable names as the inspector explains them: A–Z, 0–9 and _, starting with a letter, at most 32 characters.</summary>
    [GeneratedRegex(@"^[A-Z][A-Z0-9_]{0,31}\z", RegexOptions.CultureInvariant)]
    private static partial Regex VariableNamePattern();

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2}|today)\z", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex DateTimePattern();

    [GeneratedRegex(@"^\d{2}:\d{2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TimePattern();
}
