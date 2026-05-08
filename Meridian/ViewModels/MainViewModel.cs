using Meridian.Auth;
using Meridian.Models;
using Meridian.Services;
using Meridian.Views;
using Microsoft.UI.Dispatching;

namespace Meridian.ViewModels;

public partial class MainViewModel
{
    private readonly CalendarCache _cache;
    private readonly DispatcherQueue _dispatcher;

    private ICalendarView? _activeView;

    public MainViewModel(AccountManager accounts, DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;

        var fetcher = new GoogleCalendarFetcher(accounts);
        _cache = new CalendarCache();
        _cache.SetFetcher(fetcher.FetchMonthAsync);
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


}
