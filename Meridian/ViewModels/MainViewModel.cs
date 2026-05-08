using CommunityToolkit.Mvvm.ComponentModel;
using Meridian.Auth;
using Meridian.Models;
using Meridian.Services;
using Meridian.Views;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;

namespace Meridian.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AccountManager _accounts;
    private readonly CalendarCache _cache;
    private readonly DispatcherQueue _dispatcher;

    private ICalendarView? _activeView;

    [ObservableProperty]
    private DateTime _currentDate = DateTime.Today;

    public ObservableCollection<string> Accounts => _accounts.Accounts;

    public MainViewModel(AccountManager accounts, DispatcherQueue dispatcher)
    {
        _accounts = accounts;
        _dispatcher = dispatcher;

        _cache = new CalendarCache();
        _cache.SetFetcher(FetchMonthAsync);
        _cache.DataRefreshed += OnDataRefreshed;
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

    public void Refresh()
    {
        if (_activeView == null) return;
        var (from, to) = _activeView.GetRange();
        var snapshot = _cache.Request(from, to);
        _activeView.ApplySnapshot(snapshot);
    }

    // ── Cache event ───────────────────────────────────────────────────────────

    private void OnDataRefreshed(IReadOnlyList<YearMonth> refreshed)
    {
        _dispatcher.TryEnqueue(() =>
        {
            if (_activeView == null) return;
            var (from, to) = _activeView.GetRange();

            bool relevant = refreshed.Any(ym =>
            {
                var ymFrom = ym.FirstDay();
                var ymTo = ym.FirstDayOfNext();
                return ymFrom < to && ymTo > from;
            });

            if (!relevant) return;

            var snapshot = _cache.Request(from, to);
            _activeView.ApplySnapshot(snapshot);
        });
    }

    // ── Fetch (called by CalendarCache) ───────────────────────────────────────

    private async Task FetchMonthAsync(YearMonth ym)
    {
        var entry = _cache.GetOrCreate(ym);
        var from = ym.FirstDay();
        var to = ym.FirstDayOfNext();

        try
        {
            var calendarServices = _accounts.Credentials
                .Select(kv => (email: kv.Key, svc: new GoogleCalendarService(kv.Value)))
                .ToList();

            var eventFetches = calendarServices.Select(x => x.svc.GetEventsAsync(from, to, x.email));
            var taskFetches = _accounts.Credentials.Select(kv =>
                new GoogleTasksService(kv.Value).GetTasksAsync(kv.Key)
                    .ContinueWith(t => (email: kv.Key, tasks: t.Result)));
            var reminderFetches = calendarServices.Select(x => x.svc.GetTaskReminderTimesAsync(from, to));

            var allEvents = (await Task.WhenAll(eventFetches)).SelectMany(x => x).ToList();
            var taskResults = await Task.WhenAll(taskFetches);
            var reminderMaps = await Task.WhenAll(reminderFetches);

            var allReminders = reminderMaps.SelectMany(d => d).ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var (email, tasks) in taskResults)
            {
                foreach (var t in tasks)
                    if (t.Id != null && allReminders.TryGetValue(t.Id, out var rt))
                        t.ReminderTime = rt;
                _cache.SetTasks(email, tasks);
            }

            entry.Events = allEvents;
            entry.State = CacheState.Fresh;
        }
        catch (Exception ex)
        {
            entry.State = CacheState.Stale;
            _dispatcher.TryEnqueue(() =>
                _activeView?.ApplySnapshot(new CalendarSnapshot([], [], false,
                    $"Ошибка загрузки {ym}: {ex.Message}")));
        }
    }
}
