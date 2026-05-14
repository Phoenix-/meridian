using CommunityToolkit.Mvvm.ComponentModel;
using Meridian.Auth;
using Meridian.Models;
using Meridian.Services;
using Meridian.Views;
using Microsoft.UI.Dispatching;

namespace Meridian.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly CalendarCache _events;
    private readonly TaskCache _tasks;
    private readonly CalendarListCache _calendarLists;
    private readonly ReminderScheduler _reminders;
    private readonly DispatcherQueue _dispatcher;
    private readonly CancellationTokenSource _pollCts = new();

    private ICalendarView? _activeView;

    [ObservableProperty]
    private bool _isRefreshing;

    public MainViewModel(AccountManager accounts, ProviderRegistry providers, DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _calendarLists = new CalendarListCache(accounts, providers, new JsonCalendarListStore());
        _events = new CalendarCache(accounts, providers, new JsonEventStore(), _calendarLists);
        _tasks  = new TaskCache(accounts, providers, new JsonTaskStore());

        _events.DataRefreshed += OnEventsRefreshed;
        _tasks.DataRefreshed += OnTasksRefreshed;
        _calendarLists.DataRefreshed += OnCalendarListsRefreshed;

        // Subscribes to CalendarCache.DataRefreshed internally; no extra wiring.
        _reminders = new ReminderScheduler(_events, dispatcher);

        _events.FetchingChanged += UpdateFetching;
        _tasks.FetchingChanged += UpdateFetching;

        _calendarLists.RefreshAll();
        _ = PollLoopAsync(_pollCts.Token);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                // Both caches swallow per-stream errors internally, so a flaky
                // network just leaves stale data until the next tick.
                _events.RefreshAll();
                _tasks.RefreshAll();
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _pollCts.Cancel();
        _pollCts.Dispose();
    }

    // ── Called by MainWindow when the active view changes ─────────────────────

    public void SetActiveView(ICalendarView view)
    {
        _activeView = view;
        Refresh();
    }

    // ── Called by MainWindow nav buttons ─────────────────────────────────────

    public void NavigatePrevious()
    {
        _activeView?.NavigatePrevious();
        Refresh();
    }

    public void NavigateNext()
    {
        _activeView?.NavigateNext();
        Refresh();
    }

    public void NavigateToToday()
    {
        _activeView?.NavigateToToday();
        Refresh();
    }

    public void Refresh()
    {
        if (_activeView == null) return;
        var (from, to) = _activeView.GetRange();
        ApplyCurrent(from, to);
    }

    /// User-triggered refresh: pulls incremental sync for all loaded years and
    /// re-fetches tasks. Cheap when nothing changed.
    public void RefreshFromServer()
    {
        _events.RefreshAll();
        _tasks.RefreshAll();
    }

    public void InvalidateAndRefresh()
    {
        _events.InvalidateAll();
        _tasks.InvalidateAll();
        _calendarLists.InvalidateAll();
        _calendarLists.RefreshAll();
        Refresh();
    }

    /// Drops cached state for a single account and re-renders. Other accounts'
    /// data stays in the view without waiting for re-sync.
    public void InvalidateAccountAndRefresh(AccountId account)
    {
        _events.InvalidateAccount(account);
        _tasks.InvalidateAccount(account);
        _calendarLists.InvalidateAccount(account);
        _reminders.DropAccount(account);
        Refresh();
    }

    // ── Cache events ──────────────────────────────────────────────────────────

    private void OnEventsRefreshed(IReadOnlyList<int> refreshedYears)
    {
        _dispatcher.TryEnqueue(() =>
        {
            if (_activeView == null) return;
            var (from, to) = _activeView.GetRange();

            bool relevant = refreshedYears.Any(y => y >= from.Year && y <= to.AddDays(-1).Year);
            if (!relevant) return;

            ApplyCurrent(from, to);
        });
    }

    private void OnTasksRefreshed()
    {
        _dispatcher.TryEnqueue(() =>
        {
            if (_activeView == null) return;
            var (from, to) = _activeView.GetRange();
            ApplyCurrent(from, to);
        });
    }

    private void OnCalendarListsRefreshed()
    {
        // A fresh calendar list may unlock previously-unknown streams; trigger
        // a Refresh on the UI thread so EnsureYear picks them up.
        _dispatcher.TryEnqueue(Refresh);
    }

    private void ApplyCurrent(DateTime from, DateTime to)
    {
        var events = _events.Request(from, to);
        var tasks  = _tasks.Request(from, to);
        var snapshot = new CalendarSnapshot(events, tasks, IsComplete: true);
        _activeView?.ApplySnapshot(snapshot);
    }

    private void UpdateFetching() =>
        _dispatcher.TryEnqueue(() => IsRefreshing = _events.IsFetching || _tasks.IsFetching);
}
