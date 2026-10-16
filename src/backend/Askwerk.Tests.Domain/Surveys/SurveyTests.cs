// SPDX-License-Identifier: AGPL-3.0-only
using Askwerk.Domain.Identity;
using Askwerk.Domain.Surveys;

namespace Askwerk.Tests.Domain.Surveys;

public sealed class SurveyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 11, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.CreateVersion7(Now);
    private static readonly Guid Anna = Guid.CreateVersion7(Now);

    [Fact]
    public void Create_StartsAsDraftWithOneEmptyPageInItsLanguages()
    {
        var (survey, version) = Survey.Create(Guid.CreateVersion7(Now), Tenant, " Customer satisfaction 2026 ", ["de", "en"], "de", Anna, Now);

        Assert.Equal("Customer satisfaction 2026", survey.Title);
        Assert.Equal(SurveyStatus.Draft, survey.Status);
        Assert.Equal((1, SurveyVersionState.Draft), (version.Number, version.State));
        var definition = version.ReadDefinition();
        Assert.Equal(["de", "en"], definition.Languages);
        Assert.Equal("de", definition.DefaultLanguage);
        Assert.Empty(Assert.Single(definition.Pages).Questions);
    }

    [Theory]
    [InlineData(" ", "de", "title", "title-required")]
    [InlineData("Survey", "fr", "defaultLanguage", "default-language-not-offered")]
    public void Create_Invalid_NamesTheField(string title, string defaultLanguage, string field, string code)
    {
        var error = Assert.Throws<DomainValidationException>(() => Survey.Create(Guid.CreateVersion7(Now), Tenant, title, ["de", "en"], defaultLanguage, Anna, Now));

        Assert.Equal((field, code), (error.Field, error.Code));
    }

    [Fact]
    public void Create_GivesAShortCodeForLinks_WithoutLookAlikeCharacters()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => Survey.Create(Guid.CreateVersion7(Now), Tenant, "Survey", ["de"], "de", Anna, Now).Survey.Code).ToList();

        Assert.All(codes, code => Assert.Matches("^[23456789abcdefghjkmnpqrstuvwxyz]{8}$", code));
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void Create_NoLanguage_IsRefused()
    {
        var error = Assert.Throws<DomainValidationException>(() => Survey.Create(Guid.CreateVersion7(Now), Tenant, "Survey", [], "de", Anna, Now));

        Assert.Equal(("languages", "languages-required"), (error.Field, error.Code));
    }

    [Fact]
    public void Publish_FreezesTheDraftAndStartsTheNextOneWithTheSameDefinition()
    {
        var (survey, draft) = Survey.Create(Guid.CreateVersion7(Now), Tenant, "Survey", ["de"], "de", Anna, Now);

        var next = survey.Publish(draft, Guid.CreateVersion7(Now), Anna, Now.AddHours(1));

        Assert.Equal((SurveyVersionState.Published, Now.AddHours(1)), (draft.State, draft.PublishedAt));
        Assert.Equal((SurveyStatus.Published, 1), (survey.Status, survey.PublishedVersion));
        Assert.Equal((2, SurveyVersionState.Draft, draft.Definition), (next.Number, next.State, next.Definition));
    }

    [Fact]
    public void ReplaceDefinition_OnAPublishedVersion_IsRefused()
    {
        var (survey, draft) = Survey.Create(Guid.CreateVersion7(Now), Tenant, "Survey", ["de"], "de", Anna, Now);
        survey.Publish(draft, Guid.CreateVersion7(Now), Anna, Now);

        Assert.Throws<InvalidOperationException>(() => draft.ReplaceDefinition(draft.ReadDefinition(), Now));
    }
    private static Survey Published()
    {
        var (survey, draft) = Survey.Create(Guid.CreateVersion7(Now), Tenant, "Survey", ["de"], "de", Anna, Now);
        survey.Publish(draft, Guid.CreateVersion7(Now), Anna, Now);

        return survey;
    }

    [Fact]
    public void SetClosingDate_InTheFuture_KeepsCollectingUntilThen()
    {
        var survey = Published();

        survey.SetClosingDate(Now.AddDays(2), Now);

        Assert.Equal(Now.AddDays(2), survey.ClosesAt);
        Assert.True(survey.IsCollecting(Now.AddDays(2).AddMinutes(-1)));
        Assert.False(survey.IsCollecting(Now.AddDays(2)));
        Assert.Equal(SurveyStatus.Closed, survey.StatusAt(Now.AddDays(2)));
    }

    [Fact]
    public void SetClosingDate_InThePast_IsRefused()
    {
        var survey = Published();

        var error = Assert.Throws<DomainValidationException>(() => survey.SetClosingDate(Now, Now));

        Assert.Equal(("closesAt", "closing-date-past"), (error.Field, error.Code));
    }

    [Fact]
    public void SetClosingDate_Null_RemovesIt()
    {
        var survey = Published();
        survey.SetClosingDate(Now.AddDays(2), Now);

        survey.SetClosingDate(null, Now);

        Assert.Null(survey.ClosesAt);
        Assert.True(survey.IsCollecting(Now.AddYears(1)));
    }

    [Fact]
    public void CloseAtClosingDate_OnceItHasPassed_ClosesTheSurvey()
    {
        var survey = Published();
        survey.SetClosingDate(Now.AddDays(2), Now);

        var early = survey.CloseAtClosingDate(Now.AddDays(1));
        var due = survey.CloseAtClosingDate(Now.AddDays(2));

        Assert.Equal((false, true), (early, due));
        Assert.Equal(SurveyStatus.Closed, survey.Status);
    }

    [Fact]
    public void Reopen_AfterTheClosingDate_RemovesTheDate()
    {
        var survey = Published();
        survey.SetClosingDate(Now.AddDays(2), Now);
        survey.CloseAtClosingDate(Now.AddDays(2));

        survey.Reopen(Now.AddDays(3));

        Assert.Equal((SurveyStatus.Published, (DateTimeOffset?)null), (survey.Status, survey.ClosesAt));
    }

    [Fact]
    public void Reopen_ClosedByHandBeforeTheDate_KeepsTheDate()
    {
        var survey = Published();
        survey.SetClosingDate(Now.AddDays(2), Now);
        survey.Close(Now.AddDays(1));

        survey.Reopen(Now.AddDays(1));

        Assert.Equal(Now.AddDays(2), survey.ClosesAt);
    }

    [Fact]
    public void OneResponsePerPerson_IsOnByDefault_AndCanBeTurnedOff()
    {
        var survey = Published();
        var before = survey.OneResponsePerPerson;

        survey.AllowOneResponsePerPerson(false, Now);

        Assert.Equal((true, false), (before, survey.OneResponsePerPerson));
    }
}
