using System;
using AtomUI.Theme.Palette;
using AtomUI.Theme.Styling;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace perinma.Views.Calendar.EventView;

public partial class ParticipationView : UserControl
{
    public ParticipationView()
    {
        InitializeComponent();
        ActualThemeVariantChanged += OnThemeVariantChanged;
        UpdatePaletteBrushes();
    }

    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        UpdatePaletteBrushes();
    }

    private void UpdatePaletteBrushes()
    {
        var isDark = IsDarkThemeVariant(ActualThemeVariant);
        SetPaletteBrushes("ParticipationAccept", PresetPrimaryColor.Green, isDark);
        SetPaletteBrushes("ParticipationDecline", PresetPrimaryColor.Red, isDark);
        SetPaletteBrushes("ParticipationTentative", PresetPrimaryColor.Gold, isDark);
    }

    private void SetPaletteBrushes(string resourcePrefix, PresetPrimaryColor presetColor, bool isDark)
    {
        var palette = PresetPalettes.GetPresetPalette(presetColor, isDark);
        var colors = ColorMap.FromColors(palette.ColorSequence);

        Resources[$"{resourcePrefix}Brush"] = new SolidColorBrush(colors.Color6);
        Resources[$"{resourcePrefix}HoverBrush"] = new SolidColorBrush(colors.Color1);
        Resources[$"{resourcePrefix}ActiveForegroundBrush"] =
            new SolidColorBrush(isDark ? colors.Color1 : colors.Color10);
    }

    private static bool IsDarkThemeVariant(ThemeVariant themeVariant)
    {
        for (var current = themeVariant; current != null; current = current.InheritVariant)
        {
            if (current == ThemeVariant.Dark)
                return true;
        }

        return false;
    }
}
