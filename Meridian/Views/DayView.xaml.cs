using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Meridian.Models;
using Meridian.ViewModels;
using Windows.UI;

namespace Meridian.Views;

public sealed partial class DayView : Page, ICalendarView
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

    private List<(WeekEventBlock Block, EventLayout Layout)> _eventBlocks = [];
    private SizeChangedEventHandler? _columnSizeChanged;

    private Line? _nowLine;
    private Ellipse? _nowDot;
    private DateTime _nowLineDate;
    private DispatcherTimer? _nowTimer;

    public DayView()
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
        // If the day under the cursor changed since the line was placed,
        // do a full rebuild so the marker moves to the right column (or disappears).
        if (_nowLineDate != DateTime.Today)
        {
            Rebuild();
            return;
        }
        if (_nowLine == null) return;
        double y = WeekViewLayout.TimeToY(DateTime.Now.TimeOfDay);
        _nowLine.Y1 = y;
        _nowLine.Y2 = y;
        if (_nowDot != null)
            _nowDot.Margin = new Thickness(-5, y - 5, 0, 0);
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

    public (DateTime From, DateTime To) GetRange() =>
        (_date.Date, _date.Date.AddDays(1));

    public string GetLabel() => _date.ToString("d MMMM yyyy");

    public void NavigatePrevious() { _date = _date.AddDays(-1); }
    public void NavigateNext()     { _date = _date.AddDays(1); }
    public void NavigateToToday()  { _date = DateTime.Today; }

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

        BuildHeader();
        BuildAllDayStrip();
        BuildTimedGrid();
    }

    // ── Header ───────────────────────────────────────────────────────────────

    private void BuildHeader()
    {
        DayHeaderStack.Children.Clear();

        bool isToday = _date.Date == DateTime.Today;
        var accentBrush = new SolidColorBrush(Color.FromArgb(255, 26, 115, 232));
        var normalBrush = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"];
        var highBrush   = (Brush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"];

        var dayName = new TextBlock
        {
            Text = _date.ToString("ddd").ToUpper(),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = isToday ? accentBrush : normalBrush,
        };

        var dayNum = new Border
        {
            Width = 46, Height = 46,
            CornerRadius = new CornerRadius(23),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = isToday
                ? accentBrush
                : new SolidColorBrush(Colors.Transparent),
            Child = new TextBlock
            {
                Text = _date.Day.ToString(),
                FontSize = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = isToday ? new SolidColorBrush(Colors.White) : highBrush,
            },
        };

        DayHeaderStack.Children.Add(dayName);
        DayHeaderStack.Children.Add(dayNum);
    }

    // ── All-day strip ────────────────────────────────────────────────────────

    private void BuildAllDayStrip()
    {
        AllDayGrid.Children.Clear();
        AllDayGrid.RowDefinitions.Clear();

        var accountIndex = new Dictionary<string, int>();

        var allDayEvents = _lastSnapshot!.Events
            .Where(e => e.IsAllDay && e.Start.Date <= _date.Date && e.End.Date > _date.Date)
            .ToList();

        foreach (var ev in allDayEvents.OrderBy(e => e.Start))
        {
            var color = GetAccountColor(ev.AccountEmail, accountIndex);
            var chip = new MonthEventChip();
            chip.Apply(new EventChipData(ev.Title, color, null, true));

            int row = AllDayGrid.RowDefinitions.Count;
            AllDayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            Grid.SetRow(chip, row);
            AllDayGrid.Children.Add(chip);
        }

        // Tasks with due date but no reminder
        foreach (var t in _lastSnapshot!.Tasks.Where(t => t.Due.HasValue && !t.ReminderTime.HasValue && t.Due.Value == DateOnly.FromDateTime(_date.Date)))
        {
            var chip = new MonthEventChip();
            chip.Apply(new EventChipData("☑ " + t.Title, TaskColor, null, true));

            int row = AllDayGrid.RowDefinitions.Count;
            AllDayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            Grid.SetRow(chip, row);
            AllDayGrid.Children.Add(chip);
        }

        if (AllDayGrid.RowDefinitions.Count == 0)
            AllDayGrid.MinHeight = 8;
    }

    // ── Timed grid ───────────────────────────────────────────────────────────

    private void BuildTimedGrid()
    {
        _eventBlocks.Clear();
        TimeGutter.Children.Clear();
        DayCanvas.Children.Clear();
        // Remove dynamically added children (now-dot) but keep the XAML-defined DayCanvas
        for (int i = DayColumnWrapper.Children.Count - 1; i >= 0; i--)
            if (DayColumnWrapper.Children[i] is not Canvas)
                DayColumnWrapper.Children.RemoveAt(i);

        double totalHeight = 24 * WeekViewLayout.HourHeight;
        TimeGutter.Height = totalHeight;
        DayCanvas.Height = totalHeight;

        var gutterFg = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"];
        var lineBrush = (Brush)Application.Current.Resources["SystemControlForegroundBaseLowBrush"];

        // Hour labels
        for (int h = 0; h < 24; h++)
        {
            double y = h * WeekViewLayout.HourHeight;
            var label = new TextBlock
            {
                Text = $"{h:00}:00",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Top,
                Foreground = gutterFg,
            };
            Canvas.SetTop(label, y - 8);
            Canvas.SetLeft(label, 2);
            TimeGutter.Children.Add(label);

            // Hour separator line — in gutter (right half) and in day column
            if (h > 0)
            {
                var gutterLine = new Line
                {
                    X1 = 36, X2 = 56,
                    Y1 = y, Y2 = y,
                    StrokeThickness = 0.5,
                    Stroke = lineBrush,
                };
                Canvas.SetTop(gutterLine, 0);
                TimeGutter.Children.Add(gutterLine);
            }

            DayCanvas.Children.Add(new Line
            {
                X1 = 0, X2 = 2000,
                Y1 = y, Y2 = y,
                StrokeThickness = h == 0 ? 0 : 0.5,
                Stroke = lineBrush,
            });

            // Half-hour line
            if (h > 0)
            {
                double yHalf = y - WeekViewLayout.HourHeight / 2;
                DayCanvas.Children.Add(new Line
                {
                    X1 = 0, X2 = 2000,
                    Y1 = yHalf, Y2 = yHalf,
                    StrokeThickness = 0.3,
                    Stroke = lineBrush,
                });
            }
        }

        // Today highlight
        if (_date.Date == DateTime.Today)
        {
            var highlight = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(10, 26, 115, 232)),
                Width = 2000,
                Height = totalHeight,
            };
            Canvas.SetLeft(highlight, 0);
            Canvas.SetTop(highlight, 0);
            DayCanvas.Children.Add(highlight);
        }

        // Events
        var accountIndex = new Dictionary<string, int>();

        var dayEvents = _lastSnapshot!.Events
            .Where(e => !e.IsAllDay && e.Start.Date == _date.Date)
            .ToList();

        var taskEvents = _lastSnapshot.Tasks
            .Where(t => t.ReminderTime.HasValue && t.ReminderTime.Value.Date == _date.Date)
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

        var allEvents = dayEvents.Concat(taskEvents).ToList();

        // Now-line (position only, no dot yet — dot goes in DayColumnWrapper after ApplyWidth)
        _nowLine = null;
        _nowDot = null;
        _nowLineDate = DateTime.Today;
        Line? nowLine = null;
        if (_date.Date == DateTime.Today)
        {
            double nowY = WeekViewLayout.TimeToY(DateTime.Now.TimeOfDay);
            nowLine = new Line
            {
                X1 = 0, X2 = 2000,
                Y1 = nowY, Y2 = nowY,
                StrokeThickness = 2,
                Stroke = new SolidColorBrush(Color.FromArgb(255, 234, 67, 53)),
            };
            DayCanvas.Children.Add(nowLine);

            var dot = new Ellipse
            {
                Width = 10, Height = 10,
                Fill = new SolidColorBrush(Color.FromArgb(255, 234, 67, 53)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(-5, nowY - 5, 0, 0),
            };
            DayColumnWrapper.Children.Add(dot);

            _nowLine = nowLine;
            _nowDot = dot;
        }

        // Clip + resize handler — unsubscribe previous before adding new
        var clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, totalHeight, totalHeight) };
        DayCanvas.Clip = clip;

        if (_columnSizeChanged != null)
            DayColumnWrapper.SizeChanged -= _columnSizeChanged;

        bool blocksBuilt = false;

        void ApplyWidth(double colWidth)
        {
            if (colWidth < 10) return;
            clip.Rect = new Windows.Foundation.Rect(0, 0, colWidth, totalHeight);

            if (!blocksBuilt)
            {
                // Build event blocks once we know the real column width
                blocksBuilt = true;
                var layouts = WeekViewLayout.LayoutDay(allEvents, colWidth);
                foreach (var layout in layouts)
                {
                    bool isTask = taskEvents.Any(t => t.Id == layout.Event.Id);
                    var color = isTask ? TaskColor : GetAccountColor(layout.Event.AccountEmail, accountIndex);
                    var block = new WeekEventBlock();
                    block.Apply(layout.Event.Title, layout.Event.Start, layout.Event.End, color, layout.Height);
                    block.Width  = Math.Max(layout.Width, 20);
                    block.Height = layout.Height;
                    Canvas.SetTop(block, layout.Top);
                    Canvas.SetLeft(block, layout.Left + 1);
                    Canvas.SetZIndex(block, layout.ZIndex);
                    // Insert before now-line so it renders beneath
                    if (nowLine != null)
                        DayCanvas.Children.Insert(DayCanvas.Children.IndexOf(nowLine), block);
                    else
                        DayCanvas.Children.Add(block);
                    _eventBlocks.Add((block, layout));
                }
            }
            else
            {
                // Reposition existing blocks on resize
                var newLayouts = WeekViewLayout.LayoutDay(allEvents, colWidth);
                for (int j = 0; j < newLayouts.Count && j < _eventBlocks.Count; j++)
                {
                    var (block, _) = _eventBlocks[j];
                    block.Width = newLayouts[j].Width;
                    Canvas.SetLeft(block, newLayouts[j].Left + 1);
                }
            }
        }

        _columnSizeChanged = (_, _) => ApplyWidth(DayColumnWrapper.ActualWidth);
        DayColumnWrapper.SizeChanged += _columnSizeChanged;

        // Apply immediately if width already known (e.g. navigating between days)
        if (DayColumnWrapper.ActualWidth >= 10)
            ApplyWidth(DayColumnWrapper.ActualWidth);

        ScrollToTime();
    }

    private void ScrollToTime()
    {
        var sv = FindScrollViewer(this);
        if (sv == null) return;

        void DoScroll()
        {
            double targetY = _date.Date == DateTime.Today
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
