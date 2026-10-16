// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Domain.Surveys;

namespace Askwerk.Tests.Domain.Surveys;

/// <summary>Runs the logic corpus shared with packages/schema (AWK-31): both evaluators must agree on every way.</summary>
public sealed class SurveyLogicCorpusTests
{
    private static readonly string CorpusDirectory = Path.Combine(AppContext.BaseDirectory, "corpus", "logic");

    public static TheoryData<string> Cases() =>
        new(Directory.GetFiles(CorpusDirectory, "*.json").Select(Path.GetFileName).OfType<string>().Order());

    [Theory]
    [MemberData(nameof(Cases))]
    public void Route_CorpusCase_GivesTheExpectedWay(string file)
    {
        var corpusCase = JsonSerializer.Deserialize<CorpusCase>(File.ReadAllText(Path.Combine(CorpusDirectory, file)), SurveyDefinitionJson.Options)!;

        var way = SurveyLogic.Route(corpusCase.Definition, corpusCase.Answers);

        Assert.Equal(
            corpusCase.Expected.Select(page => $"{page.Page}: {string.Join(",", page.Questions)}"),
            way.Select(page => $"{page.Page.Id}: {string.Join(",", page.Questions.Select(question => question.Id))}"));
    }

    private sealed record CorpusCase(string Name, SurveyDefinition Definition, Dictionary<string, AnswerValue> Answers, IReadOnlyList<ExpectedPage> Expected);

    private sealed record ExpectedPage(string Page, IReadOnlyList<string> Questions);
}
