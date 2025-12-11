using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using perinma.ViewModels;

namespace perinma.Views;

public partial class CalendarWeekView : UserControl
{
    private readonly Grid _timeRowGrid;
    private readonly MainView _mainView = new();
    private readonly CalendarWeekViewModel _viewModel = CalendarWeekViewModel.Instance;
    
    public CalendarWeekView()
    {
        InitializeComponent();
        
        var timeRowContainer = this.FindControl<ScrollViewer>("TimeRows");
        this._timeRowGrid = new Grid();
        timeRowContainer.Content = this._timeRowGrid;
        this._timeRowGrid.ColumnDefinitions.Add(new ColumnDefinition(1.0,  GridUnitType.Star));
        this._timeRowGrid.SetValue(Grid.IsSharedSizeScopeProperty, true);

        RowDefinition newRow()
        {
            return new RowDefinition(1.0, GridUnitType.Star)
            {
                SharedSizeGroup = "sizeGroup"
            };
        }
        
        _timeRowGrid.RowDefinitions.Add(new RowDefinition(1.0, GridUnitType.Star));
        for (int i = 0; i < 24; i++)
        {
            var timeElement1 = new TimeElement(i, 0);
            var timeElement2 = new TimeElement(i, 30);
            
            if (i > 0)
            {
                _timeRowGrid.RowDefinitions.Add(newRow());
                _timeRowGrid.RowDefinitions.Add(newRow());
                _timeRowGrid.Children.Add(timeElement1);
                timeElement1.SetValue(Grid.RowProperty, _timeRowGrid.RowDefinitions.Count - 1);
                _timeRowGrid.RowDefinitions.Add(newRow());
            }
            
            _timeRowGrid.RowDefinitions.Add(newRow());
            _timeRowGrid.Children.Add(timeElement2);
            timeElement2.SetValue(Grid.RowProperty, _timeRowGrid.RowDefinitions.Count - 1);
        }
        _timeRowGrid.RowDefinitions.Add(new RowDefinition(1.0, GridUnitType.Star));
        
        var centerView = this.FindControl<ScrollViewer>("CenterView");
        centerView.Content = _mainView;

        this.AttachedToVisualTree += async (_, __) =>
        {
            // Load events when view is ready
            await _viewModel.LoadAsync();
            _mainView.DayColumns = _viewModel.DayColumns;
            _mainView.SetEvents(_viewModel.Events);
            _mainView.RefreshContent();
        };
    }
    
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _timeRowGrid.RowDefinitions.First().Height = new GridLength(_timeRowGrid.RowDefinitions[1].ActualHeight * 1.5);
        _timeRowGrid.RowDefinitions.Last().Height = new GridLength(_timeRowGrid.RowDefinitions[1].ActualHeight * 1.5);
        _mainView.RowHeight = _timeRowGrid.RowDefinitions[1].ActualHeight;
        _mainView.RefreshContent();
    }
    
    private class MainView : ContentControl
    {

        public int DayColumns = 5;
        public double RowHeight = 0;

        private Canvas _canvas = new();
        
        public MainView()
        {
            this.Content = _canvas;
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

        private void ItemsOnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            RebuildFromItems();
        }

        private void RebuildFromItems()
        {
            _canvas.Children.Clear();
            if (_items == null) return;
            foreach (var vm in _items)
            {
                var ev = new EventView
                {
                    Title = vm.Title,
                    StartSlot = vm.StartSlot,
                    EndSlot = vm.EndSlot,
                    DaySlot = vm.DaySlot,
                    Color = vm.Color,
                };
                ev.RefreshContent();
                _canvas.Children.Add(ev);
            }
            RefreshContent();
        }

        public void RefreshContent()
        {
            this.Height = RowHeight * 24 * 4;

            var colWidth = this.Bounds.Width / DayColumns;
            foreach (var eventView in this._canvas.Children.OfType<EventView>())
            {
                eventView.SetValue(Canvas.TopProperty, eventView.StartSlot * RowHeight);
                eventView.SetValue(Canvas.LeftProperty, eventView.DaySlot * colWidth);
                eventView.Height = eventView.EndSlot * RowHeight;
                eventView.Width = colWidth;
                eventView.RefreshContent();
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

    private class TimeElement : ContentControl
    {
        public TimeElement(int hour, int minute)
        {
            var container = new StackPanel()
            {
                Orientation = Orientation.Horizontal,
            };

            var hourLabel = new TextBlock()
            {
                FontSize = 12,
                Text = $"{hour:D2}",
                VerticalAlignment = VerticalAlignment.Top,
                Foreground = Brushes.Gray,
            };
            
            var minuteLabel = new TextBlock()
            {
                FontSize = 9,
                Text = $"{minute:D2}",
                VerticalAlignment = VerticalAlignment.Top,
                Foreground = Brushes.Gray,
                Margin = new Thickness(2, 0, 0, 0),
            };
            
            container.Children.Add(hourLabel);
            container.Children.Add(minuteLabel);

            Content = container;
        }
    }

    private class EventView : ContentControl
    {

        public string Title = "";
        public DateTime StartTime;
        public DateTime EndTime;
        public int StartSlot = 0;
        public int EndSlot = 0;
        public int DaySlot = 0;
        public Color Color = Color.FromArgb(0x99, 0xFF, 0x00, 0x00);

        private readonly TextBlock _titleTextBlock = new();
        private readonly TextBlock _startTimeTextBlock = new();
        private readonly TextBlock _endTimeTextBlock = new();
        
        private readonly StackPanel _stackPanel = new();
        
        public EventView()
        {
            this.Content = _stackPanel;
            _titleTextBlock.FontWeight = FontWeight.Bold;
            _titleTextBlock.Margin = new Thickness(5);
            _titleTextBlock.TextTrimming = TextTrimming.CharacterEllipsis;

            _stackPanel.Orientation = Orientation.Vertical;
            _stackPanel.Children.Add(_titleTextBlock);
            _stackPanel.Children.Add(_startTimeTextBlock);
            _stackPanel.Children.Add(_endTimeTextBlock);
        }

        public void RefreshContent()
        {
            _stackPanel.Background = new SolidColorBrush(Color);
            _titleTextBlock.Text = Title;
        }
    }
}