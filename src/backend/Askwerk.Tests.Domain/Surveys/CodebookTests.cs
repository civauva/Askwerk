// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Domain.Surveys;

namespace Askwerk.Tests.Domain.Surveys;

/// <summary>The codebook every export follows (AWK-24, data-model.md rule 3): variables, types and value labels of a version.</summary>
public sealed class CodebookTests
{
    private const string Questions = """
        [
          {"id":"q1","variable":"SOURCE","type":"single","text":{"en":"Where from?"},"required":false,
           "options":[{"code":"1","label":{"en":"Search"}},{"code":"2","label":{"en":"Ads"}}],"other":{"code":"OTHER","label":{"en":"Other:"},"variable":"SOURCE_OTHER"}},
          {"id":"q2","variable":"CH","type":"multiple","text":{"en":"Choose?"},"required":false,"options":[{"code":"a","label":{"en":"A"}},{"code":"b","label":{"en":"B"}}]},
          {"id":"q3","variable":"SAT","type":"rating","text":{"en":"Satisfied?"},"required":false,"scale":{"min":1,"max":5},"dontKnow":true},
          {"id":"q4","variable":"ASP","type":"matrix","text":{"en":"Rate?"},"required":false,
           "rows":[{"code":"x","label":{"en":"X"}},{"code":"y","label":{"en":"Y"}}],"options":[{"code":"1","label":{"en":"Good"}},{"code":"2","label":{"en":"Poor"}}]},
          {"id":"q5","variable":"RK","type":"ranking","text":{"en":"Order?"},"required":false,"options":[{"code":"a","label":{"en":"A"}},{"code":"b","label":{"en":"B"}}]},
          {"id":"q6","variable":"INTRO","type":"text-block","text":{"en":"Hello"},"required":false},
          {"id":"q7","variable":"TXT","type":"long-text","text":{"en":"Anything else?"},"required":false}
        ]
        """;

    private static readonly Codebook Book = Codebook.Of(
        JsonSerializer.Deserialize<SurveyDefinition>($$"""{"schemaVersion":1,"languages":["en"],"defaultLanguage":"en","pages":[{"id":"p1","questions":{{Questions}}}]}""", SurveyDefinitionJson.Options)!,
        "en");

    private static CodebookVariable Variable(string name) => Book.Variables.Single(variable => variable.Name == name);

    private static string?[] Cells(string question, AnswerValue? value, bool labels) =>
        [.. Book.Variables.Where(variable => variable.QuestionKey == question).Select(variable => Book.Cell(variable, value, labels))];

    [Fact]
    public void Variables_OnePerAnswer_InTheSurveysOrder_WithoutTextBlocks()
    {
        Assert.Equal(["SOURCE", "SOURCE_OTHER", "CH_a", "CH_b", "SAT", "ASP_x", "ASP_y", "RK_a", "RK_b", "TXT"], Book.Variables.Select(variable => variable.Name));
        Assert.Equal(
            ["Where from?", "Where from? – Other:", "Choose? – A", "Choose? – B", "Satisfied?", "Rate? – X", "Rate? – Y", "Order? – A", "Order? – B", "Anything else?"],
            Book.Variables.Select(variable => variable.Label));
        Assert.Equal([false, false, true, true, true, true, true, true, true, false], Book.Variables.Select(variable => variable.Numeric));
    }

    [Fact]
    public void ValueLabels_ForChoices_DichotomiesAndDontKnow()
    {
        Assert.Equal([new ValueLabel("1", "Search"), new ValueLabel("2", "Ads"), new ValueLabel("OTHER", "Other:")], Variable("SOURCE").Values);
        Assert.Equal([new ValueLabel("1", "A"), new ValueLabel("0", "Not chosen")], Variable("CH_a").Values);
        Assert.Equal([new ValueLabel("-99", "Don't know")], Variable("SAT").Values);
        Assert.Equal([new ValueLabel("1", "Good"), new ValueLabel("2", "Poor")], Variable("ASP_x").Values);
        Assert.Empty(Variable("TXT").Values);
    }

    [Fact]
    public void Cells_AsCodes_OrAsLabels()
    {
        Assert.Equal(["OTHER", "trade fair"], Cells("q1", new AnswerValue(Option: "OTHER", Other: "trade fair"), labels: false));
        Assert.Equal(["Other:", "trade fair"], Cells("q1", new AnswerValue(Option: "OTHER", Other: "trade fair"), labels: true));
        Assert.Equal(["1", "0"], Cells("q2", new AnswerValue(Options: ["a"]), labels: false));
        Assert.Equal(["A", null], Cells("q2", new AnswerValue(Options: ["a"]), labels: true));
        Assert.Equal(["4"], Cells("q3", new AnswerValue(Number: 4m), labels: false));
        Assert.Equal(["-99"], Cells("q3", new AnswerValue(DontKnow: true), labels: false));
        Assert.Equal(["Don't know"], Cells("q3", new AnswerValue(DontKnow: true), labels: true));
        Assert.Equal(["2", null], Cells("q4", new AnswerValue(Rows: new Dictionary<string, string> { ["x"] = "2" }), labels: false));
        Assert.Equal(["Poor", null], Cells("q4", new AnswerValue(Rows: new Dictionary<string, string> { ["x"] = "2" }), labels: true));
        Assert.Equal(["2", "1"], Cells("q5", new AnswerValue(Order: ["b", "a"]), labels: false));
        Assert.Equal(["Shorter queues"], Cells("q7", new AnswerValue(Text: "Shorter queues"), labels: true));
    }

    [Fact]
    public void Cells_OfAnUnansweredQuestion_AreEmpty()
    {
        Assert.Equal([null, null], Cells("q2", null, labels: false));
        Assert.Equal([null], Cells("q3", null, labels: true));
    }
}
