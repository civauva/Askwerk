// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Domain.Surveys;

namespace Askwerk.Tests.Domain.Surveys;

/// <summary>What the runner accepts as an answer (AWK-6): the server checks every page, whatever the browser did.</summary>
public sealed class AnswerRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    private static Question QuestionOf(string json) =>
        JsonSerializer.Deserialize<Question>(json, SurveyDefinitionJson.Options)!;

    private static AnswerValue? AnswerOf(string json) =>
        json == "null" ? null : JsonSerializer.Deserialize<AnswerValue>(json, SurveyDefinitionJson.Options);

    [Theory]
    [InlineData("""{"id":"q","variable":"Q","type":"rating","text":{"de":"x"},"required":true,"scale":{"min":1,"max":5}}""", "null", "required")]
    [InlineData("""{"id":"q","variable":"Q","type":"rating","text":{"de":"x"},"required":false,"scale":{"min":1,"max":5}}""", "null", null)]
    [InlineData("""{"id":"q","variable":"Q","type":"rating","text":{"de":"x"},"required":false,"scale":{"min":1,"max":5}}""", """{"number":6}""", "number-out-of-range")]
    [InlineData("""{"id":"q","variable":"Q","type":"rating","text":{"de":"x"},"required":false,"scale":{"min":1,"max":5}}""", """{"number":2.5}""", "number-not-whole")]
    [InlineData("""{"id":"q","variable":"Q","type":"nps","text":{"de":"x"},"required":false}""", """{"number":10}""", null)]
    [InlineData("""{"id":"q","variable":"Q","type":"slider","text":{"de":"x"},"required":false,"scale":{"min":0,"max":100,"step":5}}""", """{"number":72}""", "number-off-step")]
    [InlineData("""{"id":"q","variable":"Q","type":"slider","text":{"de":"x"},"required":false,"scale":{"min":0,"max":100,"step":5}}""", """{"number":70}""", null)]
    [InlineData("""{"id":"q","variable":"Q","type":"single","text":{"de":"x"},"required":false,"options":[{"code":"1","label":{"de":"a"}},{"code":"2","label":{"de":"b"}}]}""", """{"option":"3"}""", "option-unknown")]
    [InlineData("""{"id":"q","variable":"Q","type":"single","text":{"de":"x"},"required":false,"options":[{"code":"1","label":{"de":"a"}}],"other":{"code":"OTHER","label":{"de":"o"},"variable":"Q_OTHER"}}""", """{"option":"OTHER","other":" "}""", "other-text-missing")]
    [InlineData("""{"id":"q","variable":"Q","type":"multiple","text":{"de":"x"},"required":true,"options":[{"code":"1","label":{"de":"a"}}]}""", """{"options":[]}""", "required")]
    [InlineData("""{"id":"q","variable":"Q","type":"yes-no","text":{"de":"x"},"required":false}""", """{"option":"1"}""", null)]
    [InlineData("""{"id":"q","variable":"Q","type":"yes-no","text":{"de":"x"},"required":false,"yes":{"code":"Y"},"no":{"code":"N"}}""", """{"option":"1"}""", "option-unknown")]
    [InlineData("""{"id":"q","variable":"Q","type":"short-text","text":{"de":"x"},"required":false,"format":"email"}""", """{"text":"anna@acme.at"}""", null)]
    [InlineData("""{"id":"q","variable":"Q","type":"short-text","text":{"de":"x"},"required":false,"format":"phone"}""", """{"text":"call me"}""", "format-phone")]
    [InlineData("""{"id":"q","variable":"Q","type":"short-text","text":{"de":"x"},"required":false,"format":"url"}""", """{"text":"acme.at"}""", null)]
    [InlineData("""{"id":"q","variable":"Q","type":"date","text":{"de":"x"},"required":false,"latest":"today"}""", """{"date":"2026-10-15"}""", "date-too-late")]
    [InlineData("""{"id":"q","variable":"Q","type":"date","text":{"de":"x"},"required":false,"mode":"time","earliest":"08:00"}""", """{"date":"07:45"}""", "date-too-early")]
    [InlineData("""{"id":"q","variable":"Q","type":"date","text":{"de":"x"},"required":false,"mode":"date-time"}""", """{"date":"2026-10-14"}""", "date-invalid")]
    [InlineData("""{"id":"q","variable":"Q","type":"matrix","text":{"de":"x"},"required":true,"options":[{"code":"1","label":{"de":"a"}}],"rows":[{"code":"R1","label":{"de":"r"}},{"code":"R2","label":{"de":"s"}}]}""", """{"rows":{"R1":"1"}}""", "rows-missing")]
    [InlineData("""{"id":"q","variable":"Q","type":"ranking","text":{"de":"x"},"required":false,"options":[{"code":"1","label":{"de":"a"}},{"code":"2","label":{"de":"b"}}]}""", """{"order":["2","2"]}""", "order-invalid")]
    [InlineData("""{"id":"q","variable":"Q","type":"rating","text":{"de":"x"},"required":true,"dontKnow":true,"scale":{"min":1,"max":5}}""", """{"dontKnow":true}""", null)]
    [InlineData("""{"id":"q","variable":"Q","type":"long-text","text":{"de":"x"},"required":false}""", """{"number":1}""", "answer-invalid")]
    public void Check_Answer_GivesTheFirstProblem(string question, string answer, string? expected)
    {
        var problem = AnswerRules.Check(QuestionOf(question), AnswerOf(answer), Today);

        Assert.Equal(expected, problem);
    }
}
