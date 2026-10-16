// SPDX-License-Identifier: AGPL-3.0-only
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Askwerk.Tests.Integration;

namespace Askwerk.Tests.Api;

/// <summary>Message templates and the email renderer (AWK-17 step 6, W10 D and E): texts per language, a preview, a test send.</summary>
public sealed class MessagesApiTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static async Task<string> PublishedSurveyAsync(HttpClient owner)
    {
        var id = await SurveysApiTests.CreateSurveyAsync(owner);
        (await owner.PutAsJsonAsync(Url($"/api/app/surveys/{id}/draft"), new { definition = RunnerApiTests.Definition() }, Cancel)).EnsureSuccessStatusCode();
        (await owner.PostAsync(Url($"/api/app/surveys/{id}/publish"), null, Cancel)).EnsureSuccessStatusCode();

        return id;
    }

    [Fact]
    public async Task List_GivesAskwerksTexts_ForInvitationReminderAndThanks_InBothLanguages()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);

        var items = (await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/messages"), Cancel)).GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(["invitation", "reminder", "thanks"], items.Select(item => item.GetProperty("kind").GetString()));
        Assert.All(items, item => Assert.True(item.GetProperty("isDefault").GetBoolean()));
        Assert.Contains("{link}", items[0].GetProperty("body").GetProperty("de").GetString(), StringComparison.Ordinal);
        Assert.Contains("{link}", items[0].GetProperty("body").GetProperty("en").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_RefusesPlaceholdersAskwerkDoesNotKnow_AndKeepsTheOrganisationsText()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var text = new { de = "Guten Tag {name}, bitte bis {deadline}: {link}", en = "Hello {name}, please by {deadline}: {link}" };

        var refused = await owner.PutAsJsonAsync(Url("/api/app/messages/invitation"), new { subject = new { de = "Umfrage bis {frist}", en = "Survey" }, body = text, answerInEmail = false }, Cancel);
        var saved = await owner.PutAsJsonAsync(Url("/api/app/messages/invitation"), new { subject = new { de = "{organisation}: Ihre Meinung", en = "{organisation}: your opinion" }, body = text, answerInEmail = true }, Cancel);
        var invitation = (await owner.GetFromJsonAsync<JsonElement>(Url("/api/app/messages"), Cancel)).GetProperty("items")[0];

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("placeholder-unknown", (await JsonAsync(refused)).GetProperty("errors").GetProperty("subject.de")[0].GetString());
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal((false, true, "{organisation}: your opinion"), (invitation.GetProperty("isDefault").GetBoolean(), invitation.GetProperty("answerInEmail").GetBoolean(), invitation.GetProperty("subject").GetProperty("en").GetString()));
    }

    [Fact]
    public async Task Preview_RendersTheEmailInTheSurveysTheme_WithTheFirstQuestionAsLinks()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var surveyId = await PublishedSurveyAsync(owner);

        var preview = await JsonAsync(await owner.PostAsJsonAsync(Url("/api/app/messages/invitation/preview"), new { surveyId, language = "en", answerInEmail = true }, Cancel));

        var html = preview.GetProperty("html").GetString()!;
        var text = preview.GetProperty("text").GetString()!;
        Assert.Equal("Acme GmbH: your opinion counts", preview.GetProperty("subject").GetString());
        Assert.Contains("#d2440f", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<table", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
        var code = (string)(await SurveysApiTests.ScalarAsync(factory, $"SELECT code FROM survey WHERE id = '{surveyId}'"))!;
        Assert.Contains($"/s/{code}", text, StringComparison.Ordinal);
        Assert.Contains("Satisfied?", html, StringComparison.Ordinal);
        Assert.All(Enumerable.Range(1, 5), value => Assert.Contains($"/s/{code}?answer=q1:{value}", html, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TestSend_GoesToTheSignedInUser_OnlyWhenEmailIsSetUp()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var surveyId = await PublishedSurveyAsync(owner);

        var withoutSmtp = await owner.PostAsJsonAsync(Url("/api/app/messages/reminder/test"), new { surveyId, language = "de" }, Cancel);
        await SurveysApiTests.ScalarAsync(factory, "UPDATE instance_settings SET smtp_host = 'smtp.acme.at', smtp_port = 587, smtp_sender = 'Acme Surveys <surveys@acme.at>'");
        var sent = await owner.PostAsJsonAsync(Url("/api/app/messages/reminder/test"), new { surveyId, language = "de" }, Cancel);
        var email = (string)(await SurveysApiTests.ScalarAsync(factory, "SELECT email FROM app_user"))!;

        Assert.Equal(HttpStatusCode.Conflict, withoutSmtp.StatusCode);
        Assert.Equal("email-not-set-up", (await JsonAsync(withoutSmtp)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NoContent, sent.StatusCode);
        var (_, message) = Assert.Single(factory.Emails.Sent);
        Assert.Equal(email, message.To);
        Assert.StartsWith("[Test] ", message.Subject, StringComparison.Ordinal);
        Assert.Contains("<table", message.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Editors_SeeTheTexts_ButOnlyOwnersAndAdminsChangeThem()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        await SurveysApiTests.ScalarAsync(factory, "UPDATE membership SET role = 'Editor'");

        var list = await owner.GetAsync(Url("/api/app/messages"), Cancel);
        var save = await owner.PutAsJsonAsync(Url("/api/app/messages/thanks"), new { subject = new { de = "Danke" }, body = new { de = "Danke!" }, answerInEmail = false }, Cancel);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Forbidden), (list.StatusCode, save.StatusCode));
    }

    [Fact]
    public async Task Preview_WithUnsavedTexts_AndAThemeWithLogoAndHeader_StillDrawsTheEmail()
    {
        await using var factory = await AskwerkApiFactory.WithFreshDatabaseAsync(server);
        using var owner = await SurveysApiTests.SignedInOwnerAsync(factory);
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(AssetImagesApiTests.SolidPng(400, 200));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "logo.png");
        content.Add(new StringContent("image"), "kind");
        var logo = (await JsonAsync(await owner.PostAsync(Url("/api/app/assets"), content, Cancel))).GetProperty("id").GetString();
        var tokens = ThemesApiTests.Werkstatt();
        tokens["layout"] = "header-image";
        tokens["logo"] = new System.Text.Json.Nodes.JsonObject { ["asset"] = logo };
        tokens["headerImage"] = new System.Text.Json.Nodes.JsonObject { ["asset"] = logo, ["focalX"] = 0.5, ["focalY"] = 0.5 };
        (await owner.PostAsJsonAsync(Url("/api/app/themes"), new { name = "Green", tokens }, Cancel)).EnsureSuccessStatusCode();
        var surveyId = await PublishedSurveyAsync(owner);
        var request = new
        {
            surveyId,
            language = "en",
            subject = new { de = "{organisation}: Ihre Meinung zählt", en = "{organisation}: your opinion counts" },
            body = new { de = "Guten Tag {name},\n\n{link}", en = "Hello {name},\n\n{link}" },
            answerInEmail = true,
        };

        var preview = await owner.PostAsJsonAsync(Url("/api/app/messages/invitation/preview"), request, Cancel);

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Contains("Start the survey", (await JsonAsync(preview)).GetProperty("html").GetString(), StringComparison.Ordinal);
    }
}
