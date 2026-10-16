// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Domain.Surveys;
using Askwerk.Domain.Themes;

namespace Askwerk.Tests.Domain.Themes;

/// <summary>Runs the theme corpus shared with the TypeScript checks in packages/schema (AWK-17).</summary>
public sealed class ThemeChecksCorpusTests
{
    private static readonly string CorpusDirectory = Path.Combine(AppContext.BaseDirectory, "corpus", "theme-checks");
    private static readonly string PresetDirectory = Path.Combine(AppContext.BaseDirectory, "presets");

    public static TheoryData<string> Cases() =>
        new(Directory.GetFiles(CorpusDirectory, "*.json").Select(Path.GetFileName).OfType<string>().Order());

    [Theory]
    [MemberData(nameof(Cases))]
    public void Check_CorpusCase_ReportsExpectedIssues(string file)
    {
        var corpusCase = JsonSerializer.Deserialize<CorpusCase>(File.ReadAllText(Path.Combine(CorpusDirectory, file)), SurveyDefinitionJson.Options)!;

        Assert.Equal(corpusCase.Expected, ThemeChecks.Check(corpusCase.Tokens));
    }

    [Fact]
    public void Presets_EachPassItsOwnChecks()
    {
        var presets = Directory.GetFiles(PresetDirectory, "*.json")
            .Select(file => JsonSerializer.Deserialize<ThemePreset>(File.ReadAllText(file), SurveyDefinitionJson.Options)!)
            .ToList();

        Assert.Equal(["atelier", "blaupause", "graphit", "kiosk", "klar", "konfetti", "kontrast", "minimal", "nacht", "panorama", "papier", "pastell", "sonne", "wald", "warm", "werkstatt", "zeitung"], presets.Select(preset => preset.Id).Order());
        Assert.All(presets, preset => Assert.Empty(ThemeChecks.Check(preset.Tokens)));
    }

    [Fact]
    public void DefaultTheme_IsTheWerkstattPreset()
    {
        var werkstatt = JsonSerializer.Deserialize<ThemePreset>(File.ReadAllText(Path.Combine(PresetDirectory, "werkstatt.json")), SurveyDefinitionJson.Options)!;

        Assert.Equal(JsonSerializer.Serialize(werkstatt.Tokens, SurveyDefinitionJson.Options), JsonSerializer.Serialize(DefaultTheme.Werkstatt, SurveyDefinitionJson.Options));
    }

    private sealed record CorpusCase(string Name, ThemeTokens Tokens, IReadOnlyList<ThemeIssue> Expected);
}
