using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Meridian.Models;
using Meridian.ViewModels;
using Windows.UI;

namespace Meridian.Views;

public sealed partial class MonthView : Page, ICalendarView
{
    private MainViewModel? _vm;
    private DateTime _date;
    private CalendarSnapshot? _lastSnapshot;

    private static readonly Color[] EventColors =
    [
        Color.FromArgb(255, 26, 115, 232),
        Color.FromArgb(255, 52, 168, 83),
        Color.FromArgb(255, 234, 67, 53),
        Color.FromArgb(255, 251, 188, 4),
        Color.FromArgb(255, 103, 58, 183),
    ];

    private static readonly Color TaskColor = Color.FromArgb(255, 70, 130, 180);

    private sealed class CellContent(
        Border cell, StackPanel stack, Grid dateRow,
        List<Border> chips, TextBlock overflowLabel, int totalCount)
    {
        public Border Cell { get; } = cell;
        public StackPanel Stack { get; } = stack;
        public Grid DateRow { get; } = dateRow;
        public List<Border> Chips { get; } = chips;
        public TextBlock OverflowLabel { get; } = overflowLabel;
        public int TotalCount { get; } = totalCount;
        public double LastFitHeight { get; set; } = -1;
    }

    private readonly List<CellContent> _cells = [];

    public MonthView()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _vm = e.Parameter as MainViewModel;
        if (_vm == null) return;
        _date = _vm.CurrentDate;

        CalendarGrid.LayoutUpdated += OnCalendarGridLayoutUpdated;
        _vm.SetActiveView(this);
    }

    public (DateTime From, DateTime To) GetRange()
    {
        var first = new DateTime(_date.Year, _date.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        int startDow = ((int)first.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        int endDow = ((int)last.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return (first.AddDays(-startDow), last.AddDays(6 - endDow).AddDays(1));
    }

    public string GetLabel() => _date.ToString("MMMM yyyy");

    public void NavigatePrevious() { _date = _date.AddMonths(-1); }
    public void NavigateNext()     { _date = _date.AddMonths(1); }

    public void ApplySnapshot(CalendarSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        Rebuild();
    }

    private void OnCalendarGridLayoutUpdated(object? _, object _2)
    {
        foreach (var cell in _cells)
            RefitCell(cell);
    }

    private void Rebuild()
    {
        var snapshot = _lastSnapshot;
        if (snapshot == null) return;

        LoadingRing.IsActive = !snapshot.IsComplete && snapshot.Events.Count == 0;
        ErrorBar.IsOpen = snapshot.ErrorMessage != null;
        ErrorBar.Message = snapshot.ErrorMessage ?? "";

        if (LoadingRing.IsActive) return;

        BuildDowHeaders();
        BuildCalendarGrid();

        var grid = CalendarGrid;
        var cells = _cells.ToList();
        void OnLayoutUpdated(object? s, object e)
        {
            grid.LayoutUpdated -= OnLayoutUpdated;
            foreach (var cell in cells)
                RefitCell(cell);
        }
        grid.LayoutUpdated += OnLayoutUpdated;
    }

    // ── Day-of-week header row ───────────────────────────────────────────────

    private void BuildDowHeaders()
    {
        DowHeaderGrid.Children.Clear();
        DowHeaderGrid.ColumnDefinitions.Clear();

        var dowNames = new[] { "ПН", "ВТ", "СР", "ЧТ", "ПТ", "СБ", "ВС" };
        for (int i = 0; i < 7; i++)
        {
            DowHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tb = new TextBlock
            {
                Text = dowNames[i],
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4),
                Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
            };
            Grid.SetColumn(tb, i);
            DowHeaderGrid.Children.Add(tb);
        }
    }

    // ── Calendar grid ────────────────────────────────────────────────────────

    private void BuildCalendarGrid()
    {
        _cells.Clear();
        CalendarGrid.Children.Clear();
        CalendarGrid.ColumnDefinitions.Clear();
        CalendarGrid.RowDefinitions.Clear();

        var current = _date;
        var firstOfMonth = new DateTime(current.Year, current.Month, 1);

        int startDow = ((int)firstOfMonth.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var gridStart = firstOfMonth.AddDays(-startDow);

        var lastOfMonth = firstOfMonth.AddMonths(1).AddDays(-1);
        int endDow = ((int)lastOfMonth.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var gridEnd = lastOfMonth.AddDays(6 - endDow);
        int totalDays = (gridEnd - gridStart).Days + 1;
        int rows = totalDays / 7;

        for (int i = 0; i < 7; i++)
            CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < rows; r++)
            CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var accountIndex = BuildAccountIndex();

        // Build per-day item lists
        var dayItems = new Dictionary<DateTime, List<(string title, Color color)>>();
        for (int d = 0; d < totalDays; d++)
            dayItems[gridStart.AddDays(d)] = [];

        foreach (var ev in _lastSnapshot!.Events)
        {
            var color = GetAccountColor(ev.AccountEmail, accountIndex);
            if (ev.IsAllDay)
            {
                var day = ev.Start.Date;
                var endDay = ev.End.Date > ev.Start.Date ? ev.End.Date.AddDays(-1) : ev.End.Date;
                for (var d = day; d <= endDay; d = d.AddDays(1))
                    if (dayItems.ContainsKey(d))
                        dayItems[d].Add((ev.Title, color));
            }
            else
            {
                if (dayItems.ContainsKey(ev.Start.Date))
                    dayItems[ev.Start.Date].Add((ev.Title, color));
            }
        }

        foreach (var task in _lastSnapshot!.Tasks)
        {
            if (!task.Due.HasValue) continue;
            var day = task.Due.Value.ToDateTime(TimeOnly.MinValue);
            if (dayItems.ContainsKey(day))
                dayItems[day].Add(("☑ " + task.Title, TaskColor));
        }

        var separatorBrush = (Brush)Application.Current.Resources["SystemControlForegroundBaseLowBrush"];
        var mutedBrush = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumLowBrush"];

        for (int d = 0; d < totalDays; d++)
        {
            int col = d % 7;
            int row = d / 7;
            var date = gridStart.AddDays(d);
            bool isCurrentMonth = date.Month == current.Month;
            bool isToday = date.Date == DateTime.Today;

            var cellContent = BuildCell(date, isCurrentMonth, isToday, dayItems[date], separatorBrush, mutedBrush);
            _cells.Add(cellContent);

            Grid.SetColumn(cellContent.Cell, col);
            Grid.SetRow(cellContent.Cell, row);
            CalendarGrid.Children.Add(cellContent.Cell);

            var captured = cellContent;
            cellContent.Cell.SizeChanged += (_, _) => RefitCell(captured);
            cellContent.Cell.Loaded += (_, _) =>
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    () => RefitCell(captured));
        }
    }

    private static void RefitCell(CellContent c)
    {
        double cellHeight = c.Cell.ActualHeight;
        if (cellHeight <= 0) return;
        if (cellHeight == c.LastFitHeight) return;
        c.LastFitHeight = cellHeight;

        var inf = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);

        var probeHeader = new Border { Width = 26, Height = 26 };
        probeHeader.Measure(inf);
        double headerHeight = probeHeader.DesiredSize.Height + c.Stack.Spacing;

        // Measure detached elements to get clean DesiredSize unaffected by tree layout cache.
        var probeChip = MakeChip("x", Colors.Transparent);
        probeChip.Measure(inf);
        double chipHeight = probeChip.DesiredSize.Height + c.Stack.Spacing;

        var probeOverflow = new TextBlock { Text = "+0 ещё", FontSize = 11, Margin = new Thickness(2, 0, 0, 0) };
        probeOverflow.Measure(inf);
        double overflowHeight = probeOverflow.DesiredSize.Height + c.Stack.Spacing;

        double stackMargin = c.Stack.Margin.Top + c.Stack.Margin.Bottom;
        double available = cellHeight - stackMargin - headerHeight;
        if (available <= 0 || chipHeight <= 0)
        {
            foreach (var chip in c.Chips) chip.Visibility = Visibility.Collapsed;
            c.OverflowLabel.Visibility = Visibility.Collapsed;
            return;
        }

        // Pass 1: do all chips fit without an overflow label?
        int shown = (int)(available / chipHeight);
        if (shown >= c.TotalCount)
        {
            shown = c.TotalCount;
        }
        else
        {
            // Pass 2: reserve room for the overflow label and recalculate
            shown = Math.Max(0, (int)((available - overflowHeight) / chipHeight));
        }

        for (int i = 0; i < c.Chips.Count; i++)
            c.Chips[i].Visibility = i < shown ? Visibility.Visible : Visibility.Collapsed;

        int overflow = c.TotalCount - shown;
        if (overflow > 0)
        {
            c.OverflowLabel.Text = $"+{overflow} ещё";
            c.OverflowLabel.Visibility = Visibility.Visible;
        }
        else
        {
            c.OverflowLabel.Visibility = Visibility.Collapsed;
        }
    }

    private CellContent BuildCell(
        DateTime date, bool isCurrentMonth, bool isToday,
        List<(string title, Color color)> items,
        Brush separatorBrush, Brush mutedBrush)
    {
        var cell = new Border
        {
            BorderThickness = new Thickness(0, 0, 1, 1),
            BorderBrush = separatorBrush,
        };
        var clip = new Microsoft.UI.Xaml.Media.RectangleGeometry();
        cell.Clip = clip;
        cell.SizeChanged += (_, e) => clip.Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height);

        var stack = new StackPanel { Spacing = 1, Margin = new Thickness(2) };

        // Date header
        var dateBorder = new Border
        {
            Width = 26, Height = 26,
            CornerRadius = new CornerRadius(13),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = isToday
                ? new SolidColorBrush(Color.FromArgb(255, 26, 115, 232))
                : new SolidColorBrush(Colors.Transparent),
            Child = new TextBlock
            {
                Text = date.Day.ToString(),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = isToday
                    ? new SolidColorBrush(Colors.White)
                    : isCurrentMonth ? null : mutedBrush,
            },
        };
        var dateRow = new Grid();
        dateRow.Children.Add(dateBorder);
        stack.Children.Add(dateRow);

        // All chips (initially all visible; RefitCell will hide overflow ones)
        var chips = new List<Border>(items.Count);
        foreach (var (title, color) in items)
        {
            var chip = MakeChip(title, color);
            chips.Add(chip);
            stack.Children.Add(chip);
        }

        // Overflow label (hidden until RefitCell decides it's needed)
        var overflowLabel = new TextBlock
        {
            Text = "+0 ещё",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
            Margin = new Thickness(2, 0, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        stack.Children.Add(overflowLabel);

        cell.Child = stack;
        return new CellContent(cell, stack, dateRow, chips, overflowLabel, items.Count);
    }

    private static Border MakeChip(string title, Color color)
    {
        return new Border
        {
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Child = new TextBlock
            {
                Text = title,
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            },
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Dictionary<string, int> BuildAccountIndex() => new();

    private Color GetAccountColor(string? email, Dictionary<string, int> index)
    {
        if (email == null) return EventColors[0];
        if (!index.TryGetValue(email, out int i))
        {
            i = index.Count % EventColors.Length;
            index[email] = i;
        }
        return EventColors[i];
    }
}
