using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Lucdem.Avalonia.SourceGenerators.Attributes;
using perinma.Models;
using perinma.ViewModels;

namespace perinma.Views.Calendar;

public partial class EventItem : TemplatedControl
{
    [AvaStyledProperty]
    private string _title = "[no title]";

    [AvaStyledProperty]
    private string _startTimeText = string.Empty;

    [AvaStyledProperty]
    private string _endTimeText = string.Empty;

    public int StartSlot { get; set; }
    public int EndSlot { get; set; } // inclusive end-slot index
    public int DaySlot { get; set; }

    [AvaStyledProperty]
    private Color _color = Color.FromArgb(0x99, 0x33, 0x99, 0xFF);

    [AvaStyledProperty]
    private IBrush _backgroundBrush;

    [AvaStyledProperty]
    private IBrush _foregroundBrush;

    public int TieBreaker { get; set; }
    public bool IsFullDay { get; set; }

    // Additional fields for column assignment
    public int ColumnSlot { get; set; }
    public int TotalColumns { get; set; } = 1;
    public List<EventItem> CompetingWidgets { get; } = [];

    [AvaStyledProperty]
    private string _inlineTimeText = string.Empty;

    [AvaStyledProperty]
    private Rect? _availableBounds;

    [AvaStyledProperty]
    private bool _showInlineTimes = true;
    
    [AvaStyledProperty]
    private bool _showStackedTimes = false;

    private double _inlineTimeTextWidth;
    
    [AvaStyledProperty]
    private CalendarEvent _calendarEvent;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        
        switch (change.Property.Name)
        {
            case nameof(AvailableBounds):
                ShowInlineTimes = AvailableBounds?.Width > _inlineTimeTextWidth;
                ShowStackedTimes = !ShowInlineTimes;
                break;
            case nameof(StartTimeText):
            case nameof(EndTimeText):
                InlineTimeText = $"🕐 {StartTimeText}-{EndTimeText}";
                break;
            case nameof(Color):
                BackgroundBrush = new SolidColorBrush(Color, 0.8);
                ForegroundBrush = new SolidColorBrush(ColorUtils.ContrastTextColor(Color));
                break;
            case nameof(InlineTimeText):
            case nameof(FontFamily):
            case nameof(FontSize):
            case nameof(FontStretch):
            case nameof(FontStyle):
            case nameof(FontWeight):
                RecalculateInlineTimeWidth();
                break;
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        var border = e.NameScope.Find<Border>("Border");

        CancellationTokenSource? singleTapCtx = null;
        border.Tapped += async (sender, args) =>
        {
            singleTapCtx?.Cancel();
            singleTapCtx = new CancellationTokenSource();
            try
            {
                await Task.Delay(150, singleTapCtx.Token);
                FlyoutBase.ShowAttachedFlyout(border);
            } catch (TaskCanceledException) { }
        };
        border.DoubleTapped += (sender, args) =>
        {
            singleTapCtx?.Cancel();
            Console.Out.WriteLine("Double-tapped");
        };
    }

    private void RecalculateInlineTimeWidth()
    {
        var text = InlineTimeText;
        if (string.IsNullOrWhiteSpace(text))
        {
            _inlineTimeTextWidth = 0;
            return;
        }

        // Measure the text using current font properties
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var layout = new TextLayout(
            text,
            typeface,
            FontSize,
            ForegroundBrush);

        _inlineTimeTextWidth = layout.Width;
    }
}