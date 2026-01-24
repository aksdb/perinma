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
using perinma.Services;
using perinma.Storage;
using perinma.Utils;

namespace perinma.Views.Calendar;

public partial class EventItem : TemplatedControl
{
    // Disable false-positive warnings in relation to the code generator.
#pragma warning disable CS0169 // Field is never used
#pragma warning disable CS0414 // Field is assigned but never used
    
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
    private IBrush _backgroundBrush = Brushes.Transparent;

    [AvaStyledProperty]
    private IBrush _foregroundBrush = Brushes.Black;
    
    [AvaStyledProperty]
    private IBrush _borderBrush = Brushes.Transparent;
    
    /// <summary>
    /// Indicates whether this event needs a response from the user (not yet accepted).
    /// </summary>
    [AvaStyledProperty]
    private bool _needsResponse = false;

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
    private CalendarEvent? _calendarEvent;

    [AvaStyledProperty]
    private SqliteStorage? _storage;

    [AvaStyledProperty]
    private IReadOnlyDictionary<AccountType, ICalendarProvider>? _providers;

    [AvaStyledProperty]
    private IRespondableEventViewModel? _eventViewModel;

#pragma warning restore CS0169
#pragma warning restore CS0414

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
            case nameof(NeedsResponse):
                UpdateBrushes();
                break;
            case nameof(InlineTimeText):
            case nameof(FontFamily):
            case nameof(FontSize):
            case nameof(FontStretch):
            case nameof(FontStyle):
            case nameof(FontWeight):
                RecalculateInlineTimeWidth();
                break;
            case nameof(CalendarEvent):
            case nameof(Storage):
            case nameof(Providers):
                EventViewModel = CreateViewModel() as IRespondableEventViewModel;
                break;
        }
    }
    
    private void UpdateBrushes()
    {
        if (NeedsResponse)
        {
            // Make the background much brighter for events needing response
            var brighterColor = MakeBrighter(Color, 0.8);
            BackgroundBrush = new SolidColorBrush(brighterColor, 0.9);
            ForegroundBrush = new SolidColorBrush(ColorUtils.ContrastTextColor(brighterColor));
            
            // Set border color for the dashed rectangle
            BorderBrush = new SolidColorBrush(Color);
        }
        else
        {
            // Normal styling
            BackgroundBrush = new SolidColorBrush(Color, 0.8);
            ForegroundBrush = new SolidColorBrush(ColorUtils.ContrastTextColor(Color));
            BorderBrush = Brushes.Transparent;
        }
    }
    
    /// <summary>
    /// Makes a color brighter by interpolating toward white.
    /// </summary>
    /// <param name="color">The original color.</param>
    /// <param name="amount">How much to brighten (0 = no change, 1 = pure white).</param>
    private static Color MakeBrighter(Color color, double amount)
    {
        // Interpolate each channel toward 255 (white)
        var r = (byte)(color.R + (255 - color.R) * amount);
        var g = (byte)(color.G + (255 - color.G) * amount);
        var b = (byte)(color.B + (255 - color.B) * amount);
        return Color.FromArgb(color.A, r, g, b);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        var border = e.NameScope.Find<Border>("Border");
        if (border == null) return;

        CancellationTokenSource? singleTapCtx = null;
        border.Tapped += async (sender, args) =>
        {
            singleTapCtx?.Cancel();
            singleTapCtx = new CancellationTokenSource();
            try
            {
                await Task.Delay(150, singleTapCtx.Token);
                ShowFlyout(border);
            } catch (TaskCanceledException) { }
        };
        border.DoubleTapped += (sender, args) =>
        {
            singleTapCtx?.Cancel();
            Console.Out.WriteLine("Double-tapped");
        };
    }

    private void ShowFlyout(Border border)
    {
        if (Storage == null || CalendarEvent == null)
        {
            FlyoutBase.ShowAttachedFlyout(border);
            return;
        }

        var flyout = FlyoutBase.GetAttachedFlyout(border);
        if (flyout is Flyout fly && fly.Content is ContentControl contentControl)
        {
            var viewModel = CreateViewModel();
            EventViewModel = viewModel as IRespondableEventViewModel;
            contentControl.Content = viewModel;
        }

        FlyoutBase.ShowAttachedFlyout(border);
    }

    private object? CreateViewModel()
    {
        if (Storage == null || CalendarEvent == null)
        {
            return null;
        }

        ICalendarProvider? calendarProvider = null;
        var accountType = CalendarEvent.Calendar.Account.Type;

        if (Providers != null && Providers.TryGetValue(accountType, out var provider))
        {
            calendarProvider = provider;
        }

        if (CalendarEvent.Calendar.Account.Type == AccountType.Google)
        {
            return new GoogleCalendarEventViewModel(
                CalendarEvent,
                Storage,
                calendarProvider);
        }

        if (CalendarEvent.Calendar.Account.Type == AccountType.CalDav)
        {
            return new CalDavEventViewModel(
                CalendarEvent,
                Storage,
                calendarProvider);
        }

        return new CalendarEventViewModel(CalendarEvent);
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