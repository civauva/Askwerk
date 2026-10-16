// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;

namespace Askwerk.Domain.Surveys;

/// <summary>Logic in words for the PDF (AWK-31, W19): "SAT is at most 2 and BEFORE is Yes", in English or German.</summary>
public static class LogicSentences
{
    /// <summary>A group as a sentence part, with variables and option labels.</summary>
    /// <param name="group">The conditions.</param>
    /// <param name="definition">The survey, for the questions and their options.</param>
    /// <param name="text">Picks a translated text in the reader's language.</param>
    /// <param name="german">German words, otherwise English.</param>
    /// <returns>The conditions in words.</returns>
    public static string Of(LogicGroup group, SurveyDefinition definition, Func<TranslatedText?, string> text, bool german)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(text);
        var questions = definition.Pages.SelectMany(page => page.Questions).ToDictionary(question => question.Id);
        var joiner = group.Match == LogicMatch.Any ? (german ? " oder " : " or ") : (german ? " und " : " and ");

        return string.Join(joiner, group.Conditions.Select(condition => Condition(condition, questions.GetValueOrDefault(condition.Question), text, german)));
    }

    private static string Condition(LogicCondition condition, Question? question, Func<TranslatedText?, string> text, bool german)
    {
        var variable = question?.Variable ?? condition.Question;
        string Label(string code) => Option(question, code, text, german);
        string List(IReadOnlyList<string>? codes) => string.Join(", ", (codes ?? []).Select(Label));
        string Number(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
        string Value() => condition.Number is { } number ? Number(number) : Label(condition.Value ?? "");

        var words = condition.Op switch
        {
            LogicOperator.Is => german ? $"ist {Value()}" : $"is {Value()}",
            LogicOperator.IsNot => german ? $"ist nicht {Value()}" : $"is not {Value()}",
            LogicOperator.IsOneOf => german ? $"ist eines von {List(condition.Values)}" : $"is one of {List(condition.Values)}",
            LogicOperator.Includes => german ? $"enthält {Label(condition.Value ?? "")}" : $"includes {Label(condition.Value ?? "")}",
            LogicOperator.IncludesAny => german ? $"enthält eines von {List(condition.Values)}" : $"includes any of {List(condition.Values)}",
            LogicOperator.IncludesNone => german ? $"enthält keines von {List(condition.Values)}" : $"includes none of {List(condition.Values)}",
            LogicOperator.AtLeast => german ? $"ist mindestens {Number(condition.Number)}" : $"is at least {Number(condition.Number)}",
            LogicOperator.AtMost => german ? $"ist höchstens {Number(condition.Number)}" : $"is at most {Number(condition.Number)}",
            LogicOperator.Between => german ? $"liegt zwischen {Number(condition.Number)} und {Number(condition.To)}" : $"is between {Number(condition.Number)} and {Number(condition.To)}",
            LogicOperator.Answered => german ? "ist beantwortet" : "is answered",
            _ => german ? "ist nicht beantwortet" : "is not answered",
        };

        return $"{variable} {words}";
    }

    private static string Option(Question? question, string code, Func<TranslatedText?, string> text, bool german)
    {
        if (question?.Type == QuestionType.YesNo)
        {
            var yes = question.Yes?.Code ?? "1";
            var answer = code == yes ? question.Yes : question.No;
            var label = text(answer?.Label);

            return label.Length > 0 ? label : code == yes ? (german ? "Ja" : "Yes") : (german ? "Nein" : "No");
        }

        var option = question?.Options?.FirstOrDefault(each => each.Code == code);
        var shown = option is null ? (question?.Other?.Code == code ? text(question.Other.Label) : "") : text(option.Label);

        return shown.Length > 0 ? shown : code;
    }
}
