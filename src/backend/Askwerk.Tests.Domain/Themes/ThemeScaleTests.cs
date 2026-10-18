// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Domain.Surveys;
using Askwerk.Domain.Themes;

namespace Askwerk.Tests.Domain.Themes;

/// <summary>Five text sizes since W47 (AWK-73), stored as xs, s, m, l, xl like the editors and the runner write them.</summary>
public sealed class ThemeScaleTests
{
    [Theory]
    [InlineData("\"xs\"", ThemeScale.Xs)]
    [InlineData("\"s\"", ThemeScale.S)]
    [InlineData("\"m\"", ThemeScale.M)]
    [InlineData("\"l\"", ThemeScale.L)]
    [InlineData("\"xl\"", ThemeScale.Xl)]
    public void Scale_ReadsAndWritesItsShortName(string json, ThemeScale scale)
    {
        Assert.Equal(scale, JsonSerializer.Deserialize<ThemeScale>(json, SurveyDefinitionJson.Options));
        Assert.Equal(json, JsonSerializer.Serialize(scale, SurveyDefinitionJson.Options));
    }
}
