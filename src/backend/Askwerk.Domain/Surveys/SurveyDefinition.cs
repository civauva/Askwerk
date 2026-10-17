// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json.Serialization;

namespace Askwerk.Domain.Surveys;

/// <summary>The full schema of one survey version (agents/data-model.md, Survey definition). Twin of packages/schema/src/definition.ts.</summary>
/// <param name="SchemaVersion">Version of this definition format.</param>
/// <param name="Languages">Languages the survey is offered in.</param>
/// <param name="DefaultLanguage">Language shown when a text is missing in the language a respondent chose.</param>
/// <param name="Pages">Pages in order.</param>
/// <param name="EndPage">The thank-you page.</param>
/// <param name="Layout">How the runner shows the questions (W8); classic when not given.</param>
/// <param name="Welcome">The welcome page before the first question (W8 rev. 2).</param>
/// <param name="HeaderImage">The survey's own header image, replacing the theme's (AWK-17, W10 B).</param>
public sealed record SurveyDefinition(
    int SchemaVersion,
    IReadOnlyList<string> Languages,
    string DefaultLanguage,
    IReadOnlyList<Page> Pages,
    EndPage? EndPage = null,
    SurveyLayout? Layout = null,
    WelcomePage? Welcome = null,
    SurveyImage? HeaderImage = null);

/// <summary>An uploaded image placed in a survey, with its alt text.</summary>
/// <param name="Asset">The uploaded file.</param>
/// <param name="Alt">What the image shows, per language; required.</param>
public sealed record SurveyImage(string Asset, TranslatedText? Alt = null);

/// <summary>How the runner shows the questions.</summary>
public enum SurveyLayout
{
    /// <summary>A page of questions at a time.</summary>
    Classic,

    /// <summary>One question at a time.</summary>
    OneAtATime,
}

/// <summary>The welcome page: shown before the first question, skipped when a respondent comes back.</summary>
/// <param name="Title">Title respondents see.</param>
/// <param name="Text">Introduction.</param>
/// <param name="Enabled">False switches the page off and keeps its texts (W9); shown when not given.</param>
public sealed record WelcomePage(TranslatedText? Title = null, TranslatedText? Text = null, bool? Enabled = null);

/// <summary>A page of questions.</summary>
/// <param name="Id">Stable id of the page within the survey.</param>
/// <param name="Questions">Questions in order.</param>
/// <param name="Title">Optional page title.</param>
/// <param name="ShowWhen">Shown only when these hold (AWK-31); always when null.</param>
/// <param name="After">Jump forward or end after this page (AWK-31); the next page when null.</param>
public sealed record Page(string Id, IReadOnlyList<Question> Questions, TranslatedText? Title = null, LogicGroup? ShowWhen = null, PageAfter? After = null);

/// <summary>One question.</summary>
/// <param name="Id">Stable id of the question; it survives renames of the variable.</param>
/// <param name="Variable">Variable name used in exports and logic, stable across versions (data-model.md, rule 2).</param>
/// <param name="Type">Question type.</param>
/// <param name="Text">Question text.</param>
/// <param name="Required">Whether an answer is required.</param>
/// <param name="Help">Optional help text below the question.</param>
/// <param name="Options">Answer options of choice types.</param>
/// <param name="Rows">Rows of a matrix.</param>
/// <param name="Scale">Scale of a rating question.</param>
/// <param name="Randomize">Show the options in a random order per respondent.</param>
/// <param name="DontKnow">Offer a "Don't know" answer.</param>
/// <param name="Display">Rating only: how the steps look (W7, panel 7).</param>
/// <param name="Order">Dropdown only: the order the options are offered in.</param>
/// <param name="Other">Single, multiple and dropdown: an extra last option with a text field.</param>
/// <param name="Yes">Yes/no: the yes answer; Yes/1 when not given.</param>
/// <param name="No">Yes/no: the no answer; No/0 when not given.</param>
/// <param name="Format">Short text: the format it is checked against.</param>
/// <param name="Placeholder">Short text: a hint inside the empty field.</param>
/// <param name="Mode">Date: date, date and time, or time only.</param>
/// <param name="Earliest">Date: earliest allowed value, written for the mode; dates may be "today".</param>
/// <param name="Latest">Date: latest allowed value, written for the mode; dates may be "today".</param>
/// <param name="MinuteStep">Date-time and time: minutes between offered times; divides 60.</param>
/// <param name="Image">Image blocks: the image; other questions: an image shown with the question.</param>
/// <param name="ShowWhen">Shown only when these hold (AWK-31); always when null. Conditions use earlier questions only.</param>
/// <param name="Key">A key question (W38): its result shows on the survey's card.</param>
public sealed record Question(
    string Id,
    string Variable,
    QuestionType Type,
    TranslatedText Text,
    bool Required,
    TranslatedText? Help = null,
    IReadOnlyList<AnswerOption>? Options = null,
    IReadOnlyList<AnswerOption>? Rows = null,
    RatingScale? Scale = null,
    bool? Randomize = null,
    bool? DontKnow = null,
    RatingDisplay? Display = null,
    OptionOrder? Order = null,
    OtherOption? Other = null,
    YesNoAnswer? Yes = null,
    YesNoAnswer? No = null,
    TextFormat? Format = null,
    TranslatedText? Placeholder = null,
    DateMode? Mode = null,
    string? Earliest = null,
    string? Latest = null,
    int? MinuteStep = null,
    QuestionImage? Image = null,
    LogicGroup? ShowWhen = null,
    bool? Key = null);

/// <summary>"Other, please specify": an extra last option whose text goes into its own variable.</summary>
/// <param name="Code">Option code, distinct from the other options.</param>
/// <param name="Label">Label, e.g. "Other, please specify:".</param>
/// <param name="Variable">Variable of the text, e.g. SOURCE_OTHER.</param>
public sealed record OtherOption(string Code, TranslatedText Label, string Variable);

/// <summary>One answer of a yes/no question.</summary>
/// <param name="Code">Stable code used in exports.</param>
/// <param name="Label">Visible label; the default Yes or No when not given.</param>
public sealed record YesNoAnswer(string Code, TranslatedText? Label = null);

/// <summary>How a rating's steps look; the stored value is the same.</summary>
public enum RatingDisplay
{
    /// <summary>Numbered buttons (default).</summary>
    Numbers,

    /// <summary>Stars.</summary>
    Stars,

    /// <summary>Dots growing with the value.</summary>
    Dots,

    /// <summary>Faces, for 3 or 5 steps.</summary>
    Faces,
}

/// <summary>The order a dropdown offers its options in.</summary>
public enum OptionOrder
{
    /// <summary>As entered.</summary>
    Entered,

    /// <summary>Alphabetical in the respondent's language.</summary>
    Alphabetical,
}

/// <summary>Formats a short text is checked against.</summary>
public enum TextFormat
{
    /// <summary>An email address.</summary>
    Email,

    /// <summary>A phone number.</summary>
    Phone,

    /// <summary>A web address.</summary>
    Url,
}

/// <summary>What a date question asks for.</summary>
public enum DateMode
{
    /// <summary>A date.</summary>
    Date,

    /// <summary>A date and a time.</summary>
    DateTime,

    /// <summary>A time only.</summary>
    Time,
}

/// <summary>An answer option or matrix row.</summary>
/// <param name="Code">Stable code used in exports.</param>
/// <param name="Label">Visible label.</param>
/// <param name="Image">Single and multiple choice: the option as a picture.</param>
public sealed record AnswerOption(string Code, TranslatedText Label, OptionImage? Image = null);

/// <summary>The scale of a rating question.</summary>
/// <param name="Min">Lowest value.</param>
/// <param name="Max">Highest value.</param>
/// <param name="MinLabel">Label at the lowest value.</param>
/// <param name="MaxLabel">Label at the highest value.</param>
/// <param name="Step">Slider only: the distance between values; it divides Max − Min.</param>
/// <param name="ShowValue">Slider only: show the value above the handle.</param>
public sealed record RatingScale(int Min, int Max, TranslatedText? MinLabel = null, TranslatedText? MaxLabel = null, int? Step = null, bool? ShowValue = null);

/// <summary>The thank-you page.</summary>
/// <param name="Text">Text shown after the last page.</param>
public sealed record EndPage(TranslatedText Text);

/// <summary>Question types offered by the designer.</summary>
public enum QuestionType
{
    /// <summary>One option.</summary>
    [JsonStringEnumMemberName("single")]
    SingleChoice,

    /// <summary>Several options.</summary>
    [JsonStringEnumMemberName("multiple")]
    MultipleChoice,

    /// <summary>One option from a long list, with search.</summary>
    Dropdown,

    /// <summary>Yes or no.</summary>
    YesNo,

    /// <summary>A value on a bar.</summary>
    Slider,

    /// <summary>A numeric scale.</summary>
    Rating,

    /// <summary>Net Promoter Score, 0 to 10.</summary>
    Nps,

    /// <summary>One line of text.</summary>
    ShortText,

    /// <summary>Several lines of text.</summary>
    LongText,

    /// <summary>A number.</summary>
    Number,

    /// <summary>A date.</summary>
    Date,

    /// <summary>Rows rated on the same options.</summary>
    Matrix,

    /// <summary>Options put in order.</summary>
    Ranking,

    /// <summary>Text that respondents read, such as a welcome; not answered.</summary>
    TextBlock,

    /// <summary>An image that respondents see, with an optional caption; not answered (AWK-17).</summary>
    Image,
}

/// <summary>An uploaded image in a survey (AWK-17, W10 F1).</summary>
/// <param name="Asset">The uploaded file.</param>
/// <param name="Alt">What it shows, per language; required.</param>
/// <param name="Size">Image blocks: how wide; full when not given.</param>
/// <param name="Align">Image blocks: where a narrower image sits; centre when not given.</param>
public sealed record QuestionImage(string Asset, TranslatedText? Alt = null, ImageWidth? Size = null, ImageAlign? Align = null);

/// <summary>How wide an image block is.</summary>
public enum ImageWidth
{
    /// <summary>The whole column.</summary>
    Full,

    /// <summary>Most of it.</summary>
    Wide,

    /// <summary>Half.</summary>
    Half,

    /// <summary>A small picture.</summary>
    Small,
}

/// <summary>Where a narrower image sits.</summary>
public enum ImageAlign
{
    /// <summary>Left.</summary>
    Left,

    /// <summary>Centre.</summary>
    Centre,

    /// <summary>Right.</summary>
    Right,
}

/// <summary>An option shown as a picture; its label is the alt text.</summary>
/// <param name="Asset">The uploaded file.</param>
public sealed record OptionImage(string Asset);
