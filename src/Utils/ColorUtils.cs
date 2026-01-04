using System;
using Avalonia.Media;

namespace perinma.ViewModels;

public class ColorUtils
{
    public static double Luminance(Color color)
    {
        return (color.R * 0.299 + color.G * 0.587 + color.B * 0.114) / 0xFF;
    }

    public static Color TextColorOnDark { get; } = new(0xFF, 0xEE, 0xEE, 0xEE);
    public static Color TextColorOnBright { get;  } = new(0xFF, 0x33, 0x33, 0x33);

    private static double _textColorOnDarkLum = Luminance(TextColorOnDark);
    private static double _textColorOnBrightLum = Luminance(TextColorOnBright);

    public static Color ContrastTextColor(Color reference)
    {
        var referenceLum = Luminance(reference);
        var brightDiff = Math.Abs(_textColorOnBrightLum - referenceLum); 
        var darkDiff = Math.Abs(_textColorOnDarkLum - referenceLum); 
        
        if (brightDiff > darkDiff) return TextColorOnBright;
        return TextColorOnDark;
    }
}