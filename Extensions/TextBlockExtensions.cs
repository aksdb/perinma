using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace perinma.Extensions;


public class TextBlockExtensions : AvaloniaObject
{
    public static readonly AttachedProperty<bool> IsTextTrimmedProperty =
        AvaloniaProperty.RegisterAttached<TextBlockExtensions, TextBlock, bool>("IsTextTrimmed", defaultValue: false);

    static TextBlockExtensions()
    {
        IsTextTrimmedProperty.Changed.AddClassHandler<TextBlock>((textBlock, e) =>
        {
            if (textBlock != null)
            {
                textBlock.LayoutUpdated += OnTextBlockLayoutUpdated;
                UpdateTrimmedState(textBlock);
            }
        });
    }

    private static void OnTextBlockLayoutUpdated(object sender, EventArgs e)
    {
        if (sender is TextBlock textBlock)
        {
            UpdateTrimmedState(textBlock);
        }
    }

    private static void UpdateTrimmedState(TextBlock textBlock)
    {
        bool isTrimmed = false;

        if (textBlock.TextTrimming != TextTrimming.None && textBlock.TextLayout != null)
        {
            isTrimmed = textBlock.TextLayout.TextLines.Any(line => line.HasCollapsed);
        }

        textBlock.SetValue(IsTextTrimmedProperty, isTrimmed);
    }

    public static bool GetIsTextTrimmed(TextBlock textBlock)
    {
        return textBlock.GetValue(IsTextTrimmedProperty);
    }
}