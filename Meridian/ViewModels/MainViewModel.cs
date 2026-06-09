using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Meridian.Auth;
using Meridian.Diagnostics;
using Meridian.Models;
using Meridian.Services;
using Meridian.Views;
using Microsoft.UI.Dispatching;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Meridian.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly CalendarCache _events;
    private readonly TaskCache _tasks;
    private readonly CalendarListCache _calendarLists;
    private readonly ReminderScheduler _reminders;
    private readonly OngoingEventIndicator _ongoing;
    private readonly DispatcherQueue _dispatcher;
    private readonly CancellationTokenSource _pollCts = new();

    private ICalendarView? _activeView;

    [ObservableProperty]
    private bool _isRefreshing;

    // Accounts whose tokens are known-bad. Bound by the UI to show a "!"
    // badge on the accounts button and a warning icon next to each bad
    // account inside the flyout. Mutations happen on the dispatcher thread
    // so XAML can data-bind directly without a sync wrapper.
    public ObservableCollection<AccountId> ExpiredAccounts { get; } = [];

    public event Action<AccountId>? AccountAuthExpiredOnce;

    // Per-session de-dupe of "session expired" toasts: we surface the toast
    // exactly once for each account that goes bad, so the polling loop
    // doesn't pop a new toast every minute while the user is busy. Reset
    // happens with the process lifetime — a restart counts as fresh.
    private readonly HashSet<AccountId> _toastedExpired = [];

    public MainViewModel(AccountManager accounts, ProviderRegistry providers, DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _calendarLists = new CalendarListCache(accounts, providers, new JsonCalendarListStore());
        _events = new CalendarCache(accounts, providers, new JsonEventStore(), _calendarLists);
        _tasks  = new TaskCache(accounts, providers, new JsonTaskStore());

        _events.DataRefreshed += OnEventsRefreshed;
        _tasks.DataRefreshed += OnTasksRefreshed;
        _calendarLists.DataRefreshed += OnCalendarListsRefreshed;

        _events.AccountAuthExpired += OnAccountAuthExpired;
        _tasks.AccountAuthExpired += OnAccountAuthExpired;
        _calendarLists.AccountAuthExpired += OnAccountAuthExpired;

        // Subscribes to CalendarCache.DataRefreshed internally; no extra wiring.
        _reminders = new ReminderScheduler(_events, dispatcher);
        _ongoing   = new OngoingEventIndicator(_events, accounts, dispatcher);

        _events.FetchingChanged += UpdateFetching;
        _tasks.FetchingChanged += UpdateFetching;

        _calendarLists.RefreshAll();
        _ = PollLoopAsync(_pollCts.Token);
    }

    private void OnAccountAuthExpired(AccountId account)
    {
        // Caches can raise this from any thread (continuations on the thread
        // pool). Hop to the UI thread before mutating the ObservableCollection
        // bound to XAML, and dedupe on top of that.
        _dispatcher.TryEnqueue(() =>
        {
            if (!ExpiredAccounts.Contains(account))
                ExpiredAccounts.Add(account);

            if (_toastedExpired.Add(account))
            {
                ShowAuthExpiredToast(account);
                AccountAuthExpiredOnce?.Invoke(account);
            }
        });
    }

    private static void ShowAuthExpiredToast(AccountId account)
    {
        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier(ToastSetup.ResolvedAumid);
            var title = $"Сессия истекла: {account.Email}";
            var body = "Откройте Meridian и войдите в аккаунт заново";

            var xml = new XmlDocument();
            xml.LoadXml(
                $"""
                <toast launch="{System.Net.WebUtility.HtmlEncode($"reauth={account}")}">
                  <visual>
                    <binding template="ToastGeneric">
                      <text>{System.Net.WebUtility.HtmlEncode(title)}</text>
                      <text>{System.Net.WebUtility.HtmlEncode(body)}</text>
                    </binding>
                  </visual>
                </toast>
                """);

            notifier.Show(new ToastNotification(xml)
            {
                Tag = "auth-" + account.ToDirectoryName(),
                Group = "mrd-auth",
            });
            Log.Write("Auth", $"toast: shown auth-expired for {account}");
        }
        catch (Exception ex)
        {
            Log.Error("Auth", ex, "ShowAuthExpiredToast");
        }
    }

    // Drops auth-expired state for an account after a successful re-auth and
    // kicks off fresh syncs so the UI repopulates without waiting for the
    // 60-second poll tick.
    public void ClearAuthExpired(AccountId account)
    {
        _events.ClearAuthExpired(account);
        _tasks.ClearAuthExpired(account);
        ExpiredAccounts.Remove(account);
        _toastedExpired.Remove(account);
        _calendarLists.RefreshAll();
        _events.RefreshAll();
        _tasks.RefreshAll();
        Refresh();
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
        DirectoryCache.InvalidateAll();
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
        DirectoryCache.InvalidateAccount(account);
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

    // First timed (non-all-day) event whose Start falls inside [from, to), or null.
    // Used by view-switch heuristics to focus the new view's scroll on something useful
    // when the source view had no time-of-day notion (Month).
    public DateTime? GetFirstTimedEventStart(DateTime from, DateTime to)
    {
        var events = _events.Request(from, to);
        DateTime? best = null;
        foreach (var ev in events)
        {
            if (ev.IsAllDay) continue;
            if (ev.Start < from || ev.Start >= to) continue;
            if (best == null || ev.Start < best) best = ev.Start;
        }
        return best;
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
