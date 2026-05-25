using System.Collections;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using TheArtOfDev.HtmlRenderer.Adapters;
using TheArtOfDev.HtmlRenderer.Avalonia;

namespace perinma.Views.Calendar.EventView;

// HtmlPanel uses FontsHandler._existingFontFamilies (populated from FontManager.SystemFonts)
// which stores FontFamily objects carrying a URI key ("fonts:SystemFonts#<name>").
// On Linux, AvaloniaAdapter.CreateFontInt(RFontFamily) constructs a Typeface from that keyed
// FontFamily, and TryGetGlyphTypeface fails for some weight/style combinations, causing a
// layout exception that leaves every box at height 0.
//
// Fix: clear _existingFontFamilies so all font creation falls through to the key-less
// CreateFontInt(string) overload, which constructs Typeface("name") with no URI key and
// resolves correctly. The side-effect — IsFontExists always returning false — is harmless
// here: the CSS engine returns "inherit" for any font-family declaration and Avalonia
// falls back to its default font.
internal sealed class SafeHtmlPanel : HtmlPanel
{
    public SafeHtmlPanel()
    {
        var avaloniaAdapter = typeof(HtmlContainer)
            .GetProperty("AvaloniaAdapter", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(_htmlContainer);

        if (avaloniaAdapter is not RAdapter rAdapter)
            return;

        var fontsHandler = typeof(RAdapter)
            .GetField("_fontsHandler", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(rAdapter);

        if (fontsHandler
                ?.GetType()
                .GetField("_existingFontFamilies", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(fontsHandler) is IDictionary existing)
        {
            existing.Clear();
        }

        // Map the Windows-only "Segoe UI" fallback to the system default so the
        // empty-font-family code path in CssBoxProperties.ActualFont resolves
        // without an extra fallback hop.
        rAdapter.AddFontFamilyMapping("Segoe UI", FontManager.Current.DefaultFontFamily.Name);
    }
}

public partial class RichTextView : UserControl
{
    public RichTextView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Re-trigger layout: HtmlPanel measures at height 0 before the flyout has
        // laid out this control (no valid constraint width yet). OnLoaded fires once
        // the visual tree is attached and a real width is available.
        var panel = this.FindControl<SafeHtmlPanel>("HtmlPanel");
        if (panel is not null
            && DataContext is RichTextViewModel { IsHtml: true, HtmlText: { } ht })
        {
            panel.Text = null;
            panel.Text = ht;
        }
    }
}