// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Surveys;

/// <summary>Whether all or any of a group's conditions must hold.</summary>
public enum LogicMatch
{
    /// <summary>Every condition (and).</summary>
    All,

    /// <summary>At least one condition (or).</summary>
    Any,
}

/// <summary>What a condition says about an earlier answer (AWK-31, W19 E).</summary>
public enum LogicOperator
{
    /// <summary>Equals an option code, or a number.</summary>
    Is,

    /// <summary>Differs from an option code, or a number.</summary>
    IsNot,

    /// <summary>Is one of the option codes.</summary>
    IsOneOf,

    /// <summary>Multiple choice includes the option.</summary>
    Includes,

    /// <summary>Multiple choice includes at least one of the options.</summary>
    IncludesAny,

    /// <summary>Multiple choice includes none of the options.</summary>
    IncludesNone,

    /// <summary>At least the number.</summary>
    AtLeast,

    /// <summary>At most the number.</summary>
    AtMost,

    /// <summary>From number to To, both included.</summary>
    Between,

    /// <summary>Has an answer, Don't know included.</summary>
    Answered,

    /// <summary>Has no answer.</summary>
    NotAnswered,
}

/// <summary>One condition on an earlier question.</summary>
/// <param name="Question">The question's id.</param>
/// <param name="Op">What is said about its answer.</param>
/// <param name="Value">An option code.</param>
/// <param name="Values">Option codes.</param>
/// <param name="Number">A number.</param>
/// <param name="To">The upper end for between.</param>
public sealed record LogicCondition(string Question, LogicOperator Op, string? Value = null, IReadOnlyList<string>? Values = null, decimal? Number = null, decimal? To = null);

/// <summary>Conditions joined by all or any; one level (owner decision, W19).</summary>
/// <param name="Match">All or any.</param>
/// <param name="Conditions">The conditions.</param>
public sealed record LogicGroup(LogicMatch Match, IReadOnlyList<LogicCondition> Conditions);

/// <summary>After a page: when the conditions hold (always without When), jump forward or end.</summary>
/// <param name="When">The conditions; always when null.</param>
/// <param name="GoTo">A later page to go to.</param>
/// <param name="End">End the survey.</param>
public sealed record PageAfter(LogicGroup? When = null, string? GoTo = null, bool? End = null);

/// <summary>A page on a respondent's way with the questions shown on it.</summary>
/// <param name="Page">The page.</param>
/// <param name="Questions">Its questions that are shown.</param>
public sealed record RoutedPage(Page Page, IReadOnlyList<Question> Questions);

/// <summary>
/// Survey logic (AWK-31, W19): which pages and questions a respondent sees, and where the survey ends. Twin of
/// packages/schema/src/logic.ts; both run the golden corpus in tests/corpus/logic.
/// </summary>
public static class SurveyLogic
{
    /// <summary>Whether a condition holds. Unanswered and Don't know match only answered or not answered.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="answers">Answers by question id.</param>
    /// <returns>True when it holds.</returns>
    public static bool Holds(LogicCondition condition, IReadOnlyDictionary<string, AnswerValue> answers)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(answers);
        var answer = answers.GetValueOrDefault(condition.Question);
        var answered = answer is not null && !answer.IsEmpty;

        if (condition.Op == LogicOperator.Answered)
        {
            return answered;
        }

        if (condition.Op == LogicOperator.NotAnswered)
        {
            return !answered;
        }

        if (!answered || answer!.DontKnow == true)
        {
            return false;
        }

        var chosen = answer.Options ?? (answer.Option is null ? [] : [answer.Option]);
        var values = condition.Values ?? [];

        return condition.Op switch
        {
            LogicOperator.Is => condition.Number is { } number ? answer.Number == number : answer.Option == condition.Value,
            LogicOperator.IsNot => condition.Number is { } number ? answer.Number != number : answer.Option != condition.Value,
            LogicOperator.IsOneOf => values.Contains(answer.Option ?? ""),
            LogicOperator.Includes => chosen.Contains(condition.Value ?? ""),
            LogicOperator.IncludesAny => values.Any(chosen.Contains),
            LogicOperator.IncludesNone => !values.Any(chosen.Contains),
            LogicOperator.AtLeast => answer.Number is { } value && condition.Number is { } least && value >= least,
            LogicOperator.AtMost => answer.Number is { } value && condition.Number is { } most && value <= most,
            LogicOperator.Between => answer.Number is { } value && condition.Number is { } from && condition.To is { } to && value >= from && value <= to,
            _ => false,
        };
    }

    /// <summary>Whether a group holds; an empty or missing group holds.</summary>
    /// <param name="group">The group.</param>
    /// <param name="answers">Answers by question id.</param>
    /// <returns>True when it holds.</returns>
    public static bool Holds(LogicGroup? group, IReadOnlyDictionary<string, AnswerValue> answers)
    {
        if (group is null || group.Conditions.Count == 0)
        {
            return true;
        }

        return group.Match == LogicMatch.Any ? group.Conditions.Any(condition => Holds(condition, answers)) : group.Conditions.All(condition => Holds(condition, answers));
    }

    /// <summary>
    /// The respondent's way: shown pages with their shown questions, until a rule ends the survey. Answers to questions
    /// that are not shown count as not given. Jumps go only forward; others are ignored.
    /// </summary>
    /// <param name="definition">The survey.</param>
    /// <param name="answers">Answers by question id.</param>
    /// <returns>The pages on the way.</returns>
    public static IReadOnlyList<RoutedPage> Route(SurveyDefinition definition, IReadOnlyDictionary<string, AnswerValue> answers)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(answers);
        var routed = new List<RoutedPage>();
        var known = new Dictionary<string, AnswerValue>();
        var index = 0;

        while (index < definition.Pages.Count)
        {
            var page = definition.Pages[index];
            var next = index + 1;

            if (Holds(page.ShowWhen, known))
            {
                var questions = new List<Question>();

                foreach (var question in page.Questions.Where(question => Holds(question.ShowWhen, known)))
                {
                    questions.Add(question);

                    if (answers.TryGetValue(question.Id, out var answer))
                    {
                        known[question.Id] = answer;
                    }
                }

                routed.Add(new RoutedPage(page, questions));

                if (page.After is { } after && Holds(after.When, known))
                {
                    if (after.End == true)
                    {
                        break;
                    }

                    var target = after.GoTo is null ? -1 : definition.Pages.ToList().FindIndex(each => each.Id == after.GoTo);

                    if (target > index)
                    {
                        next = target;
                    }
                }
            }

            index = next;
        }

        return routed;
    }
}
