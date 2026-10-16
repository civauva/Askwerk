// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Domain.Surveys;

namespace Askwerk.Tests.Domain.Surveys;

/// <summary>Runs the golden corpus shared with the TypeScript checks in packages/schema.</summary>
public sealed class DefinitionChecksCorpusTests
{
    private static readonly string CorpusDirectory = Path.Combine(AppContext.BaseDirectory, "corpus", "definition-checks");

    public static TheoryData<string> Cases() =>
        new(Directory.GetFiles(CorpusDirectory, "*.json").Select(Path.GetFileName).OfType<string>().Order());

    [Theory]
    [MemberData(nameof(Cases))]
    public void Check_CorpusCase_ReportsExpectedIssues(string file)
    {
        var corpusCase = ReadCase(file);

        var issues = DefinitionChecks.Check(corpusCase.Definition, corpusCase.Previous);

        Assert.Equal(corpusCase.Expected, issues);
    }

    public static TheoryData<string> CasesWithPrevious() =>
        new(Cases().Select(row => row.Data).Where(file => ReadCase(file).Previous is not null));

    [Theory]
    [MemberData(nameof(CasesWithPrevious))]
    public void VariableRenames_CorpusCase_ReportsExpectedRenames(string file)
    {
        var corpusCase = ReadCase(file);

        var renames = DefinitionChecks.VariableRenames(corpusCase.Previous!, corpusCase.Definition);

        Assert.Equal(corpusCase.ExpectedRenames, renames);
    }

    private static CorpusCase ReadCase(string file) =>
        JsonSerializer.Deserialize<CorpusCase>(File.ReadAllText(Path.Combine(CorpusDirectory, file)), SurveyDefinitionJson.Options)!;

    private sealed record CorpusCase(
        string Name,
        SurveyDefinition Definition,
        SurveyDefinition? Previous,
        IReadOnlyList<DefinitionIssue> Expected,
        IReadOnlyList<VariableRename>? ExpectedRenames);
}
