using System;
using System.Text.RegularExpressions;
using Avalonia.Media;

namespace perinma.Utils;

public static partial class ColorUtils
{
    [GeneratedRegex(@"^#[0-9A-Fa-f]{8}$")]
    private static partial Regex HexColorWithAlphaRegex();

    [GeneratedRegex(@"^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColorRegex();

    [GeneratedRegex(@"^#[0-9A-Fa-f]{3}$")]
    private static partial Regex ShortHexColorRegex();

    /// <summary>
    /// Normalizes a color string to the standard #RRGGBB format.
    /// Handles various formats returned by CalDAV servers:
    /// - #RRGGBBAA (with alpha) -> #RRGGBB
    /// - #RRGGBB -> #RRGGBB (unchanged)
    /// - #RGB -> #RRGGBB (expanded)
    /// - RRGGBB (without hash) -> #RRGGBB
    /// - Invalid formats -> null
    /// </summary>
    public static string? NormalizeHexColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return null;

        // Remove any leading/trailing whitespace
        color = color.Trim();

        // Add hash if missing
        if (!color.StartsWith('#'))
            color = "#" + color;

        // Match #RRGGBBAA (8 hex chars after #) - strip alpha channel
        if (HexColorWithAlphaRegex().IsMatch(color))
            return color[..7].ToUpperInvariant();

        // Match #RRGGBB (6 hex chars after #) - already correct format
        if (HexColorRegex().IsMatch(color))
            return color.ToUpperInvariant();

        // Match #RGB (3 hex chars) - expand to #RRGGBB
        if (ShortHexColorRegex().IsMatch(color))
        {
            var r = color[1];
            var g = color[2];
            var b = color[3];
            return $"#{r}{r}{g}{g}{b}{b}".ToUpperInvariant();
        }

        // Invalid format - return null
        return null;
    }

    public static double Luminance(Color color)
    {
        return (color.R * 0.299 + color.G * 0.587 + color.B * 0.114) / 0xFF;
    }

    public static Color TextColorOnDark { get; } = new(0xFF, 0xEE, 0xEE, 0xEE);
    public static Color TextColorOnBright { get; } = new(0xFF, 0x33, 0x33, 0x33);

    private static readonly double _textColorOnDarkLum = Luminance(TextColorOnDark);
    private static readonly double _textColorOnBrightLum = Luminance(TextColorOnBright);

    public static Color ContrastTextColor(Color reference)
    {
        var referenceLum = Luminance(reference);
        var brightDiff = Math.Abs(_textColorOnBrightLum - referenceLum);
        var darkDiff = Math.Abs(_textColorOnDarkLum - referenceLum);

        if (brightDiff > darkDiff) return TextColorOnBright;
        return TextColorOnDark;
    }
}
