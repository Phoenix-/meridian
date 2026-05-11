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
    private int? _lastContentHash;

    internal static readonly Color[] EventColors =
    [
        Color.FromArgb(255, 26, 115, 232),
        Color.FromArgb(255, 52, 168, 83),
        Color.FromArgb(255, 234, 67, 53),
        Color.FromArgb(255, 251, 188, 4),
        Color.FromArgb(255, 103, 58, 183),
    ];

    public MonthView()
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
        var first = new DateTime(_date.Year, _date.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        int startDow = ((int)first.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        int endDow = ((int)last.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return (first.AddDays(-startDow), last.AddDays(6 - endDow).AddDays(1));
    }

    public string GetLabel() => _date.ToString("MMMM yyyy");

    public void NavigatePrevious() { _date = _date.AddMonths(-1); _lastContentHash = null; }
    public void NavigateNext()     { _date = _date.AddMonths(1);  _lastContentHash = null; }
    public void NavigateToToday()  { _date = DateTime.Today;      _lastContentHash = null; }

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

        BuildDowHeaders();
        BuildWeekRows(snapshot);
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

    // ── Week rows ────────────────────────────────────────────────────────────

    private void BuildWeekRows(CalendarSnapshot snapshot)
    {
        CalendarGrid.Children.Clear();
        CalendarGrid.RowDefinitions.Clear();

        var firstOfMonth = new DateTime(_date.Year, _date.Month, 1);
        int startDow = ((int)firstOfMonth.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var gridStart = firstOfMonth.AddDays(-startDow);

        var lastOfMonth = firstOfMonth.AddMonths(1).AddDays(-1);
        int endDow = ((int)lastOfMonth.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var gridEnd = lastOfMonth.AddDays(6 - endDow);
        int weekCount = ((gridEnd - gridStart).Days + 1) / 7;

        var separatorBrush = (Brush)Application.Current.Resources["SystemControlForegroundBaseLowBrush"];

        // Shared account→color index so colors are stable across all week rows
        var accountIndex = new Dictionary<string, int>();

        // Pre-filter events into per-week buckets (an event belongs to a week if it overlaps)
        IReadOnlyList<CalendarEvent> allEvents = snapshot.Events;
        IReadOnlyList<TaskItem>     allTasks  = snapshot.Tasks;

        for (int w = 0; w < weekCount; w++)
        {
            CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var weekStart = gridStart.AddDays(w * 7);
            var weekEnd   = weekStart.AddDays(6);

            // Events that touch this week
            var weekEvents = allEvents
                .Where(e => e.Start.Date <= weekEnd && EventEndDate(e) >= weekStart)
                .ToList();

            // Tasks due this week
            var weekTasks = allTasks
                .Where(t => t.Due.HasValue &&
                            t.Due.Value.ToDateTime(TimeOnly.MinValue).Date >= weekStart &&
                            t.Due.Value.ToDateTime(TimeOnly.MinValue).Date <= weekEnd)
                .ToList();

            var weekRow = new WeekRowControl();
            weekRow.Build(weekStart, _date, weekEvents, weekTasks, EventColors, accountIndex, separatorBrush);

            Grid.SetRow(weekRow, w);
            CalendarGrid.Children.Add(weekRow);
        }
    }

    // Inclusive end date of an event (handles all-day Google convention where End is exclusive)
    private static DateTime EventEndDate(CalendarEvent ev)
    {
        if (ev.IsAllDay && ev.End.Date > ev.Start.Date)
            return ev.End.Date.AddDays(-1);
        return ev.End.Date;
    }
}
