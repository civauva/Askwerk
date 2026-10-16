// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Domain.Surveys;

namespace Askwerk.Tests.Domain.Surveys;

public sealed class SurveyIndexTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 13, 0, 0, TimeSpan.Zero);

    private static SurveyIndex IndexOf(string questionsJson)
    {
        var definition = JsonSerializer.Deserialize<SurveyDefinition>(
            $$"""{"schemaVersion":1,"languages":["de"],"defaultLanguage":"de","pages":[{"id":"p1","questions":{{questionsJson}}}]}""",
            SurveyDefinitionJson.Options)!;
        var (survey, draft) = Survey.Create(Guid.CreateVersion7(Now), Guid.CreateVersion7(Now), "Survey", ["de"], "de", Guid.CreateVersion7(Now), Now);
        draft.ReplaceDefinition(definition, Now);
        survey.Publish(draft, Guid.CreateVersion7(Now), survey.CreatedBy, Now);

        return SurveyIndex.Build(draft, () => Guid.CreateVersion7(Now));
    }

    [Fact]
    public void Build_OtherSpecify_IsAnOptionRowAfterTheOptions()
    {
        var index = IndexOf("""[{"id":"q1","variable":"SOURCE","type":"multiple","text":{"de":"Woher?"},"required":false,"options":[{"code":"1","label":{"de":"Suche"}},{"code":"2","label":{"de":"Werbung"}}],"other":{"code":"OTHER","label":{"de":"Andere:"},"variable":"SOURCE_OTHER"}}]""");

        Assert.Equal([("option", "1"), ("option", "2"), ("other", "OTHER")], index.Options.Select(option => (option.Kind, option.Code)));
    }

    [Fact]
    public void Build_YesNo_GivesTwoOptionRowsWithTheirCodes()
    {
        var index = IndexOf("""[{"id":"q1","variable":"BEFORE","type":"yes-no","text":{"de":"Schon einmal hier?"},"required":true}]""");

        Assert.Equal([("option", "1"), ("option", "0")], index.Options.Select(option => (option.Kind, option.Code)));
        Assert.Equal("yes-no", Assert.Single(index.Questions).Type);
    }
}
