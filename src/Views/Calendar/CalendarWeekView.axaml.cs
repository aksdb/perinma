using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using perinma.Services;

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
        // Show at most 3 full-day rows; scroll if there are more
        topView.MaxHeight = _topBarView.RowHeight * 3;

        _mainView.SetEvents(_viewModel.Events); // timed events only
        _mainView.SettingsService = _viewModel.SettingsService;
        _mainView.SettingsLoaded += OnSettingsLoaded;
        _topBarView.SetEvents(_viewModel.FullDayEvents); // full-day events only
        
        _viewModel.PropertyChanged += (sender, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(CalendarWeekViewModel.DayColumns):
                    RebuildColumns();
                    break;
                case nameof(CalendarWeekViewModel.WeekStart):
                    _mainView.WeekStart = _viewModel.WeekStart;
                    _mainView.InvalidateVisual();
                    break;
            }
        };
        _mainView.WeekStart = _viewModel.WeekStart;
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

        public event EventHandler? SettingsLoaded;

        private readonly Canvas _canvas = new();
        
        public MainView()
        {
            Content = _canvas;
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
    }

    private class TopBarView : ContentControl
    {
        public int DayColumns = 5;
        public double RowHeight = 24; // height for each full-day event row

        private readonly Canvas _canvas = new();

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