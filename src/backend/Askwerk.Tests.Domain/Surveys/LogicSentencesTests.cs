// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Surveys;

namespace Askwerk.Tests.Domain.Surveys;

/// <summary>Logic in words for the PDF (AWK-31, W19).</summary>
public sealed class LogicSentencesTests
{
    private static TranslatedText Text(string de, string en) => new(new Dictionary<string, string> { ["de"] = de, ["en"] = en });

    private static readonly SurveyDefinition Survey = new(
        1,
        ["de", "en"],
        "de",
        [
            new Page("p1", [
                new Question("q1", "SAT", QuestionType.Rating, Text("Zufrieden?", "Satisfied?"), Required: true),
                new Question("q2", "BEFORE", QuestionType.YesNo, Text("Schon hier?", "Been here?"), Required: false),
                new Question("q3", "ROLE", QuestionType.SingleChoice, Text("Rolle", "Role"), Required: false, Options: [new AnswerOption("guest", Text("Gast", "Guest")), new AnswerOption("staff", Text("Personal", "Staff"))]),
            ]),
        ]);

    [Fact]
    public void Of_AllConditions_ReadsAsASentence()
    {
        var group = new LogicGroup(LogicMatch.All, [new LogicCondition("q1", LogicOperator.AtMost, Number: 2), new LogicCondition("q2", LogicOperator.Is, Value: "1")]);

        Assert.Equal("SAT is at most 2 and BEFORE is Yes", LogicSentences.Of(group, Survey, text => text?.GetValueOrDefault("en") ?? "", german: false));
        Assert.Equal("SAT ist höchstens 2 und BEFORE ist Ja", LogicSentences.Of(group, Survey, text => text?.GetValueOrDefault("de") ?? "", german: true));
    }

    [Fact]
    public void Of_AnyCondition_UsesOptionLabels()
    {
        var group = new LogicGroup(LogicMatch.Any, [new LogicCondition("q3", LogicOperator.IsOneOf, Values: ["guest", "staff"]), new LogicCondition("q1", LogicOperator.Between, Number: 2, To: 4)]);

        Assert.Equal("ROLE is one of Guest, Staff or SAT is between 2 and 4", LogicSentences.Of(group, Survey, text => text?.GetValueOrDefault("en") ?? "", german: false));
    }
}
