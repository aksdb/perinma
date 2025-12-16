using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using perinma.ViewModels;

namespace perinma.Views;

public partial class CalendarWeekView : UserControl
{
    private readonly Grid _timeRowGrid;
    private readonly MainView _mainView = new();
    private readonly TopBarView _topBarView = new();
    private readonly CalendarWeekViewModel _viewModel = CalendarWeekViewModel.Instance;
    
    public CalendarWeekView()
    {
        InitializeComponent();
        
        var timeRowContainer = this.FindControl<ScrollViewer>("TimeRows")!;
        _timeRowGrid = new Grid();
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
        
        var centerView = this.FindControl<ScrollViewer>("CenterView")!;
        centerView.Content = _mainView;

        var topView = this.FindControl<ScrollViewer>("TopView")!;
        topView.Content = _topBarView;
        // Show at most 3 full-day rows; scroll if there are more
        topView.MaxHeight = _topBarView.RowHeight * 3;

        _viewModel.Load();
        _mainView.DayColumns = _viewModel.DayColumns;
        _topBarView.DayColumns = _viewModel.DayColumns;
        _mainView.SetEvents(_viewModel.Events); // timed events only
        _topBarView.SetEvents(_viewModel.FullDayEvents); // full-day events only
        _mainView.RefreshContent();
        _topBarView.RefreshContent();
    }
    
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _timeRowGrid.RowDefinitions.First().Height = new GridLength(_timeRowGrid.RowDefinitions[1].ActualHeight * 1.5);
        _timeRowGrid.RowDefinitions.Last().Height = new GridLength(_timeRowGrid.RowDefinitions[1].ActualHeight * 1.5);
        _mainView.RowHeight = _timeRowGrid.RowDefinitions[1].ActualHeight;
        _mainView.RefreshContent();
        _topBarView.RefreshContent();
    }
    
    private class MainView : ContentControl
    {

        public int DayColumns = 5;
        public double RowHeight = 0;

        private readonly Canvas _canvas = new();
        
        public MainView()
        {
            Content = _canvas;
        }

        private ObservableCollection<EventItemViewModel>? _items;
        public void SetEvents(ObservableCollection<EventItemViewModel> items)
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
            foreach (var eventView in _canvas.Children.OfType<EventItemViewModel>())
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
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            RefreshContent();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var thinPen = new Pen(Brushes.LightGray, 1);
            var thickPen = new Pen(Brushes.LightGray, 2);

            var height = Bounds.Height;
            var width = Bounds.Width;

            for (int i = 0; i < 24; i++)
            {
                context.DrawLine(thinPen, new Point(0, (i*4+1)*RowHeight), new Point(width, (i*4+1)*RowHeight));
                context.DrawLine(thinPen, new Point(0, (i*4+2)*RowHeight), new Point(width, (i*4+2)*RowHeight));
                context.DrawLine(thinPen, new Point(0, (i*4+3)*RowHeight), new Point(width, (i*4+3)*RowHeight));
                context.DrawLine(thickPen, new Point(0, (i*4+4)*RowHeight), new Point(width, (i*4+4)*RowHeight));
            }

            var columnWidth = width / DayColumns;
            for (int i = 1; i < DayColumns; i++)
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

        private ObservableCollection<EventItemViewModel>? _items;
        public void SetEvents(ObservableCollection<EventItemViewModel> items)
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
                var ev = new EventChip
                {
                    Title = vm.Title,
                    DaySlot = vm.DaySlot,
                    BackgroundBrush = new SolidColorBrush(vm.Color, 0.8),
                    ForegroundBrush = new SolidColorBrush(ColorUtils.ContrastTextColor(vm.Color))
                };
                _canvas.Children.Add(ev);
            }
            RefreshContent();
        }

        public void RefreshContent()
        {
            var dayColWidth = Bounds.Width / Math.Max(1, DayColumns);
            var perDayRowIndex = new int[Math.Max(1, DayColumns)];
            var maxRows = 0;

            foreach (var ev in _canvas.Children.OfType<EventChip>())
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