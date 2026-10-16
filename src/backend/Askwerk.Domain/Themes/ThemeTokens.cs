// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Themes;

/// <summary>
/// A survey's look as data (AWK-17, W10 rev. 2): the runner, the email renderer and the PDF renderer each map the same
/// tokens. Twin of packages/schema/src/theme.ts; JSON uses camelCase and kebab-case values.
/// </summary>
/// <param name="SchemaVersion">Version of this format.</param>
/// <param name="Colours">The colours.</param>
/// <param name="Fonts">Heading and body fonts.</param>
/// <param name="Scale">Type scale.</param>
/// <param name="Spacing">Space between and inside elements.</param>
/// <param name="Corners">Corner style.</param>
/// <param name="Buttons">Button style.</param>
/// <param name="Controls">Look of the answer controls.</param>
/// <param name="Progress">Look of the progress indicator.</param>
/// <param name="Layout">Where logo, header image and content go.</param>
/// <param name="Logo">The logo.</param>
/// <param name="HeaderImage">The header image.</param>
/// <param name="Background">What lies behind the content.</param>
/// <param name="Components">Finer settings of each part of a page (W26); unset parts follow the settings above.</param>
/// <param name="Pages">Header, welcome, question and end pages (W26).</param>
/// <param name="Decoration">A pattern drawn from the colours, no file needed (W26).</param>
/// <param name="Mode">Light, dark, or the device's choice (W26); dark uses <paramref name="DarkColours"/>.</param>
/// <param name="DarkColours">The colours in dark mode (W26).</param>
/// <param name="Email">How invitation emails follow the look (W26).</param>
/// <param name="Pdf">How the PDF follows the look (W26).</param>
public sealed record ThemeTokens(
    int SchemaVersion,
    ThemeColours Colours,
    ThemeFonts Fonts,
    ThemeScale Scale,
    ThemeSpacing Spacing,
    ThemeCorners Corners,
    ThemeButtons Buttons,
    ThemeControls Controls,
    ThemeProgress Progress,
    ThemeLayout Layout,
    ThemeImage? Logo = null,
    ThemeImage? HeaderImage = null,
    ThemeBackground? Background = null,
    ThemeComponents? Components = null,
    ThemePages? Pages = null,
    ThemeDecoration? Decoration = null,
    ThemeMode? Mode = null,
    ThemeColours? DarkColours = null,
    ThemeEmail? Email = null,
    ThemePdf? Pdf = null);

/// <summary>Colours as "#rrggbb".</summary>
/// <param name="Accent">The one colour that marks what matters.</param>
/// <param name="OnAccent">Text and icons on the accent colour.</param>
/// <param name="Background">Behind the content.</param>
/// <param name="Surface">Cards and answer controls.</param>
/// <param name="Text">Body text.</param>
/// <param name="Muted">Secondary text.</param>
/// <param name="Line">Borders and rules.</param>
public sealed record ThemeColours(string? Accent, string? OnAccent, string? Background, string? Surface, string? Text, string? Muted, string? Line);

/// <summary>Heading and body fonts.</summary>
/// <param name="Heading">Headlines and questions.</param>
/// <param name="Body">Reading text and controls.</param>
public sealed record ThemeFonts(ThemeFont Heading, ThemeFont Body);

/// <summary>One of the self-hosted families, or a font file the organisation uploaded.</summary>
/// <param name="Family">Family name.</param>
/// <param name="Asset">The uploaded file, if any.</param>
public sealed record ThemeFont(string? Family, string? Asset = null);

/// <summary>An uploaded image; the focal point (0–1) stays visible when the image is cropped.</summary>
/// <param name="Asset">The uploaded file.</param>
/// <param name="FocalX">From the left, 0–1.</param>
/// <param name="FocalY">From the top, 0–1.</param>
public sealed record ThemeImage(string? Asset, double? FocalX = null, double? FocalY = null);

/// <summary>Behind the content.</summary>
/// <param name="Kind">Colour, image, or image under a colour overlay.</param>
/// <param name="Image">The image.</param>
/// <param name="Fit">How the image fills the area.</param>
/// <param name="Overlay">Overlay colour.</param>
/// <param name="OverlayOpacity">Overlay opacity, 0–1.</param>
public sealed record ThemeBackground(BackgroundKind Kind, ThemeImage? Image = null, BackgroundFit? Fit = null, string? Overlay = null, double? OverlayOpacity = null);

/// <summary>What the background is.</summary>
public enum BackgroundKind
{
    /// <summary>The background colour only.</summary>
    Colour,

    /// <summary>An image.</summary>
    Image,

    /// <summary>An image under a colour overlay.</summary>
    ImageOverlay,
}

/// <summary>How a background image fills the area.</summary>
public enum BackgroundFit
{
    /// <summary>Fills the area, cropped around the focal point.</summary>
    Cover,

    /// <summary>Whole image visible.</summary>
    Contain,

    /// <summary>Repeated.</summary>
    Tile,
}

/// <summary>Type scale.</summary>
public enum ThemeScale
{
    /// <summary>Small.</summary>
    S,

    /// <summary>Medium.</summary>
    M,

    /// <summary>Large.</summary>
    L,
}

/// <summary>Spacing.</summary>
public enum ThemeSpacing
{
    /// <summary>Tight.</summary>
    Compact,

    /// <summary>Default.</summary>
    Normal,

    /// <summary>Generous.</summary>
    Airy,
}

/// <summary>Corner style.</summary>
public enum ThemeCorners
{
    /// <summary>Square.</summary>
    Sharp,

    /// <summary>Slightly rounded.</summary>
    Rounded,

    /// <summary>Fully rounded controls.</summary>
    Pill,
}

/// <summary>Button style.</summary>
public enum ThemeButtons
{
    /// <summary>Filled with the accent colour.</summary>
    Filled,

    /// <summary>Accent outline.</summary>
    Outline,

    /// <summary>A soft accent tint with dark text (W26).</summary>
    Soft,
}

/// <summary>Look of the answer controls.</summary>
public enum ThemeControls
{
    /// <summary>Boxes.</summary>
    Boxes,

    /// <summary>Rounded pills.</summary>
    Pills,

    /// <summary>Underlined fields.</summary>
    Underline,

    /// <summary>Large tiles side by side (W26).</summary>
    Tiles,
}

/// <summary>Progress indicator.</summary>
public enum ThemeProgress
{
    /// <summary>The measuring rule.</summary>
    Ruler,

    /// <summary>A plain bar.</summary>
    Bar,

    /// <summary>Numbered steps.</summary>
    Steps,

    /// <summary>Hidden.</summary>
    None,
}

/// <summary>Layout preset.</summary>
public enum ThemeLayout
{
    /// <summary>Content on a card.</summary>
    Card,

    /// <summary>A header image above the content.</summary>
    HeaderImage,

    /// <summary>An image beside the content on wide screens, above it on phones.</summary>
    SplitImage,

    /// <summary>Text only, no card.</summary>
    Minimal,
}

/// <summary>A preset theme from packages/schema/presets.</summary>
/// <param name="Id">Stable id, e.g. "werkstatt".</param>
/// <param name="Tokens">The tokens.</param>
public sealed record ThemePreset(string Id, ThemeTokens Tokens);
