// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.Text;

namespace Askwerk.Domain.Surveys;

/// <summary>Which part of an answer a variable holds.</summary>
public enum VariablePart
{
    /// <summary>The whole answer: the chosen code, the number, the text or the date.</summary>
    Answer,

    /// <summary>One option: chosen or not (multiple choice), or its place (ranking).</summary>
    Option,

    /// <summary>One row of a matrix: the option chosen in it.</summary>
    Row,

    /// <summary>The text typed for "Other, please specify".</summary>
    Other,
}

/// <summary>A code and what it means.</summary>
/// <param name="Code">The code as exported.</param>
/// <param name="Label">Its label.</param>
public sealed record ValueLabel(string Code, string Label);

/// <summary>One column of an export.</summary>
/// <param name="Name">Variable name, stable across versions of the question.</param>
/// <param name="Label">The question's text, with the option or row for parts.</param>
/// <param name="QuestionKey">The question's id in the definition.</param>
/// <param name="Part">Which part of the answer it holds.</param>
/// <param name="PartCode">The option or row code for parts.</param>
/// <param name="Numeric">Whether every value is a number.</param>
/// <param name="Values">Value labels, in the survey's order.</param>
public sealed record CodebookVariable(string Name, string Label, string QuestionKey, VariablePart Part, string? PartCode, bool Numeric, IReadOnlyList<ValueLabel> Values);

/// <summary>
/// The codebook of a survey version (data-model.md, rule 3; AWK-24): one variable per answer part, with types and
/// value labels in one language. Every export follows it: multiple choice becomes one 1/0 variable per option, a
/// matrix one variable per row, ranking one place per option, "Other" text its own variable. "Don't know" is -99.
/// </summary>
public sealed class Codebook
{
    /// <summary>The code "Don't know" is exported as.</summary>
    public const string DontKnowCode = "-99";

    private static readonly TranslatedText NotChosen = new(new Dictionary<string, string> { ["de"] = "Nicht gewählt", ["en"] = "Not chosen" });
    private static readonly TranslatedText DontKnow = new(new Dictionary<string, string> { ["de"] = "Weiß nicht", ["en"] = "Don't know" });

    private readonly Dictionary<string, Question> questions;
    private readonly string language;
    private readonly string fallback;

    private Codebook(IReadOnlyList<CodebookVariable> variables, Dictionary<string, Question> questions, string language, string fallback)
    {
        Variables = variables;
        this.questions = questions;
        this.language = language;
        this.fallback = fallback;
    }

    /// <summary>The language of the labels.</summary>
    public string Language => language;

    /// <summary>The variables, in the survey's order.</summary>
    public IReadOnlyList<CodebookVariable> Variables { get; }

    /// <summary>The codebook of a definition, with labels in a language.</summary>
    /// <param name="definition">The version's definition.</param>
    /// <param name="language">The language for labels; the default language fills gaps.</param>
    /// <returns>The codebook.</returns>
    public static Codebook Of(SurveyDefinition definition, string language)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var fallback = definition.DefaultLanguage;
        string In(TranslatedText? text) => AnswerText.In(text, language, fallback);
        var variables = new List<CodebookVariable>();
        var questions = new Dictionary<string, Question>(StringComparer.Ordinal);

        foreach (var question in definition.Pages.SelectMany(page => page.Questions).Where(question => question.Type is not (QuestionType.TextBlock or QuestionType.Image)))
        {
            questions[question.Id] = question;
            var text = In(question.Text);
            var dontKnow = question.DontKnow == true ? new[] { new ValueLabel(DontKnowCode, In(DontKnow)) } : [];
            var options = (question.Options ?? []).Select(option => new ValueLabel(option.Code, In(option.Label))).ToList();
            CodebookVariable Part(VariablePart part, string code, string label, bool numeric, IReadOnlyList<ValueLabel> values) =>
                new($"{question.Variable}_{Safe(code)}", $"{text} – {label}", question.Id, part, code, numeric, values);

            switch (question.Type)
            {
                case QuestionType.MultipleChoice:
                    variables.AddRange(options.Select(option => Part(VariablePart.Option, option.Code, option.Label, true, [new ValueLabel("1", option.Label), new ValueLabel("0", In(NotChosen)), .. dontKnow])));
                    break;
                case QuestionType.Ranking:
                    variables.AddRange(options.Select(option => Part(VariablePart.Option, option.Code, option.Label, true, dontKnow)));
                    break;
                case QuestionType.Matrix:
                    variables.AddRange((question.Rows ?? []).Select(row => Part(VariablePart.Row, row.Code, In(row.Label), options.All(option => IsNumber(option.Code)), [.. options, .. dontKnow])));
                    break;
                default:
                    var values = AnswerText.Options(question).Select(option => new ValueLabel(option.Code, In(option.Label))).ToList();
                    var numeric = question.Type is QuestionType.Rating or QuestionType.Nps or QuestionType.Slider or QuestionType.Number
                        || (values.Count > 0 && values.All(value => IsNumber(value.Code)));
                    variables.Add(new CodebookVariable(question.Variable, text, question.Id, VariablePart.Answer, null, numeric, [.. values, .. dontKnow]));
                    break;
            }

            if (question.Other is { } other && question.Type is not (QuestionType.Matrix or QuestionType.Ranking))
            {
                variables.Add(new CodebookVariable(other.Variable, $"{text} – {In(other.Label)}", question.Id, VariablePart.Other, other.Code, false, []));
            }
        }

        return new Codebook(variables, questions, language, fallback);
    }

    /// <summary>One cell: the variable's value in an answer, as a code or as its label.</summary>
    /// <param name="variable">One of <see cref="Variables"/>.</param>
    /// <param name="value">The answer; null when the question was not answered.</param>
    /// <param name="labels">Labels instead of codes.</param>
    /// <returns>The cell; null when empty.</returns>
    public string? Cell(CodebookVariable variable, AnswerValue? value, bool labels)
    {
        ArgumentNullException.ThrowIfNull(variable);

        if (value is null)
        {
            return null;
        }

        if (value.DontKnow == true)
        {
            return variable.Part == VariablePart.Other ? null : labels ? AnswerText.In(DontKnow, language, fallback) : DontKnowCode;
        }

        var question = questions[variable.QuestionKey];
        string Label(string code) => variable.Values.FirstOrDefault(each => each.Code == code)?.Label ?? code;

        return variable.Part switch
        {
            VariablePart.Other => string.IsNullOrWhiteSpace(value.Other) ? null : value.Other,
            VariablePart.Row => value.Rows?.GetValueOrDefault(variable.PartCode!) is { } code ? (labels ? Label(code) : code) : null,
            VariablePart.Option when question.Type == QuestionType.Ranking =>
                value.Order?.ToList().IndexOf(variable.PartCode!) is >= 0 and var place ? (place + 1).ToString(CultureInfo.InvariantCulture) : null,
            VariablePart.Option => value.Options is null
                ? null
                : value.Options.Contains(variable.PartCode!) ? (labels ? Label("1") : "1") : (labels ? null : "0"),
            _ => Whole(question, value, labels, Label),
        };
    }

    private static string? Whole(Question question, AnswerValue value, bool labels, Func<string, string> label) =>
        value.Option is { } option ? (labels ? label(option) : option)
        : value.Number is { } number ? AnswerText.Number(number)
        : !string.IsNullOrWhiteSpace(value.Date) ? value.Date
        : question.Type is QuestionType.ShortText or QuestionType.LongText && !string.IsNullOrWhiteSpace(value.Text) ? value.Text
        : null;

    private static bool IsNumber(string code) => decimal.TryParse(code, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);

    /// <summary>A code as part of a variable name: letters, digits and underscores only.</summary>
    private static string Safe(string code)
    {
        var name = new StringBuilder(code.Length);

        foreach (var character in code)
        {
            name.Append(char.IsAsciiLetterOrDigit(character) ? character : '_');
        }

        return name.ToString();
    }
}
