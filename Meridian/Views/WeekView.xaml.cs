using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Meridian.Models;
using Meridian.Theme;
using Meridian.ViewModels;
using Windows.Foundation;
using Windows.UI;

namespace Meridian.Views;

public sealed partial class WeekView : Page, ICalendarView
{
    private MainViewModel? _vm;
    private TimeSpan? _initialFocusTime;
    private DateTime _date;
    private DateTime _weekStart;
    private CalendarSnapshot? _lastSnapshot;
    private int? _lastContentHash;

    private Line? _nowLine;
    private Line? _nowLineOverlay;
    private Ellipse? _nowDot;
    private DateTime _nowLineDate;
    private DispatcherTimer? _nowTimer;
    private SizeChangedEventHandler? _scrollSizeChanged;
    private ScrollViewer? _scrollSizeChangedTarget;

    public WeekView()
    {
        InitializeComponent();
        Loaded += (_, _) => StartNowTimer();
        Unloaded += (_, _) => StopNowTimer();
    }

    private void StartNowTimer()
    {
        if (_nowTimer != null) return;
        _nowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _nowTimer.Tick += (_, _) => UpdateNowMarker();
        _nowTimer.Start();
    }

    private void StopNowTimer()
    {
        _nowTimer?.Stop();
        _nowTimer = null;
    }

    private void UpdateNowMarker()
    {
        // Day rolled over (or no marker yet but today is now in the visible week) → full rebuild.
        if (_nowLineDate != DateTime.Today)
        {
            Rebuild();
            return;
        }
        if (_nowLine == null) return;
        double y = WeekViewLayout.TimeToY(DateTime.Now.TimeOfDay);
        _nowLine.Y1 = y;
        _nowLine.Y2 = y;
        if (_nowLineOverlay != null)
        {
            _nowLineOverlay.Y1 = y;
            _nowLineOverlay.Y2 = y;
        }
        if (_nowDot != null)
            _nowDot.Margin = new Thickness(-5, y - 5, 0, 0);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is CalendarNavParam p)
        {
            _vm = p.ViewModel;
            _date = p.Date;
            _initialFocusTime = p.FocusTime;
        }
        else
        {
            _vm = e.Parameter as MainViewModel;
            _date = DateTime.Today;
            _initialFocusTime = null;
        }
        if (_vm == null) return;
        _vm.SetActiveView(this);
    }

    public DateTime GetCurrentDate() => _date;

    public TimeSpan? GetFocusTime()
    {
        var sv = TimedScrollViewer;
        if (sv.ActualHeight <= 0) return null;
        double centerY = sv.VerticalOffset + sv.ActualHeight / 2;
        return WeekViewLayout.YToTime(centerY);
    }

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

    public void NavigatePrevious() { _date = _date.AddDays(-7); _lastContentHash = null; }
    public void NavigateNext()     { _date = _date.AddDays(7);  _lastContentHash = null; }
    public void NavigateToToday()  { _date = DateTime.Today;    _lastContentHash = null; }

    public void ApplySnapshot(CalendarSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        var (from, to) = GetRange();
        int hash = snapshot.ContentHash(from, to);
        if (_lastContentHash == hash) return;
        _lastContentHash = hash;
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
                    ? new SolidColorBrush(AppColors.Accent)
                    : (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
            };

            var dayNum = new Border
            {
                Width = 32, Height = 32,
                CornerRadius = new CornerRadius(16),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = isToday
                    ? new SolidColorBrush(AppColors.Accent)
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

            var color = EventColorPicker.Pick(ev, accountIndex);
            var textColor = EventColorPicker.PickText(ev);
            var chip = new MonthEventChip();
            chip.Apply(new EventChipData(ev.Title, color, textColor, null, true, ev));
            Grid.SetColumn(chip, col);
            Grid.SetColumnSpan(chip, endCol - col + 1);
            Grid.SetRow(chip, r);
            AllDayGrid.Children.Add(chip);

            for (int c = col; c <= endCol; c++)
                rowMap[r][c] = (ev.End.Date, c);
        }

        // Tasks with due date — show as all-day chips
        var tasksByDay = new Dictionary<int, List<TaskItem>>();
        foreach (var t in _lastSnapshot!.Tasks.Where(t => t.Due.HasValue))
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
                chip.Apply(new EventChipData("☑ " + task.Title, AppColors.Task, null, null, true));
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
    private record DayColumnData(List<CalendarEvent> Events, List<(WeekEventBlock Block, double CascadeLeft)> EventBlocks);
    private readonly List<DayColumnData> _dayColumns = [];

    private void BuildTimedGrid()
    {
        _dayColumns.Clear();
        TimeGutter.Children.Clear();
        DayColumnsGrid.Children.Clear();
        DayColumnsGrid.ColumnDefinitions.Clear();

        // Reset now-marker refs; they'll be set again below if today is in this week.
        _nowLine = null;
        _nowLineOverlay = null;
        _nowDot = null;
        _nowLineDate = DateTime.Today;

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
                var highlight = new Rectangle { Fill = new SolidColorBrush(AppColors.AccentWashWeek), Width = 2000, Height = totalHeight };
                Canvas.SetLeft(highlight, 0);
                Canvas.SetTop(highlight, 0);
                dayCanvas.Children.Add(highlight);
            }

            // Bright now-line goes under event blocks (added before them); a faint overlay
            // is appended after blocks below so the line shows faintly across them.
            if (day.Date == DateTime.Today)
            {
                double nowY = WeekViewLayout.TimeToY(DateTime.Now.TimeOfDay);
                var nowLine = new Line { X1 = 0, X2 = 2000, Y1 = nowY, Y2 = nowY, StrokeThickness = 2, Stroke = new SolidColorBrush(AppColors.Now) };
                dayCanvas.Children.Add(nowLine);

                var dot = new Ellipse
                {
                    Width = 10, Height = 10,
                    Fill = new SolidColorBrush(AppColors.Now),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(-5, nowY - 5, 0, 0),
                };
                wrapper.Children.Add(dot);

                _nowLine = nowLine;
                _nowDot = dot;
                _nowLineDate = DateTime.Today;
            }

            // Regular timed events
            var dayEvents = _lastSnapshot!.Events.Where(e => !e.IsAllDay && e.Start.Date == day.Date).ToList();
            var layouts = WeekViewLayout.LayoutDay(dayEvents, 1000.0); // width corrected in SizeChanged
            var eventBlocks = new List<(WeekEventBlock, double)>();

            foreach (var layout in layouts)
            {
                var color = EventColorPicker.Pick(layout.Event, accountIndex);
                var textColor = EventColorPicker.PickText(layout.Event);
                var block = new WeekEventBlock();
                block.Apply(layout.Event, color, textColor, layout.Height);
                block.Width = Math.Max(layout.Width, 20);
                block.Height = layout.Height;
                Canvas.SetTop(block, layout.Top);
                Canvas.SetLeft(block, layout.Left + 1);
                Canvas.SetZIndex(block, layout.ZIndex);
                dayCanvas.Children.Add(block);
                eventBlocks.Add((block, layout.Left));
            }

            var colData = new DayColumnData(dayEvents, eventBlocks);
            _dayColumns.Add(colData);

            // Faint now-line overlay sits above event blocks so the marker stays visible
            // across them without obscuring the event text.
            if (day.Date == DateTime.Today && _nowLine != null)
            {
                double y = _nowLine.Y1;
                var overlay = new Line
                {
                    X1 = 0, X2 = 2000,
                    Y1 = y, Y2 = y,
                    StrokeThickness = 2,
                    Stroke = new SolidColorBrush(AppColors.NowFaint),
                    IsHitTestVisible = false,
                };
                dayCanvas.Children.Add(overlay);
                _nowLineOverlay = overlay;
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
        var sv = TimedScrollViewer;

        // Drop any handler armed by a previous Rebuild — otherwise stale handlers
        // accumulate across Rebuilds and yank VerticalOffset back to the focus
        // target every time the user tries to scroll.
        if (_scrollSizeChanged != null && _scrollSizeChangedTarget != null)
        {
            _scrollSizeChangedTarget.SizeChanged -= _scrollSizeChanged;
            _scrollSizeChanged = null;
            _scrollSizeChangedTarget = null;
        }

        void DoScroll()
        {
            TimeSpan focus;
            if (_initialFocusTime is { } explicitFocus)
                focus = explicitFocus;
            else
            {
                bool hasToday = Enumerable.Range(0, 7).Any(i => _weekStart.AddDays(i).Date == DateTime.Today);
                focus = hasToday ? DateTime.Now.TimeOfDay : new TimeSpan(9, 0, 0);
            }
            _initialFocusTime = null;
            double targetY = WeekViewLayout.TimeToY(focus) - sv.ActualHeight / 2;
            sv.ChangeView(null, Math.Max(0, targetY), null, disableAnimation: true);
        }

        if (sv.ActualHeight > 0)
        {
            DoScroll();
            return;
        }

        // SizeChanged fires repeatedly during window restore / cold start with
        // ActualHeight still <= 0. Wait until we see a real height, then scroll
        // and unsubscribe. Detaching before the height check would lose the
        // single shot if the first tick is still zero-height, and the next
        // Rebuild (same date/content) wouldn't re-arm it.
        SizeChangedEventHandler? handler = null;
        handler = (_, _) =>
        {
            if (sv.ActualHeight <= 0) return;
            sv.SizeChanged -= handler;
            _scrollSizeChanged = null;
            _scrollSizeChangedTarget = null;
            DoScroll();
        };
        _scrollSizeChanged = handler;
        _scrollSizeChangedTarget = sv;
        sv.SizeChanged += handler;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private int DayIndex(DateTime date) => (date.Date - _weekStart.Date).Days;

    private static DateTime GetMonday(DateTime date)
    {
        int diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-diff).Date;
    }

    private static Dictionary<string, int> BuildAccountIndex() => new();
}
