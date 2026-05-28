using Meridian.Models;
using Meridian.Theme;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Meridian.Views;

public sealed partial class WeekRowControl : UserControl
{
    // Natural height of one labeled band, probed once (font-scale safe — a hardcoded
    // value would clip the label when the OS text size is enlarged).
    private static double? _bandHeight;

    private sealed record SpanSegment(
        string Title, Color Color, Color? TextColor,
        int ColStart, int ColEnd,   // 0-based Mon..Sun, both inclusive
        bool IsFirst, bool IsLast,
        int Lane, CalendarEvent Source);

    // State kept between Build() and the size-driven Relayout().
    private readonly List<SpanSegment> _segments = [];
    private readonly DayCellControl[] _cells = new DayCellControl[7];
    private Grid? _bandOverlay;
    private double _lastBodyHeight = -1;

    public WeekRowControl()
    {
        InitializeComponent();
    }

    private static double MeasureBandHeight()
    {
        if (_bandHeight is { } h) return h;
        var probe = MakeSpanBand("Ag", Colors.Gray, null, isFirst: true, isLast: true, source: null!);
        probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        _bandHeight = probe.DesiredSize.Height;
        return _bandHeight.Value;
    }

    public void Build(
        DateTime weekStart,
        DateTime displayMonth,
        IReadOnlyList<CalendarEvent> events,
        IReadOnlyList<TaskItem> tasks,
        Dictionary<string, int> accountIndex,
        Brush separatorBrush)
    {
        WeekGrid.Children.Clear();
        WeekGrid.ColumnDefinitions.Clear();
        WeekGrid.RowDefinitions.Clear();
        _segments.Clear();
        _lastBodyHeight = -1;

        for (int i = 0; i < 7; i++)
            WeekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var weekEnd = weekStart.AddDays(6);

        // ── Step 1: classify events ───────────────────────────────────────────

        var multiDay = events
            .Where(e => e.IsAllDay && e.End.Date > e.Start.Date.AddDays(1))
            .ToList();

        var singleEvents = events
            .Where(e => !(e.IsAllDay && e.End.Date > e.Start.Date.AddDays(1)))
            .ToList();

        // ── Step 2: lane assignment (unbounded — visibility is decided in Relayout) ──

        var laneOccupied = new List<bool[]>();

        foreach (var ev in multiDay)
        {
            var color = EventColorPicker.Pick(ev, accountIndex);
            var textColor = EventColorPicker.PickText(ev);
            var evEnd  = ev.End.Date.AddDays(-1);   // Google End is exclusive
            var start  = ev.Start.Date > weekStart ? ev.Start.Date : weekStart;
            var end    = evEnd < weekEnd ? evEnd : weekEnd;
            if (start > end) continue;

            int colStart = (start - weekStart).Days;
            int colEnd   = (end   - weekStart).Days;
            bool isFirst = ev.Start.Date >= weekStart;
            bool isLast  = evEnd <= weekEnd;

            int assigned = -1;
            for (int l = 0; l < laneOccupied.Count; l++)
            {
                bool free = true;
                for (int c = colStart; c <= colEnd && free; c++)
                    if (laneOccupied[l][c]) free = false;
                if (free) { assigned = l; break; }
            }
            if (assigned < 0)
            {
                assigned = laneOccupied.Count;
                laneOccupied.Add(new bool[7]);
            }
            for (int c = colStart; c <= colEnd; c++)
                laneOccupied[assigned][c] = true;

            _segments.Add(new SpanSegment(ev.Title, color, textColor, colStart, colEnd, isFirst, isLast, assigned, ev));
        }

        // ── Step 3: RowDefinitions — date row + body row (bands live in an overlay) ──

        WeekGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        WeekGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ── Step 4: per-day chip lists ────────────────────────────────────────

        var dayItems = new List<EventChipData>[7];
        for (int i = 0; i < 7; i++) dayItems[i] = [];

        foreach (var ev in singleEvents)
        {
            var color = EventColorPicker.Pick(ev, accountIndex);
            var textColor = EventColorPicker.PickText(ev);
            int col = (ev.Start.Date - weekStart).Days;
            if (col is >= 0 and < 7)
                dayItems[col].Add(new EventChipData(ev.Title, color, textColor, ev.IsAllDay ? null : ev.Start, ev.IsAllDay, ev));
        }

        foreach (var task in tasks)
        {
            if (!task.Due.HasValue) continue;
            var day = task.Due.Value.ToDateTime(TimeOnly.MinValue).Date;
            int col = (day - weekStart).Days;
            if (col is >= 0 and < 7)
                dayItems[col].Add(new EventChipData("☑ " + task.Title, AppColors.Task, null, null, true));
        }

        // ── Step 5: background borders + day cells ────────────────────────────

        for (int col = 0; col < 7; col++)
        {
            var date = weekStart.AddDays(col);
            bool isCurrentMonth = date.Month == displayMonth.Month;
            bool isToday = date.Date == DateTime.Today;

            // Full-height background spanning both rows
            var bg = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 1),
                BorderBrush = separatorBrush,
                Background = isCurrentMonth
                    ? new SolidColorBrush(AppColors.AccentWashMonth)
                    : new SolidColorBrush(Colors.Transparent),
            };
            var bgClip = new RectangleGeometry();
            bg.Clip = bgClip;
            bg.SizeChanged += (_, e) =>
                bgClip.Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
            Grid.SetColumn(bg, col);
            Grid.SetRow(bg, 0);
            Grid.SetRowSpan(bg, 2);
            WeekGrid.Children.Add(bg);

            var cell = new DayCellControl();
            cell.SetDate(date, isCurrentMonth, isToday);
            cell.SetItems(dayItems[col]);
            cell.OnOverflowTap = d => App.MainWindow?.RequestNavigateDay(d);
            _cells[col] = cell;

            Grid.SetColumn(cell.DateCircle, col);
            Grid.SetRow(cell.DateCircle, 0);
            WeekGrid.Children.Add(cell.DateCircle);

            Grid.SetColumn(cell.BodyPanel, col);
            Grid.SetRow(cell.BodyPanel, 1);
            WeekGrid.Children.Add(cell.BodyPanel);
        }

        // ── Step 6: band overlay (7 star columns) on top of the body row ──────
        // Bands are placed/sized in Relayout once the body height is known.

        _bandOverlay = new Grid();
        for (int i = 0; i < 7; i++)
            _bandOverlay.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_bandOverlay, 0);
        Grid.SetColumnSpan(_bandOverlay, 7);
        Grid.SetRow(_bandOverlay, 1);
        _bandOverlay.SizeChanged += OnBodySizeChanged;
        _bandOverlay.Loaded += (_, _) =>
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Relayout(force: true));
        WeekGrid.Children.Add(_bandOverlay);
    }

    private void OnBodySizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Height > 0) Relayout(force: false);
    }

    // Decides which atomic multi-day bands fit (greedy by lane, all-or-nothing per band,
    // always leaving room for chips), positions the visible bands in the overlay, and
    // tells each day cell how much band height to reserve + how many bands it hid.
    private void Relayout(bool force)
    {
        if (_bandOverlay is null) return;

        double bodyHeight = _bandOverlay.ActualHeight;
        if (bodyHeight <= 0) return;
        if (!force && bodyHeight == _lastBodyHeight) return;
        _lastBodyHeight = bodyHeight;

        double laneHeight = MeasureBandHeight();

        // Total lane slots a day's body could hold; always keep ≥1 row for chips so the
        // timeline is never fully hidden behind bands (and a row for "+N" when needed).
        int capRows = Math.Max(0, (int)(bodyHeight / laneHeight));
        int maxBandRows = Math.Max(0, capRows - 1);   // guarantee one chip row

        var dayBandRows = new int[7];
        var hiddenBandCount = new int[7];

        // Greedy by lane: a band shows only if every day it covers can host its lane within
        // the band budget; otherwise it is hidden on every covered day (atomic).
        foreach (var seg in _segments.OrderBy(s => s.Lane))
        {
            bool showable = seg.Lane + 1 <= maxBandRows;
            if (showable)
            {
                for (int c = seg.ColStart; c <= seg.ColEnd; c++)
                    dayBandRows[c] = Math.Max(dayBandRows[c], seg.Lane + 1);
            }
            else
            {
                for (int c = seg.ColStart; c <= seg.ColEnd; c++)
                    hiddenBandCount[c]++;
            }
        }

        // Rebuild the visible bands in the overlay.
        _bandOverlay.Children.Clear();
        foreach (var seg in _segments)
        {
            if (seg.Lane + 1 > maxBandRows) continue;   // hidden → folded into "+N"
            var band = MakeSpanBand(seg.Title, seg.Color, seg.TextColor, seg.IsFirst, seg.IsLast, seg.Source);
            band.VerticalAlignment = VerticalAlignment.Top;
            // Stride is exactly laneHeight (the measured band height). Keep the horizontal
            // insets from MakeSpanBand but drive the vertical position from the lane index;
            // a 1px top/bottom gap inside the stride keeps adjacent lanes from touching.
            band.Height = laneHeight - 2;
            band.Margin = new Thickness(band.Margin.Left, seg.Lane * laneHeight + 1,
                                        band.Margin.Right, 0);
            Grid.SetColumn(band, seg.ColStart);
            Grid.SetColumnSpan(band, seg.ColEnd - seg.ColStart + 1);
            _bandOverlay.Children.Add(band);
        }

        for (int c = 0; c < 7; c++)
            _cells[c]?.SetBandReserve(dayBandRows[c] * laneHeight, hiddenBandCount[c]);
    }

    private static Border MakeSpanBand(string title, Color color, Color? textColor, bool isFirst, bool isLast, CalendarEvent source)
    {
        double leftR  = isFirst ? 3 : 0;
        double rightR = isLast  ? 3 : 0;
        var fg = textColor ?? EventColorPicker.PickReadable(color);
        var band = new Border
        {
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(leftR, rightR, rightR, leftR),
            Margin = new Thickness(isFirst ? 2 : 0, 1, isLast ? 2 : 0, 1),
            // Keep a left inset even on continuation segments so the repeated label
            // (Google-style) isn't glued to the cell border.
            Padding = new Thickness(4, 1, isLast ? 4 : 0, 1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            // The title is repeated on every week the span touches, so continuation
            // segments get a label too (this also gives the band a non-zero height).
            Child = new TextBlock
            {
                Text = title,
                FontSize = 11,
                Foreground = new SolidColorBrush(fg),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        band.Tapped += (_, e) =>
        {
            EventDetailsFlyout.Show(band, source);
            e.Handled = true;
        };
        band.RightTapped += (_, e) =>
        {
            MonthEventChip.ShowContextMenu(band, source, e.GetPosition(band));
            e.Handled = true;
        };

        return band;
    }

}
