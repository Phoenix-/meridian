using CommunityToolkit.Mvvm.ComponentModel;
using Meridian.Auth;
using Meridian.Models;
using Meridian.Services;
using Meridian.Views;
using Microsoft.UI.Dispatching;

namespace Meridian.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CalendarCache _events;
    private readonly TaskCache _tasks;
    private readonly DispatcherQueue _dispatcher;

    private ICalendarView? _activeView;

    [ObservableProperty]
    private bool _isRefreshing;

    public MainViewModel(AccountManager accounts, ProviderRegistry providers, DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _events = new CalendarCache(accounts, providers, new JsonEventStore());
        _tasks  = new TaskCache(accounts, providers, new JsonTaskStore());

        _events.DataRefreshed += OnEventsRefreshed;
        _tasks.DataRefreshed += OnTasksRefreshed;

        _events.FetchingChanged += UpdateFetching;
        _tasks.FetchingChanged += UpdateFetching;
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
