// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.RegularExpressions;
using Askwerk.Domain.Surveys;

namespace Askwerk.Domain.Themes;

/// <summary>One finding of the theme checks.</summary>
/// <param name="Code">Stable code, e.g. "contrast-too-low".</param>
/// <param name="Severity">Errors keep a theme from being saved.</param>
/// <param name="Path">Where, e.g. "colours/text".</param>
/// <param name="Against">Contrast issues: the colour it is read against.</param>
/// <param name="Ratio">Contrast issues: the ratio found, to two decimals.</param>
public sealed record ThemeIssue(string Code, IssueSeverity Severity, string Path, string? Against = null, double? Ratio = null);

/// <summary>
/// Checks a theme before it is saved (AWK-17, W10 note 2): contrast after WCAG 2.2, colours, fonts and images.
/// Twin of packages/schema/src/theme-checks.ts; tests/corpus/theme-checks keeps both in step.
/// </summary>
public static partial class ThemeChecks
{
    private static readonly int[] ChannelStarts = [1, 3, 5];

    private static readonly (string Foreground, string Background, double Minimum)[] Pairs =
    [
        ("text", "surface", 4.5),
        ("text", "background", 4.5),
        ("muted", "surface", 4.5),
        ("onAccent", "accent", 4.5),
        ("accent", "surface", 3), // non-text parts, WCAG 1.4.11
    ];

    /// <summary>Everything that keeps a theme from being saved (errors) or deserves a look (warnings), in a fixed order.</summary>
    /// <param name="tokens">The theme.</param>
    /// <returns>The issues; empty when the theme is fine.</returns>
    public static IReadOnlyList<ThemeIssue> Check(ThemeTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var issues = new List<ThemeIssue>();
        var valid = Colours(tokens.Colours, "colours", issues);

        foreach (var (name, font) in new[] { ("heading", tokens.Fonts.Heading), ("body", tokens.Fonts.Body) })
        {
            if (string.IsNullOrWhiteSpace(font.Family))
            {
                issues.Add(new ThemeIssue("font-missing", IssueSeverity.Error, $"fonts/{name}"));
            }
        }

        Contrast(valid, "colours", issues);

        // Dark mode (W26): the dark colours meet the same pairs, and dark or auto needs them.
        if (tokens.DarkColours is { } dark)
        {
            Contrast(Colours(dark, "darkColours", issues), "darkColours", issues);
        }
        else if (tokens.Mode is ThemeMode.Dark or ThemeMode.Auto)
        {
            issues.Add(new ThemeIssue("dark-colours-missing", IssueSeverity.Error, "darkColours"));
        }

        // The error colour (W26) is text on the surface.
        if (tokens.Components?.Messages?.Error is { } error)
        {
            if (!Hex().IsMatch(error))
            {
                issues.Add(new ThemeIssue("colour-invalid", IssueSeverity.Error, "components/messages/error"));
            }
            else if (valid.TryGetValue("surface", out var surface) && Round2(ContrastRatio(Rgb(error), surface)) is var ratio && ratio < 4.5)
            {
                issues.Add(new ThemeIssue("contrast-too-low", IssueSeverity.Error, "components/messages/error", "colours/surface", ratio));
            }
        }

        if (tokens.Logo is { } logo)
        {
            CheckImage(logo, "logo", issues);
        }

        if (tokens.HeaderImage is { } header)
        {
            CheckImage(header, "headerImage", issues);
        }

        if (tokens.Background is { } background2)
        {
            CheckBackground(background2, valid.GetValueOrDefault("text"), issues);
        }

        return issues;
    }

    /// <summary>The WCAG contrast ratio of two colours given as 0–1 channels.</summary>
    /// <param name="first">One colour.</param>
    /// <param name="second">The other.</param>
    /// <returns>1 to 21.</returns>
    public static double ContrastRatio(double[] first, double[] second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var a = Luminance(first);
        var b = Luminance(second);

        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static Dictionary<string, double[]> Colours(ThemeColours colours, string path, List<ThemeIssue> issues)
    {
        var named = new (string Name, string? Value)[]
        {
            ("accent", colours.Accent), ("onAccent", colours.OnAccent), ("background", colours.Background), ("surface", colours.Surface),
            ("text", colours.Text), ("muted", colours.Muted), ("line", colours.Line),
        };
        var valid = new Dictionary<string, double[]>();

        foreach (var (name, value) in named)
        {
            if (value is null || !Hex().IsMatch(value))
            {
                issues.Add(new ThemeIssue("colour-invalid", IssueSeverity.Error, $"{path}/{name}"));
            }
            else
            {
                valid[name] = Rgb(value);
            }
        }

        return valid;
    }

    private static void Contrast(Dictionary<string, double[]> valid, string path, List<ThemeIssue> issues)
    {
        foreach (var (foreground, background, minimum) in Pairs)
        {
            if (valid.TryGetValue(foreground, out var front) && valid.TryGetValue(background, out var back))
            {
                var ratio = Round2(ContrastRatio(front, back));

                if (ratio < minimum)
                {
                    issues.Add(new ThemeIssue("contrast-too-low", IssueSeverity.Error, $"{path}/{foreground}", $"{path}/{background}", ratio));
                }
            }
        }
    }

    private static void CheckBackground(ThemeBackground background, double[]? text, List<ThemeIssue> issues)
    {
        if (background.Kind is BackgroundKind.Image or BackgroundKind.ImageOverlay)
        {
            if (background.Image is null)
            {
                issues.Add(new ThemeIssue("background-image-missing", IssueSeverity.Error, "background/image"));
            }
            else
            {
                CheckImage(background.Image, "background/image", issues);
            }
        }

        if (background.Kind == BackgroundKind.Image)
        {
            issues.Add(new ThemeIssue("contrast-not-checked", IssueSeverity.Warning, "background/image"));
        }

        if (background.Kind != BackgroundKind.ImageOverlay)
        {
            return;
        }

        var opacity = background.OverlayOpacity ?? 1;

        if (!(opacity >= 0 && opacity <= 1))
        {
            issues.Add(new ThemeIssue("opacity-out-of-range", IssueSeverity.Error, "background/overlayOpacity"));
        }
        else if (background.Overlay is null || !Hex().IsMatch(background.Overlay))
        {
            issues.Add(new ThemeIssue("colour-invalid", IssueSeverity.Error, "background/overlay"));
        }
        else if (text is not null)
        {
            // The image is unknown, so the overlay is judged over the worst case: a black and a white image.
            var overlay = Rgb(background.Overlay);
            double[] Over(double image) => [.. overlay.Select(channel => (opacity * channel) + ((1 - opacity) * image))];
            var ratio = Round2(Math.Min(ContrastRatio(text, Over(0)), ContrastRatio(text, Over(1))));

            if (ratio < 4.5)
            {
                issues.Add(new ThemeIssue("contrast-too-low", IssueSeverity.Error, "background/overlay", "colours/text", ratio));
            }
        }
    }

    private static void CheckImage(ThemeImage image, string path, List<ThemeIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(image.Asset))
        {
            issues.Add(new ThemeIssue("asset-missing", IssueSeverity.Error, $"{path}/asset"));
        }

        foreach (var (axis, value) in new[] { ("focalX", image.FocalX), ("focalY", image.FocalY) })
        {
            if (value is { } given && !(given >= 0 && given <= 1))
            {
                issues.Add(new ThemeIssue("focal-out-of-range", IssueSeverity.Error, $"{path}/{axis}"));
            }
        }
    }

    private static double[] Rgb(string colour) =>
        [.. ChannelStarts.Select(start => Convert.ToInt32(colour.Substring(start, 2), 16) / 255.0)];

    private static double Luminance(double[] channels)
    {
        static double Linear(double channel) => channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

        return (0.2126 * Linear(channels[0])) + (0.7152 * Linear(channels[1])) + (0.0722 * Linear(channels[2]));
    }

    private static double Round2(double value) => Math.Floor((value * 100) + 0.5) / 100;

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex Hex();
}
