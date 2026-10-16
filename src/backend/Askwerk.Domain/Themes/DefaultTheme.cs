// SPDX-License-Identifier: AGPL-3.0-only
namespace Askwerk.Domain.Themes;

/// <summary>
/// The look of surveys in an organisation without any theme: the Werkstatt preset. A test keeps it equal to
/// packages/schema/presets/werkstatt.json.
/// </summary>
public static class DefaultTheme
{
    /// <summary>The Werkstatt tokens.</summary>
    public static ThemeTokens Werkstatt { get; } = new(
        SchemaVersion: 1,
        Colours: new ThemeColours(Accent: "#d2440f", OnAccent: "#ffffff", Background: "#f6f5f2", Surface: "#ffffff", Text: "#1a1917", Muted: "#6e6962", Line: "#c8c2b8"),
        Fonts: new ThemeFonts(new ThemeFont("IBM Plex Sans Condensed"), new ThemeFont("IBM Plex Sans")),
        Scale: ThemeScale.M,
        Spacing: ThemeSpacing.Normal,
        Corners: ThemeCorners.Sharp,
        Buttons: ThemeButtons.Filled,
        Controls: ThemeControls.Boxes,
        Progress: ThemeProgress.Ruler,
        Layout: ThemeLayout.Card);

    /// <summary>Every uploaded file a theme points to: logo, header and background images, uploaded fonts.</summary>
    /// <param name="tokens">The theme.</param>
    /// <returns>Asset ids as written in the tokens.</returns>
    public static IEnumerable<string> AssetsOf(ThemeTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        return new[] { tokens.Logo?.Asset, tokens.HeaderImage?.Asset, tokens.Background?.Image?.Asset, tokens.Fonts.Heading.Asset, tokens.Fonts.Body.Asset }
            .OfType<string>()
            .Where(asset => asset.Length > 0);
    }
}
