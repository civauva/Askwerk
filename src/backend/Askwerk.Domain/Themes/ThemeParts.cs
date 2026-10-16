// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Themes;

/// <summary>Finer settings of each part of a page (W26). Every field is optional; unset ones follow the main settings.</summary>
/// <param name="Question">Question texts.</param>
/// <param name="Choices">Single and multiple choice.</param>
/// <param name="Buttons">Back, Next and Send.</param>
/// <param name="TextFields">Text, number and date fields.</param>
/// <param name="Rating">Rating and scale steps.</param>
/// <param name="Slider">The slider.</param>
/// <param name="Matrix">The matrix.</param>
/// <param name="Progress">The progress indicator.</param>
/// <param name="Messages">Error messages.</param>
/// <param name="TextBlock">Text blocks between the questions (W28).</param>
public sealed record ThemeComponents(
    QuestionLook? Question = null,
    ChoicesLook? Choices = null,
    ButtonsLook? Buttons = null,
    TextFieldsLook? TextFields = null,
    RatingLook? Rating = null,
    SliderLook? Slider = null,
    MatrixLook? Matrix = null,
    ProgressLook? Progress = null,
    MessagesLook? Messages = null,
    TextBlockLook? TextBlock = null);

/// <summary>Text blocks between the questions (W28).</summary>
/// <param name="Style">On a lighter tint with an accent edge, or plain text.</param>
public sealed record TextBlockLook(TextBlockStyle? Style = null);

/// <summary>Question texts.</summary>
/// <param name="Size">Size of the question text.</param>
/// <param name="Font">Heading or body font.</param>
/// <param name="Numbered">Number the questions.</param>
public sealed record QuestionLook(ThemeScale? Size = null, LookFont? Font = null, bool? Numbered = null);

/// <summary>Single and multiple choice; the style is <see cref="ThemeTokens.Controls"/>.</summary>
/// <param name="Columns">One column, two, or as many as fit.</param>
/// <param name="Selected">How a chosen answer shows.</param>
/// <param name="Marker">Round or check marker, or none.</param>
/// <param name="Other">The "Other" field inside its option or below it.</param>
public sealed record ChoicesLook(ChoiceColumns? Columns = null, SelectedStyle? Selected = null, ChoiceMarker? Marker = null, OtherPlacement? Other = null);

/// <summary>Back, Next and Send; the style is <see cref="ThemeTokens.Buttons"/>.</summary>
/// <param name="Width">Natural, wide or full width.</param>
/// <param name="Align">Back left and Next right, both at the end, or centred.</param>
public sealed record ButtonsLook(ButtonWidth? Width = null, ButtonAlign? Align = null);

/// <summary>Text, number and date fields.</summary>
/// <param name="Style">Boxes, underlined or filled.</param>
public sealed record TextFieldsLook(FieldStyle? Style = null);

/// <summary>Rating and scale steps.</summary>
/// <param name="Size">Size of the steps.</param>
/// <param name="Selected">How the chosen step shows.</param>
public sealed record RatingLook(ThemeScale? Size = null, SelectedStyle? Selected = null);

/// <summary>The slider.</summary>
/// <param name="Track">Thin or thick track.</param>
/// <param name="Thumb">Round or square handle.</param>
public sealed record SliderLook(SliderTrack? Track = null, SliderThumb? Thumb = null);

/// <summary>The matrix.</summary>
/// <param name="Stripes">Every other row tinted.</param>
/// <param name="Header">Plain or tinted column heads.</param>
public sealed record MatrixLook(bool? Stripes = null, MatrixHeader? Header = null);

/// <summary>The progress indicator; the style is <see cref="ThemeTokens.Progress"/>.</summary>
/// <param name="Label">Show "Page 2 of 4".</param>
/// <param name="Position">Above the questions or below them.</param>
public sealed record ProgressLook(bool? Label = null, ProgressPosition? Position = null);

/// <summary>Error messages.</summary>
/// <param name="Error">The error colour, "#rrggbb".</param>
/// <param name="Style">A box or a bar at the side.</param>
public sealed record MessagesLook(string? Error = null, MessageStyle? Style = null);

/// <summary>Header, welcome, question and end pages (W26).</summary>
/// <param name="Header">The bar with logo and language.</param>
/// <param name="Welcome">The welcome page.</param>
/// <param name="Questions">Pages with questions.</param>
/// <param name="End">Thank-you, closed and other messages.</param>
/// <param name="Title">Where the survey's title and the page show on question pages (W28).</param>
public sealed record ThemePages(HeaderLook? Header = null, PageLook? Welcome = null, QuestionPagesLook? Questions = null, PageLook? End = null, TitlePlace? Title = null);

/// <summary>The bar with logo and language.</summary>
/// <param name="Show">Show the bar.</param>
/// <param name="Organisation">Show the organisation's name.</param>
/// <param name="LogoSize">Size of the logo.</param>
public sealed record HeaderLook(bool? Show = null, bool? Organisation = null, ThemeScale? LogoSize = null);

/// <summary>A page with a title and text.</summary>
/// <param name="Layout">Left, centred, or on an accent band.</param>
public sealed record PageLook(PageLayout? Layout = null);

/// <summary>Pages with questions.</summary>
/// <param name="Width">Narrow, normal or wide column.</param>
/// <param name="Cards">Each question on its own card.</param>
/// <param name="Panel">The content on a solid surface, or a frosted one over the background (W28).</param>
public sealed record QuestionPagesLook(PageWidth? Width = null, bool? Cards = null, PanelStyle? Panel = null);

/// <summary>A pattern drawn from the colours (W26).</summary>
/// <param name="Pattern">Which pattern.</param>
public sealed record ThemeDecoration(ThemePattern Pattern);

/// <summary>How invitation emails follow the look (W26).</summary>
/// <param name="Header">Logo, an accent band, or plain.</param>
public sealed record ThemeEmail(EmailHeader? Header = null);

/// <summary>How the PDF follows the look (W26).</summary>
/// <param name="Header">The look's header, or plain for printing.</param>
/// <param name="Numbered">Number the questions.</param>
public sealed record ThemePdf(PdfHeader? Header = null, bool? Numbered = null);

/// <summary>Heading or body font.</summary>
public enum LookFont
{
    /// <summary>The heading font.</summary>
    Heading,

    /// <summary>The body font.</summary>
    Body,
}

/// <summary>Columns of choices.</summary>
public enum ChoiceColumns
{
    /// <summary>One column.</summary>
    One,

    /// <summary>Two columns on wide screens.</summary>
    Two,

    /// <summary>As many as fit.</summary>
    Auto,
}

/// <summary>How a chosen answer shows.</summary>
public enum SelectedStyle
{
    /// <summary>Accent border and tint.</summary>
    Accent,

    /// <summary>Text-coloured border.</summary>
    Text,

    /// <summary>Filled with the accent.</summary>
    Fill,
}

/// <summary>The marker of a choice.</summary>
public enum ChoiceMarker
{
    /// <summary>Round (single) and square (multiple) as usual.</summary>
    Round,

    /// <summary>A check mark.</summary>
    Check,

    /// <summary>No marker; the option itself shows the choice.</summary>
    None,
}

/// <summary>Where the "Other" field sits.</summary>
public enum OtherPlacement
{
    /// <summary>Inside its option.</summary>
    Inside,

    /// <summary>Below its option, full width.</summary>
    Below,
}

/// <summary>Button width.</summary>
public enum ButtonWidth
{
    /// <summary>As wide as the text.</summary>
    Auto,

    /// <summary>Wide.</summary>
    Wide,

    /// <summary>Full width.</summary>
    Full,
}

/// <summary>Where the buttons sit.</summary>
public enum ButtonAlign
{
    /// <summary>Back left, Next right.</summary>
    Split,

    /// <summary>Both at the end.</summary>
    End,

    /// <summary>Centred.</summary>
    Centre,
}

/// <summary>Look of text fields.</summary>
public enum FieldStyle
{
    /// <summary>Boxes.</summary>
    Boxes,

    /// <summary>A line underneath.</summary>
    Underline,

    /// <summary>A tinted fill.</summary>
    Filled,
}

/// <summary>Slider track.</summary>
public enum SliderTrack
{
    /// <summary>Thin.</summary>
    Thin,

    /// <summary>Thick.</summary>
    Thick,
}

/// <summary>Slider handle.</summary>
public enum SliderThumb
{
    /// <summary>Round.</summary>
    Round,

    /// <summary>Square.</summary>
    Square,
}

/// <summary>Matrix column heads.</summary>
public enum MatrixHeader
{
    /// <summary>Plain.</summary>
    Plain,

    /// <summary>Tinted.</summary>
    Tinted,
}

/// <summary>Where the progress shows.</summary>
public enum ProgressPosition
{
    /// <summary>Above the questions.</summary>
    Top,

    /// <summary>Below the questions.</summary>
    Bottom,
}

/// <summary>How error messages show.</summary>
public enum MessageStyle
{
    /// <summary>A box.</summary>
    Box,

    /// <summary>A bar at the side.</summary>
    Bar,
}

/// <summary>Layout of a page with a title and text.</summary>
public enum PageLayout
{
    /// <summary>Left-aligned.</summary>
    Left,

    /// <summary>Centred.</summary>
    Centre,

    /// <summary>On an accent band.</summary>
    Band,
}

/// <summary>Width of the question column.</summary>
public enum PageWidth
{
    /// <summary>Narrow.</summary>
    Narrow,

    /// <summary>Normal.</summary>
    Normal,

    /// <summary>Wide.</summary>
    Wide,
}

/// <summary>A pattern drawn from the colours.</summary>
public enum ThemePattern
{
    /// <summary>No pattern.</summary>
    None,

    /// <summary>Coloured dots of several sizes.</summary>
    Confetti,

    /// <summary>A dot grid.</summary>
    Dots,

    /// <summary>A line grid.</summary>
    Grid,

    /// <summary>Soft waves.</summary>
    Waves,

    /// <summary>Rolling hills under a sky.</summary>
    Hills,

    /// <summary>Diagonal stripes.</summary>
    Stripes,

    /// <summary>A mountain landscape at dawn behind the whole page (W28).</summary>
    Mountains,
}

/// <summary>Text blocks (W28).</summary>
public enum TextBlockStyle
{
    /// <summary>On a lighter tint with an accent edge.</summary>
    Tinted,

    /// <summary>Plain text.</summary>
    Plain,
}

/// <summary>Where the content sits (W28).</summary>
public enum PanelStyle
{
    /// <summary>On the surface colour.</summary>
    Solid,

    /// <summary>On the surface at 90 % over the background, blurred.</summary>
    Frosted,
}

/// <summary>Where the survey's title shows on question pages (W28).</summary>
public enum TitlePlace
{
    /// <summary>Inside the panel, as the progress label.</summary>
    Panel,

    /// <summary>Above the panel, over the background picture.</summary>
    OverPicture,
}

/// <summary>Light or dark.</summary>
public enum ThemeMode
{
    /// <summary>Always the colours.</summary>
    Light,

    /// <summary>Always the dark colours.</summary>
    Dark,

    /// <summary>The device's choice.</summary>
    Auto,
}

/// <summary>Header of invitation emails.</summary>
public enum EmailHeader
{
    /// <summary>The logo.</summary>
    Logo,

    /// <summary>An accent band with the logo.</summary>
    Band,

    /// <summary>Plain.</summary>
    Plain,
}

/// <summary>Header of the PDF.</summary>
public enum PdfHeader
{
    /// <summary>Logo and header image as on the web.</summary>
    Full,

    /// <summary>Plain, for printing.</summary>
    Plain,
}
