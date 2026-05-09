using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Meridian.Models;
using Meridian.ViewModels;
using Windows.Foundation;
using Windows.UI;

namespace Meridian.Views;

public sealed partial class WeekView : Page, ICalendarView
{
    private MainViewModel? _vm;
    private DateTime _date;
    private DateTime _weekStart;
    private CalendarSnapshot? _lastSnapshot;

    // Accent colors for events per account (cycles through list)
    private static readonly Color[] EventColors =
    [
        Color.FromArgb(255, 26, 115, 232),   // Google blue
        Color.FromArgb(255, 52, 168, 83),    // Google green
        Color.FromArgb(255, 234, 67, 53),    // Google red
        Color.FromArgb(255, 251, 188, 4),    // Google yellow (dark text)
        Color.FromArgb(255, 103, 58, 183),   // purple
    ];

    private static readonly Color TaskColor = Color.FromArgb(255, 70, 130, 180);

    public WeekView()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is (MainViewModel vm, DateTime date))
        {
            _vm = vm;
            _date = date;
        }
        else
        {
            _vm = e.Parameter as MainViewModel;
            _date = DateTime.Today;
        }
        if (_vm == null) return;
        _vm.SetActiveView(this);
    }

    public DateTime GetCurrentDate() => _date;

    public (DateTime From, DateTime To) GetRange()
    {
        var monday = GetMonday(_date);
        return (monday, monday.AddDays(7));
    }

    public string GetLabel()
    {
        var monday = GetMonday(_date);
        var sunday = monday.AddDays(6);
        if (monday.Month == sunday.Month)
            return $"{monday.Day}–{sunday.Day} {sunday:MMMM yyyy}";
        if (monday.Year == sunday.Year)
            return $"{monday.Day} {monday:MMMM} – {sunday.Day} {sunday:MMMM yyyy}";
        return $"{monday.Day} {monday:MMMM yyyy} – {sunday.Day} {sunday:MMMM yyyy}";
    }

    public void NavigatePrevious() { _date = _date.AddDays(-7); }
    public void NavigateNext()     { _date = _date.AddDays(7); }

    public void ApplySnapshot(CalendarSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        Rebuild();
    }

    private void Rebuild()
    {
        var snapshot = _lastSnapshot;
        if (snapshot == null) return;

        ErrorBar.IsOpen = snapshot.ErrorMessage != null;
        ErrorBar.Message = snapshot.ErrorMessage ?? "";

        _weekStart = GetMonday(_date);
        BuildHeaders();
        BuildAllDayStrip();
        BuildTimedGrid();
    }

    // ── Headers ──────────────────────────────────────────────────────────────

    private void BuildHeaders()
    {
        HeaderGrid.Children.Clear();
        HeaderGrid.ColumnDefinitions.Clear();

        for (int i = 0; i < 7; i++)
        {
            HeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var day = _weekStart.AddDays(i);
            var isToday = day.Date == DateTime.Today;

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };

            var dayName = new TextBlock
            {
                Text = day.ToString("ddd").ToUpper(),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = isToday
                    ? new SolidColorBrush(Color.FromArgb(255, 26, 115, 232))
                    : (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
            };

            var dayNum = new Border
            {
                Width = 32, Height = 32,
                CornerRadius = new CornerRadius(16),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = isToday
                    ? new SolidColorBrush(Color.FromArgb(255, 26, 115, 232))
                    : new SolidColorBrush(Colors.Transparent),
                Child = new TextBlock
                {
                    Text = day.Day.ToString(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = isToday
                        ? new SolidColorBrush(Colors.White)
                        : (Brush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"],
                },
            };

            stack.Children.Add(dayName);
            stack.Children.Add(dayNum);

            Grid.SetColumn(stack, i);
            HeaderGrid.Children.Add(stack);
        }
    }

    // ── All-day strip ────────────────────────────────────────────────────────

    private void BuildAllDayStrip()
    {
        AllDayGrid.Children.Clear();
        AllDayGrid.ColumnDefinitions.Clear();
        AllDayGrid.RowDefinitions.Clear();

        for (int i = 0; i < 7; i++)
            AllDayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var accountIndex = BuildAccountIndex();

        // All-day events
        var allDayEvents = _lastSnapshot!.Events.Where(e => e.IsAllDay).ToList();
        var rowMap = new List<(DateTime end, int col)[]>();

        foreach (var ev in allDayEvents.OrderBy(e => e.Start))
        {
            int col = DayIndex(ev.Start.Date);
            if (col < 0 || col > 6) continue;
            int endCol = Math.Min(DayIndex(ev.End.Date.AddDays(-1)), 6);
            if (endCol < col) endCol = col;

            int r = FindFreeRow(rowMap, col, endCol, ev.End.Date);
            while (rowMap.Count <= r)
            {
                rowMap.Add(new (DateTime, int)[7]);
                AllDayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            }

            var color = GetAccountColor(ev.AccountEmail, accountIndex);
            var chip = new MonthEventChip();
            chip.Apply(new EventChipData(ev.Title, color, null, true));
            Grid.SetColumn(chip, col);
            Grid.SetColumnSpan(chip, endCol - col + 1);
            Grid.SetRow(chip, r);
            AllDayGrid.Children.Add(chip);

            for (int c = col; c <= endCol; c++)
                rowMap[r][c] = (ev.End.Date, c);
        }

        // Tasks with due date but no reminder time — show as all-day chips
        var tasksByDay = new Dictionary<int, List<TaskItem>>();
        foreach (var t in _lastSnapshot!.Tasks.Where(t => t.Due.HasValue && !t.ReminderTime.HasValue))
        {
            int col = DayIndex(t.Due!.Value.ToDateTime(TimeOnly.MinValue));
            if (col < 0 || col > 6) continue;
            if (!tasksByDay.ContainsKey(col)) tasksByDay[col] = [];
            tasksByDay[col].Add(t);
        }

        foreach (var (col, tasks) in tasksByDay)
        {
            foreach (var task in tasks)
            {
                int r = FindFreeRow(rowMap, col, col, DateTime.MaxValue);
                while (rowMap.Count <= r)
                {
                    rowMap.Add(new (DateTime, int)[7]);
                    AllDayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
                }

                var chip = new MonthEventChip();
                chip.Apply(new EventChipData("☑ " + task.Title, TaskColor, null, true));
                Grid.SetColumn(chip, col);
                Grid.SetRow(chip, r);
                AllDayGrid.Children.Add(chip);

                rowMap[r][col] = (DateTime.MaxValue, col);
            }
        }

        if (AllDayGrid.RowDefinitions.Count == 0)
            AllDayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
    }

    private static int FindFreeRow(List<(DateTime end, int col)[]> rowMap, int colFrom, int colTo, DateTime evEnd)
    {
        for (int r = 0; r < rowMap.Count; r++)
        {
            bool free = true;
            for (int c = colFrom; c <= colTo; c++)
            {
                if (rowMap[r][c].end > DateTime.MinValue)
                { free = false; break; }
            }
            if (free) return r;
        }
        return rowMap.Count;
    }

    // ── Timed grid ───────────────────────────────────────────────────────────

    // Per-column data needed to reposition events when column width changes.
    private record DayColumnData(List<CalendarEvent> Events, List<(WeekEventBlock Block, double CascadeLeft)> EventBlocks, Color[] Colors);
    private readonly List<DayColumnData> _dayColumns = [];

    private void BuildTimedGrid()
    {
        _dayColumns.Clear();
        TimeGutter.Children.Clear();
        DayColumnsGrid.Children.Clear();
        DayColumnsGrid.ColumnDefinitions.Clear();

        double totalHeight = 24 * WeekViewLayout.HourHeight;
        TimeGutter.Height = totalHeight;

        for (int h = 0; h < 24; h++)
        {
            double y = h * WeekViewLayout.HourHeight;
            var label = new TextBlock
            {
                Text = $"{h:00}:00",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Top,
                Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
            };
            Canvas.SetTop(label, y - 8);
            Canvas.SetLeft(label, 2);
            TimeGutter.Children.Add(label);
        }


        var accountIndex = BuildAccountIndex();

        for (int i = 0; i < 7; i++)
        {
            DayColumnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var day = _weekStart.AddDays(i);

            // Wrapper gives us ActualWidth via SizeChanged
            var wrapper = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            var dayCanvas = new Canvas { Height = totalHeight };
            wrapper.Children.Add(dayCanvas);

            // Hour lines
            for (int h = 0; h < 24; h++)
            {
                dayCanvas.Children.Add(new Line
                {
                    X1 = 0, X2 = 2000,
                    Y1 = h * WeekViewLayout.HourHeight, Y2 = h * WeekViewLayout.HourHeight,
                    StrokeThickness = h == 0 ? 0 : 0.5,
                    Stroke = (Brush)Application.Current.Resources["SystemControlForegroundBaseLowBrush"],
                });
            }

            // Vertical separator
            dayCanvas.Children.Add(new Line
            {
                X1 = 0, X2 = 0, Y1 = 0, Y2 = totalHeight,
                StrokeThickness = 0.5,
                Stroke = (Brush)Application.Current.Resources["SystemControlForegroundBaseLowBrush"],
            });

            // Today highlight
            if (day.Date == DateTime.Today)
            {
                var highlight = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(12, 26, 115, 232)), Width = 2000, Height = totalHeight };
                Canvas.SetLeft(highlight, 0);
                Canvas.SetTop(highlight, 0);
                dayCanvas.Children.Add(highlight);
            }

            // Regular timed events
            var dayEvents = _lastSnapshot!.Events.Where(e => !e.IsAllDay && e.Start.Date == day.Date).ToList();

            // Tasks with a reminder time on this day — synthesize as CalendarEvent (30 min block)
            var dayTaskEvents = _lastSnapshot.Tasks
                .Where(t => t.ReminderTime.HasValue && t.ReminderTime.Value.Date == day.Date)
                .Select(t => new CalendarEvent
                {
                    Id = t.Id,
                    Title = "☑ " + t.Title,
                    Start = t.ReminderTime!.Value,
                    End = t.ReminderTime.Value.AddMinutes(30),
                    IsAllDay = false,
                    AccountEmail = t.AccountEmail,
                })
                .ToList();

            var allDayEvents = dayEvents.Concat(dayTaskEvents).ToList();
            var layouts = WeekViewLayout.LayoutDay(allDayEvents, 1000.0); // width corrected in SizeChanged
            var eventBlocks = new List<(WeekEventBlock, double)>();

            foreach (var layout in layouts)
            {
                bool isTask = dayTaskEvents.Any(t => t.Id == layout.Event.Id);
                var color = isTask ? TaskColor : GetAccountColor(layout.Event.AccountEmail, accountIndex);
                var block = new WeekEventBlock();
                block.Apply(layout.Event.Title, layout.Event.Start, layout.Event.End, color, layout.Height);
                block.Width = Math.Max(layout.Width, 20);
                block.Height = layout.Height;
                Canvas.SetTop(block, layout.Top);
                Canvas.SetLeft(block, layout.Left + 1);
                Canvas.SetZIndex(block, layout.ZIndex);
                dayCanvas.Children.Add(block);
                eventBlocks.Add((block, layout.Left));
            }

            var colData = new DayColumnData(allDayEvents, eventBlocks, []);
            _dayColumns.Add(colData);

            // Time now marker: line in clipped canvas, dot in wrapper (unclipped) centered on left edge
            if (day.Date == DateTime.Today)
            {
                double nowY2 = WeekViewLayout.TimeToY(DateTime.Now.TimeOfDay);
                var nowLine = new Line { X1 = 0, X2 = 2000, Y1 = nowY2, Y2 = nowY2, StrokeThickness = 2, Stroke = new SolidColorBrush(Color.FromArgb(255, 234, 67, 53)) };
                dayCanvas.Children.Add(nowLine);

                var dot = new Ellipse
                {
                    Width = 10, Height = 10,
                    Fill = new SolidColorBrush(Color.FromArgb(255, 234, 67, 53)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(-5, nowY2 - 5, 0, 0),
                };
                wrapper.Children.Add(dot);
            }

            // Clip canvas to column bounds + reposition event blocks on resize
            var clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 0, totalHeight) };
            dayCanvas.Clip = clip;

            var colIndex = i;
            wrapper.SizeChanged += (_, _) =>
            {
                double colWidth = wrapper.ActualWidth;
                if (colWidth < 10) return;

                clip.Rect = new Windows.Foundation.Rect(0, 0, colWidth, totalHeight);

                var data = _dayColumns[colIndex];
                var newLayouts = WeekViewLayout.LayoutDay(data.Events, colWidth);
                for (int j = 0; j < newLayouts.Count && j < data.EventBlocks.Count; j++)
                {
                    var (block, _) = data.EventBlocks[j];
                    block.Width = newLayouts[j].Width;
                    Canvas.SetLeft(block, newLayouts[j].Left + 1);
                }
            };

            Grid.SetColumn(wrapper, i);
            DayColumnsGrid.Children.Add(wrapper);
        }

        ScrollToTime();
    }

    private void ScrollToTime()
    {
        var sv = FindScrollViewer(this);
        if (sv == null) return;

        void DoScroll()
        {
            bool hasToday = Enumerable.Range(0, 7).Any(i => _weekStart.AddDays(i).Date == DateTime.Today);
            double targetY = hasToday
                ? WeekViewLayout.TimeToY(DateTime.Now.TimeOfDay) - sv.ActualHeight / 2
                : WeekViewLayout.TimeToY(new TimeSpan(8, 0, 0));
            sv.ChangeView(null, Math.Max(0, targetY), null, disableAnimation: true);
        }

        if (sv.ActualHeight > 0)
            DoScroll();
        else
            sv.SizeChanged += (_, _) => DoScroll();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv) return sv;
            var found = FindScrollViewer(child);
            if (found != null) return found;
        }
        return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private int DayIndex(DateTime date) => (date.Date - _weekStart.Date).Days;

    private static DateTime GetMonday(DateTime date)
    {
        int diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-diff).Date;
    }

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
