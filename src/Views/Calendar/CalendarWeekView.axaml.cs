using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using perinma.Services;
using perinma.Views.MessageBox;

namespace perinma.Views.Calendar;

public partial class CalendarWeekView : UserControl
{
    private readonly Grid _timeRowGrid;
    private readonly Grid _weekdayNamesGrid;
    private readonly MainView _mainView = new();
    private readonly TopBarView _topBarView = new();
    private CalendarWeekViewModel? _viewModel;
    private ScrollViewer? _centerView;
    private bool _hasScrolledToWorkingHours;
    
    public CalendarWeekView()
    {
        InitializeComponent();
        _weekdayNamesGrid = this.FindControl<Grid>("WeekdayNames")!;
        _timeRowGrid = new Grid();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _viewModel = (CalendarWeekViewModel)DataContext!;

        var timeRowContainer = this.FindControl<ScrollViewer>("TimeRows")!;
        timeRowContainer.Content = _timeRowGrid;
        _timeRowGrid.ColumnDefinitions.Add(new ColumnDefinition(1.0,  GridUnitType.Star));
        _timeRowGrid.SetValue(Grid.IsSharedSizeScopeProperty, true);

        RowDefinition NewRow()
        {
            return new RowDefinition(1.0, GridUnitType.Star)
            {
                SharedSizeGroup = "sizeGroup"
            };
        }

        _timeRowGrid.RowDefinitions.Add(new RowDefinition(1.0, GridUnitType.Star));
        for (var i = 0; i < 24; i++)
        {
            var timeElement1 = new TimeLabel { Hour = i, Minute = 0 };
            var timeElement2 = new TimeLabel { Hour = i, Minute = 30 };

            if (i > 0)
            {
                _timeRowGrid.RowDefinitions.Add(NewRow());
                _timeRowGrid.RowDefinitions.Add(NewRow());
                _timeRowGrid.Children.Add(timeElement1);
                timeElement1.SetValue(Grid.RowProperty, _timeRowGrid.RowDefinitions.Count - 1);
                _timeRowGrid.RowDefinitions.Add(NewRow());
            }

            _timeRowGrid.RowDefinitions.Add(NewRow());
            _timeRowGrid.Children.Add(timeElement2);
            timeElement2.SetValue(Grid.RowProperty, _timeRowGrid.RowDefinitions.Count - 1);
        }
        _timeRowGrid.RowDefinitions.Add(new RowDefinition(1.0, GridUnitType.Star));

        _centerView = this.FindControl<ScrollViewer>("CenterView")!;
        _centerView.Content = _mainView;

        var topView = this.FindControl<ScrollViewer>("TopView")!;
        topView.Content = _topBarView;
        topView.MaxHeight = _topBarView.RowHeight * 3;
        
        _mainView.EventDoubleTapped += OnEventDoubleTapped;
        _mainView.SettingsService = _viewModel.SettingsService;
        _mainView.SettingsLoaded += OnSettingsLoaded;
        _topBarView.SetEvents(_viewModel.FullDayEvents);
        _topBarView.EventDoubleTapped += OnEventDoubleTapped;
        
        _mainView.SetEvents(_viewModel.Events);

        var createEventButton = this.FindControl<Button>("CreateEventButton");
        if (createEventButton != null)
        {
            createEventButton.Click += OnCreateNewEvent;
        }

        _viewModel.PropertyChanged += (sender, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(CalendarWeekViewModel.DayColumns):
                    RebuildColumns();
                    break;
                case nameof(CalendarWeekViewModel.ViewStart):
                    _mainView.WeekStart = _viewModel.ViewStart;
                    _mainView.RefreshContent();
                    break;
                case nameof(CalendarWeekViewModel.ViewMode):
                    _mainView.WeekStart = _viewModel.ViewStart;
                    RebuildColumns();
                    break;
            }
        };
        _mainView.WeekStart = _viewModel.ViewStart;
        RebuildColumns();
    }

    private void RebuildColumns()
    {
        if (_viewModel == null) return;
        _mainView.DayColumns = _viewModel.DayColumns;
        _topBarView.DayColumns = _viewModel.DayColumns;
        _mainView.RefreshContent();
        _topBarView.RefreshContent();
    }

    private void TryScrollToWorkingHours()
    {
        if (_hasScrolledToWorkingHours || _centerView == null || _mainView.RowHeight <= 0) return;
        
        // Check if content is actually laid out
        if (_mainView.Height <= 0)
        {
            return;
        }
        
        _hasScrolledToWorkingHours = true;
        
        // Scroll to show working hours at the top, with one hour of context above
        var slotsToShow = Math.Max(0, _mainView.WorkingHoursStartSlot - 4); // 4 slots = 1 hour before
        var scrollOffset = slotsToShow * _mainView.RowHeight;
        
        // Defer the scroll to next layout pass to ensure content is ready
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_centerView != null)
            {
                _centerView.Offset = new Vector(0, scrollOffset);
            }
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    private void OnSettingsLoaded(object? sender, EventArgs e)
    {
        TryScrollToWorkingHours();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _timeRowGrid.RowDefinitions.First().Height = new GridLength(_timeRowGrid.RowDefinitions[1].ActualHeight * 1.5);
        _timeRowGrid.RowDefinitions.Last().Height = new GridLength(_timeRowGrid.RowDefinitions[1].ActualHeight * 1.5);
        _mainView.RowHeight = _timeRowGrid.RowDefinitions[1].ActualHeight;
        _mainView.RefreshContent();
        _topBarView.RefreshContent();
        TryScrollToWorkingHours();
    }

    private void OnEventDoubleTapped(object? sender, Models.CalendarEvent? calendarEvent)
    {
        if (_viewModel == null) return;

        var onCompleted = new Action<string>(async (errorMessage) =>
        {
            if (!string.IsNullOrEmpty(errorMessage))
            {
                await MessageBoxWindow.ShowAsync(
                    null,
                    "Error",
                    errorMessage,
                    MessageBoxType.Error,
                    MessageBoxButtons.Ok);
            }
            else
            {
                _viewModel.Load();
            }
        });

        var editor = new EventEditView
        {
            DataContext = new EventEditViewModel(
                calendarEvent,
                calendarEvent.Calendar,
                _viewModel.Storage!,
                _viewModel.Providers,
                onCompleted)
        };
        editor.Show();
    }

    private void OnCreateNewEvent(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var onCompleted = new Action<string>(async (errorMessage) =>
        {
            if (!string.IsNullOrEmpty(errorMessage))
            {
                await MessageBoxWindow.ShowAsync(
                    null,
                    "Error",
                    errorMessage,
                    MessageBoxType.Error,
                    MessageBoxButtons.Ok);
            }
            else
            {
                _viewModel.Load();
            }
        });

        var editor = new EventEditView
        {
            DataContext = new EventEditViewModel(
                null,
                null,
                _viewModel.Storage!,
                _viewModel.Providers,
                onCompleted)
        };
        editor.Show();
    }

    private class MainView : ContentControl
    {
        public int DayColumns = 5;
        public double RowHeight = 0;
        public DateTime WeekStart;
        public SettingsService? SettingsService;

        // Cached working hours settings
        private bool[] _workingDays = [false, true, true, true, true, true, false]; // Sun-Sat, default Mon-Fri
        public int WorkingHoursStartSlot { get; private set; } = 9 * 4; // 09:00 = slot 36
        private int _workingHoursEndSlot = 17 * 4;  // 17:00 = slot 68

        public event EventHandler<Models.CalendarEvent?>? EventDoubleTapped;
        public event EventHandler? SettingsLoaded;

        private readonly Canvas _canvas = new();
        private readonly Canvas _overlayCanvas = new()
        {
            IsHitTestVisible = false
        };
        private readonly Grid _contentGrid = new();
        private System.Timers.Timer? _currentTimeUpdateTimer;

        public MainView()
        {
            _contentGrid.Children.Add(_canvas);
            _overlayCanvas.Children.Add(_currentTimeIndicator);
            _contentGrid.Children.Add(_overlayCanvas);
            Content = _contentGrid;
            StartCurrentTimeTimer();
        }

        private void StartCurrentTimeTimer()
        {
            _currentTimeUpdateTimer = new System.Timers.Timer(60000); // Update every minute
            _currentTimeUpdateTimer.Elapsed += (sender, e) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _currentTimeIndicator.Update(WeekStart, DayColumns, RowHeight);
                });
            };
            _currentTimeUpdateTimer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _currentTimeUpdateTimer?.Stop();
            _currentTimeUpdateTimer?.Dispose();
        }

        public async Task LoadSettingsAsync()
        {
            if (SettingsService == null) return;

            _workingDays =
            [
                await SettingsService.GetWorkingDaySundayAsync(),
                await SettingsService.GetWorkingDayMondayAsync(),
                await SettingsService.GetWorkingDayTuesdayAsync(),
                await SettingsService.GetWorkingDayWednesdayAsync(),
                await SettingsService.GetWorkingDayThursdayAsync(),
                await SettingsService.GetWorkingDayFridayAsync(),
                await SettingsService.GetWorkingDaySaturdayAsync()
            ];

            var startTime = await SettingsService.GetWorkingHoursStartAsync();
            var endTime = await SettingsService.GetWorkingHoursEndAsync();
            WorkingHoursStartSlot = (int)(startTime.TotalMinutes / 15);
            _workingHoursEndSlot = (int)(endTime.TotalMinutes / 15);

            SettingsLoaded?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        private ObservableCollection<EventItem>? _items;
        public void SetEvents(ObservableCollection<EventItem> items)
        {
            if (_items != null)
            {
                _items.CollectionChanged -= ItemsOnCollectionChanged;
            }
            _items = items;
            _items.CollectionChanged += ItemsOnCollectionChanged;
            RebuildFromItems();
        }

        private void ItemsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildFromItems();
        }

        private void RebuildFromItems()
        {
            _canvas.Children.Clear();
            if (_items == null) return;
            foreach (var vm in _items)
            {
                _canvas.Children.Add(vm);
                vm.EventDoubleTapped -= EventDoubleTapped;
                vm.EventDoubleTapped += EventDoubleTapped;
            }
            RefreshContent();
        }

        public void RefreshContent()
        {
            Height = RowHeight * 24 * 4;

            var dayColWidth = Bounds.Width / DayColumns;
            foreach (var eventView in _canvas.Children.OfType<EventItem>())
            {
                eventView.SetValue(Canvas.TopProperty, eventView.StartSlot * RowHeight);
                var innerWidth = dayColWidth / Math.Max(1, eventView.TotalColumns);
                var left = eventView.DaySlot * dayColWidth + eventView.ColumnSlot * innerWidth;
                eventView.SetValue(Canvas.LeftProperty, left);
                // EndSlot is inclusive end index; height is number of slots spanned
                var slotSpan = Math.Max(1, eventView.EndSlot - eventView.StartSlot + 1);
                eventView.Height = slotSpan * RowHeight;
                eventView.Width = innerWidth;
            }

            _ = LoadSettingsAsync();

            // Size overlay to fill entire view
            _overlayCanvas.Width = Bounds.Width;
            _overlayCanvas.Height = Height;
            _currentTimeIndicator.Width = Bounds.Width;
            _currentTimeIndicator.Height = Height;

            // Update current time indicator
            _currentTimeIndicator.Update(WeekStart, DayColumns, RowHeight);

            InvalidateVisual();
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            RefreshContent();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var height = Bounds.Height;
            var width = Bounds.Width;
            var columnWidth = width / DayColumns;

            // Draw non-working area backgrounds
            var nonWorkingBrush = new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)); // Semi-transparent dark overlay

            for (var dayIndex = 0; dayIndex < DayColumns; dayIndex++)
            {
                var dayDate = WeekStart.AddDays(dayIndex);
                var dayOfWeek = (int)dayDate.DayOfWeek; // 0=Sunday, 1=Monday, etc.
                var isWorkingDay = _workingDays[dayOfWeek];

                var left = dayIndex * columnWidth;

                if (!isWorkingDay)
                {
                    // Entire day is non-working
                    context.FillRectangle(nonWorkingBrush, new Rect(left, 0, columnWidth, height));
                }
                else
                {
                    // Draw non-working hours (before start and after end)
                    if (WorkingHoursStartSlot > 0)
                    {
                        var beforeHeight = WorkingHoursStartSlot * RowHeight;
                        context.FillRectangle(nonWorkingBrush, new Rect(left, 0, columnWidth, beforeHeight));
                    }

                    if (_workingHoursEndSlot < 24 * 4)
                    {
                        var afterTop = _workingHoursEndSlot * RowHeight;
                        var afterHeight = height - afterTop;
                        context.FillRectangle(nonWorkingBrush, new Rect(left, afterTop, columnWidth, afterHeight));
                    }
                }
            }

            // Draw grid lines
            var thinPen = new Pen(Brushes.LightGray, 1);
            var thickPen = new Pen(Brushes.LightGray, 2);

            for (var i = 0; i < 24; i++)
            {
                context.DrawLine(thinPen, new Point(0, (i*4+1)*RowHeight), new Point(width, (i*4+1)*RowHeight));
                context.DrawLine(thinPen, new Point(0, (i*4+2)*RowHeight), new Point(width, (i*4+2)*RowHeight));
                context.DrawLine(thinPen, new Point(0, (i*4+3)*RowHeight), new Point(width, (i*4+3)*RowHeight));
                context.DrawLine(thickPen, new Point(0, (i*4+4)*RowHeight), new Point(width, (i*4+4)*RowHeight));
            }

            for (var i = 1; i < DayColumns; i++)
            {
                context.DrawLine(thickPen, new Point(i * columnWidth, 0), new Point(i * columnWidth, height));
            }
        }

        private class CurrentTimeIndicator : Control
        {
            private DateTime _weekStart;
            private int _dayColumns;
            private double _rowHeight;

            public void Update(DateTime weekStart, int dayColumns, double rowHeight)
            {
                _weekStart = weekStart;
                _dayColumns = dayColumns;
                _rowHeight = rowHeight;
                InvalidateVisual();
                // Force immediate invalidation of parent to ensure update happens
                ((Canvas?)Parent)?.InvalidateVisual();
            }

            public override void Render(DrawingContext context)
            {
                base.Render(context);

                if (_rowHeight <= 0)
                    return;

                var today = DateTime.Now;
                var todayDate = today.Date;

                // Check if today is within the displayed week
                var weekEnd = _weekStart.AddDays(_dayColumns);
                if (todayDate < _weekStart.Date || todayDate >= weekEnd.Date)
                    return;

                // Calculate which day column is today
                var daysFromStart = (todayDate - _weekStart.Date).Days;
                var currentDayColumn = daysFromStart;

                // Calculate Y position
                var currentMinutes = today.Hour * 60 + today.Minute;
                var currentSlot = currentMinutes / 15.0;
                var yPosition = currentSlot * _rowHeight;

                // Don't draw if out of bounds
                if (yPosition < 0 || yPosition > Bounds.Height)
                    return;

                // Calculate X positions for the current day column
                var width = Bounds.Width;
                var columnWidth = width / _dayColumns;
                var leftX = currentDayColumn * columnWidth;
                var rightX = (currentDayColumn + 1) * columnWidth;

                // Define indicator styling
                var indicatorColor = Color.FromRgb(0xFF, 0x52, 0x52); // #FF5252
                var indicatorBrush = new SolidColorBrush(indicatorColor);

                // Draw glow first (behind the line)
                var glowBrush = new SolidColorBrush(Color.FromArgb(60, 0xFF, 0x52, 0x52));
                context.DrawLine(new Pen(glowBrush, 6), new Point(leftX, yPosition), new Point(rightX, yPosition));

                // Draw main line
                context.DrawLine(new Pen(indicatorBrush, 2.5), new Point(leftX, yPosition), new Point(rightX, yPosition));

                // Draw left indicator circle
                const double circleRadius = 4;
                var circleCenter = new Point(leftX + circleRadius, yPosition);
                context.DrawEllipse(indicatorBrush, null, circleCenter, circleRadius, circleRadius);

                // Draw circle glow
                var circleGlowBrush = new SolidColorBrush(Color.FromArgb(40, 0xFF, 0x52, 0x52));
                context.DrawEllipse(circleGlowBrush, null, circleCenter, circleRadius + 4, circleRadius + 4);
            }
        }

        private readonly CurrentTimeIndicator _currentTimeIndicator = new();
    }

    private class TopBarView : ContentControl
    {
        public int DayColumns = 5;
        public double RowHeight = 24; // height for each full-day event row

        private readonly Canvas _canvas = new();
        public event EventHandler<Models.CalendarEvent?>? EventDoubleTapped;

        public TopBarView()
        {
            Content = _canvas;
        }

        private ObservableCollection<EventItem>? _items;
        public void SetEvents(ObservableCollection<EventItem> items)
        {
            if (_items != null)
            {
                _items.CollectionChanged -= ItemsOnCollectionChanged;
            }
            _items = items;
            _items.CollectionChanged += ItemsOnCollectionChanged;
            RebuildFromItems();
        }

        private void ItemsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildFromItems();
        }

        private void RebuildFromItems()
        {
            _canvas.Children.Clear();
            if (_items == null) return;

            foreach (var vm in _items)
            {
                _canvas.Children.Add(vm);
            }
            RefreshContent();
        }

        public void RefreshContent()
        {
            var dayColWidth = Bounds.Width / Math.Max(1, DayColumns);
            var perDayRowIndex = new int[Math.Max(1, DayColumns)];
            var maxRows = 0;

            foreach (var ev in _canvas.Children.OfType<EventItem>())
            {
                var day = Math.Clamp(ev.DaySlot, 0, Math.Max(1, DayColumns) - 1);
                var row = perDayRowIndex[day];
                perDayRowIndex[day]++;
                if (row + 1 > maxRows) maxRows = row + 1;

                var left = day * dayColWidth;
                ev.SetValue(Canvas.LeftProperty, left);
                ev.SetValue(Canvas.TopProperty, row * RowHeight);
                ev.Width = dayColWidth;
                ev.Height = RowHeight - 4; // small spacing
            }

            Height = Math.Max(1, maxRows) * RowHeight;
            InvalidateVisual();
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            RefreshContent();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var thickPen = new Pen(Brushes.LightGray, 2);
            var height = Bounds.Height;
            var width = Bounds.Width;
            var columnWidth = width / Math.Max(1, DayColumns);
            for (var i = 1; i < DayColumns; i++)
            {
                context.DrawLine(thickPen, new Point(i * columnWidth, 0), new Point(i * columnWidth, height));
            }
        }
    }
}