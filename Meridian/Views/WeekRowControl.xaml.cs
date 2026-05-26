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
    private sealed record SpanSegment(
        string Title, Color Color, Color? TextColor,
        int ColStart, int ColEnd,   // 0-based Mon..Sun, both inclusive
        bool IsFirst, bool IsLast,
        int Lane);

    public WeekRowControl()
    {
        InitializeComponent();
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

        // ── Step 2: lane assignment ───────────────────────────────────────────

        var laneOccupied = new List<bool[]>();
        var segments = new List<SpanSegment>();

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

            segments.Add(new SpanSegment(ev.Title, color, textColor, colStart, colEnd, isFirst, isLast, assigned));
        }

        int laneCount = laneOccupied.Count;

        // ── Step 3: RowDefinitions ────────────────────────────────────────────
        // Row 0:        date circles (Auto)
        // Row 1..N:     span lanes   (Auto, one per lane)
        // Row N+1:      chips        (Star)

        WeekGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int l = 0; l < laneCount; l++)
            WeekGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        WeekGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        int rowDate  = 0;
        int rowChips = 1 + laneCount;

        // ── Step 4: per-day item lists ────────────────────────────────────────

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

            // Full-height background
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
            Grid.SetRowSpan(bg, 1 + laneCount + 1);
            WeekGrid.Children.Add(bg);

            // Day cell: date circle + chips placed in separate rows
            var cell = new DayCellControl();
            cell.SetDate(date, isCurrentMonth, isToday);
            cell.SetItems(dayItems[col]);

            Grid.SetColumn(cell.DateCircle, col);
            Grid.SetRow(cell.DateCircle, rowDate);
            WeekGrid.Children.Add(cell.DateCircle);

            Grid.SetColumn(cell.ChipsStack, col);
            Grid.SetRow(cell.ChipsStack, rowChips);
            WeekGrid.Children.Add(cell.ChipsStack);
        }

        // ── Step 6: span bands ────────────────────────────────────────────────

        foreach (var seg in segments)
        {
            var band = MakeSpanBand(seg.Title, seg.Color, seg.TextColor, seg.IsFirst, seg.IsLast);
            Grid.SetColumn(band, seg.ColStart);
            Grid.SetColumnSpan(band, seg.ColEnd - seg.ColStart + 1);
            Grid.SetRow(band, 1 + seg.Lane);
            WeekGrid.Children.Add(band);
        }
    }

    private static Border MakeSpanBand(string title, Color color, Color? textColor, bool isFirst, bool isLast)
    {
        double leftR  = isFirst ? 3 : 0;
        double rightR = isLast  ? 3 : 0;
        var fg = textColor ?? EventColorPicker.PickReadable(color);
        return new Border
        {
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(leftR, rightR, rightR, leftR),
            Margin = new Thickness(isFirst ? 2 : 0, 1, isLast ? 2 : 0, 1),
            Padding = new Thickness(isFirst ? 4 : 0, 1, isLast ? 4 : 0, 1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = isFirst ? new TextBlock
            {
                Text = title,
                FontSize = 11,
                Foreground = new SolidColorBrush(fg),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            } : null,
        };
    }

}
