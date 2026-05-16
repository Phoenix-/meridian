using System.Collections.Specialized;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Meridian.Auth;
using Meridian.Diagnostics;
using Meridian.Models;
using Meridian.Services;
using Meridian.ViewModels;
using Meridian.Views;

namespace Meridian;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private readonly AccountManager _accountManager;
    private bool _isMaximized;
    private bool _restoreMaximized;
    private bool _closed;
    private (int W, int H, int X, int Y) _normalBounds;

    public MainWindow(ProviderRegistry providers)
    {
        InitializeComponent();

        SystemBackdrop = new DesktopAcrylicBackdrop();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.Changed += OnAppWindowChanged;
        UpdateTitleBarPadding();

        RestoreWindowState();
        AppWindow.Closing += (_, _) => SaveWindowState();
        Closed += OnWindowClosed;

        _accountManager = new AccountManager(providers);
        ViewModel = new MainViewModel(_accountManager, providers, DispatcherQueue);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.IsRefreshing))
            {
                RefreshIcon.Visibility = ViewModel.IsRefreshing ? Visibility.Collapsed : Visibility.Visible;
                RefreshRing.IsActive = ViewModel.IsRefreshing;
            }
        };

        _accountManager.Accounts.CollectionChanged += OnAccountsChanged;
        ViewModel.ExpiredAccounts.CollectionChanged += OnExpiredAccountsChanged;

        MeridianToastActivator.Invoked += OnToastInvoked;
        Closed += (_, _) => MeridianToastActivator.Invoked -= OnToastInvoked;

        // Diagnostic toast buttons stay hidden unless a marker file exists.
        // Create `%APPDATA%\Meridian\diag.enabled` (empty file) to reveal
        // them — useful for validating the toast pipeline on a new machine
        // without juggling a real calendar event.
        if (File.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Meridian", "diag.enabled")))
        {
            BtnTestToast.Visibility = Visibility.Visible;
            BtnTestScheduled.Visibility = Visibility.Visible;
        }

        _ = InitAsync();
    }

    private void OnToastInvoked(string args)
    {
        // Activate() runs on the COM RPC thread. Hop to the UI dispatcher for
        // any window manipulation, and never let an exception escape — this
        // path is far from the user's last action and stack traces are useless.
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                DateTime? targetDate = null;
                foreach (var pair in args.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = pair.IndexOf('=');
                    if (eq < 0) continue;
                    var k = pair[..eq];
                    var v = pair[(eq + 1)..];
                    if (k == "date" && DateTime.TryParse(v, out var d)) targetDate = d.Date;
                }

                // Bring the window forward regardless of args. If we somehow
                // get an empty/garbled launch we still want the user to land
                // on the app rather than nothing happening.
                //
                // The foreground dance: SetForegroundWindow is locked unless
                // our thread holds the privilege. ToastActivator.Activate
                // forwarded it via AllowSetForegroundWindow on the COM thread,
                // which covers the warm case. For cold-start (the new process
                // has no foreground rights AT ALL — there's nothing to forward),
                // we synthesize an ALT key event: SendInput counts as user
                // input from this process and re-grants the UI thread the
                // SetForegroundWindow right for one shot. Documented bypass,
                // used by shipping Win32 apps for the same scenario.
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                ForceForeground(hwnd);

                if (targetDate is { } date)
                {
                    if (ContentFrame.Content is WeekView)   NavigateWeek(date);
                    else if (ContentFrame.Content is MonthView) NavigateMonth(date);
                    else NavigateDay(date);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Toast", ex, "OnToastInvoked");
            }
        });
    }

    // Defeats the foreground lock by synthesizing an ALT keystroke (which
    // Windows counts as our-process user input and re-grants this thread the
    // SetForegroundWindow privilege), then unminimizes if needed and pushes
    // the window forward. Safe to call when we already have focus too —
    // the synthetic ALT is consumed silently by the WM.
    private static void ForceForeground(nint hwnd)
    {
        var inputs = new[]
        {
            new NativeMethods.INPUT
            {
                Type = NativeMethods.INPUT_KEYBOARD,
                U = new NativeMethods.INPUTUNION
                {
                    Keyboard = new NativeMethods.KEYBDINPUT { Vk = NativeMethods.VK_MENU }
                },
            },
            new NativeMethods.INPUT
            {
                Type = NativeMethods.INPUT_KEYBOARD,
                U = new NativeMethods.INPUTUNION
                {
                    Keyboard = new NativeMethods.KEYBDINPUT
                    {
                        Vk = NativeMethods.VK_MENU,
                        Flags = NativeMethods.KEYEVENTF_KEYUP,
                    }
                },
            },
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());

        if (NativeMethods.IsIconic(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        NativeMethods.SetForegroundWindow(hwnd);
        NativeMethods.BringWindowToTop(hwnd);
    }

    private void OnAccountsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    foreach (var item in e.NewItems!)
                        if (item is AccountId aid)
                            AccountsList.Items.Add(new AccountRow(aid) { IsExpired = ViewModel.ExpiredAccounts.Contains(aid) });
                    break;
                case NotifyCollectionChangedAction.Remove:
                    foreach (var item in e.OldItems!)
                    {
                        if (item is not AccountId removed) break;
                        for (int i = AccountsList.Items.Count - 1; i >= 0; i--)
                            if (AccountsList.Items[i] is AccountRow row && row.Id == removed)
                            { AccountsList.Items.RemoveAt(i); break; }
                    }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    AccountsList.Items.Clear();
                    break;
            }
        });
    }

    private void OnExpiredAccountsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            AccountsBadge.Visibility = ViewModel.ExpiredAccounts.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;

            var expired = new HashSet<AccountId>(ViewModel.ExpiredAccounts);
            foreach (var item in AccountsList.Items)
                if (item is AccountRow row)
                    row.IsExpired = expired.Contains(row.Id);
        });
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        // Changed can fire during teardown (e.g. un-maximize before close); Content is gone by then.
        if (_closed) return;

        UpdateTitleBarPadding();
        if (args.DidSizeChange)
        {
            // After max<->restore, AppWindow fires Changed before TitleBar.RightInset updates;
            // re-apply padding on the next dispatcher tick once insets settle.
            DispatcherQueue.TryEnqueue(() => { if (!_closed) UpdateTitleBarPadding(); });

            // While minimized, AppWindow.Size/Position report the icon-strip geometry
            // (~160x32 at position -32000,-32000). Skip the recalculation so neither
            // _isMaximized nor _normalBounds get poisoned by minimized geometry —
            // otherwise closing from a minimized state saves garbage bounds.
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (NativeMethods.IsIconic(hwnd)) return;

            // OverlappedPresenter cast is broken in AOT — detect maximized via display work area.
            var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
            if (area != null)
            {
                var wa = area.WorkArea;
                var sz = AppWindow.Size;
                var pos = AppWindow.Position;
                _isMaximized = sz.Width >= wa.Width - 2 && sz.Height >= wa.Height - 2
                               && pos.X <= wa.X + 2 && pos.Y <= wa.Y + 2;

                if (!_isMaximized)
                    _normalBounds = (sz.Width, sz.Height, pos.X, pos.Y);

                // Save immediately so Closing (which fires after Windows un-maximizes the window) sees correct state.
                SaveWindowState();
            }
        }
    }

    private void UpdateTitleBarPadding()
    {
        // TitleBar insets are in physical pixels; Thickness is in DIPs — divide by raster scale.
        var scale = Content?.XamlRoot?.RasterizationScale ?? 1.0;
        if (scale <= 0) scale = 1.0;

        AppTitleBar.Padding = new Thickness(
            (AppWindow.TitleBar.LeftInset / scale) + 8,
            8,
            (AppWindow.TitleBar.RightInset / scale) + 8,
            8);

        UpdateTitleBarPassthrough();
    }

    private void OnTitleBarLoaded(object sender, RoutedEventArgs e) => UpdateTitleBarPassthrough();
    private void OnTitleBarSizeChanged(object sender, SizeChangedEventArgs e) => UpdateTitleBarPassthrough();

    // Without this, double-clicking our title-bar buttons hits the non-client area
    // underneath them and Windows treats it as a title-bar double-click → max/restore.
    // SetTitleBar marks the whole element as drag region; we carve the button strips
    // back out as Passthrough so the chrome stops eating those clicks.
    private void UpdateTitleBarPassthrough()
    {
        if (_closed) return;
        if (Content?.XamlRoot is not { } root) return;

        var src = Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        var scale = root.RasterizationScale;
        if (scale <= 0) scale = 1.0;

        var rects = new List<Windows.Graphics.RectInt32>(2);
        AddPassthroughRect(rects, LeftButtons, scale);
        AddPassthroughRect(rects, RightButtons, scale);

        src.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough, rects.ToArray());
    }

    private void AddPassthroughRect(List<Windows.Graphics.RectInt32> rects, FrameworkElement element, double scale)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
        var transform = element.TransformToVisual(null);
        var topLeft = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
        rects.Add(new Windows.Graphics.RectInt32(
            (int)Math.Floor(topLeft.X * scale),
            (int)Math.Floor(topLeft.Y * scale),
            (int)Math.Ceiling(element.ActualWidth * scale),
            (int)Math.Ceiling(element.ActualHeight * scale)));
    }

    private async Task InitAsync()
    {
        if (_restoreMaximized)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MAXIMIZE);
            _restoreMaximized = false;
        }

        try
        {
            await _accountManager.LoadSavedAccountsAsync();

            if (_accountManager.Accounts.Count == 0)
                await _accountManager.AddAccountAsync(GoogleOAuthClient.ProviderName);

            var saved = DiskCache.ReadViewState();
            if (saved != null)
            {
                var savedFocus = saved.FocusTimeTicks is { } ticks ? new TimeSpan(ticks) : (TimeSpan?)null;
                switch (saved.View)
                {
                    case "Week":  NavigateWeek(saved.Date, savedFocus);  break;
                    case "Month": NavigateMonth(saved.Date);             break;
                    default:      NavigateDay(saved.Date, savedFocus);   break;
                }
            }
            else
            {
                NavigateDay();
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Ошибка инициализации", ex.Message);
        }
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        // XamlRoot may not be ready yet if called very early — log and bail
        var root = Content?.XamlRoot;
        if (root is null)
        {
            Log.Write("UI", $"early error '{title}': {message}");
            return;
        }
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = root,
        };
        await dialog.ShowAsync();
    }

    private void SetActiveButton(Button active)
    {
        var accent = (Style)Application.Current.Resources["AccentButtonStyle"];
        foreach (var btn in new[] { BtnDay, BtnWeek, BtnMonth })
            btn.Style = btn == active ? accent : null;
    }

    private DateTime GetCurrentDate() =>
        ContentFrame.Content is ICalendarView v ? v.GetCurrentDate() : DateTime.Today;

    // Decides what time-of-day the about-to-be-shown timed view (Day/Week) should
    // center its scroll on. Priority:
    //   1. The current view's own focus time (Day↔Week — preserve what the user scrolled to).
    //   2. null if the target range contains "now" — the target view will center on Now itself.
    //   3. Start of the first timed event in the target range.
    //   4. 09:00 fallback.
    // Returns null for Month (Month has no time axis) and for case (2) above.
    private TimeSpan? ResolveFocusTime(DateTime targetDate, Type targetViewType)
    {
        if (targetViewType == typeof(MonthView)) return null;

        var sourceFocus = (ContentFrame.Content as ICalendarView)?.GetFocusTime();
        if (sourceFocus is { } src) return src;

        var (from, to) = TargetRange(targetDate, targetViewType);
        if (DateTime.Now >= from && DateTime.Now < to) return null;

        var firstEvent = ViewModel.GetFirstTimedEventStart(from, to);
        if (firstEvent is { } ev) return ev.TimeOfDay;

        return new TimeSpan(9, 0, 0);
    }

    private static (DateTime From, DateTime To) TargetRange(DateTime date, Type viewType)
    {
        if (viewType == typeof(DayView))
            return (date.Date, date.Date.AddDays(1));
        if (viewType == typeof(WeekView))
        {
            int dow = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var monday = date.Date.AddDays(-dow);
            return (monday, monday.AddDays(7));
        }
        return (date.Date, date.Date.AddDays(1));
    }

    private void NavigateDay(DateTime? date = null, TimeSpan? explicitFocus = null)
    {
        var d = date ?? GetCurrentDate();
        var focus = explicitFocus ?? ResolveFocusTime(d, typeof(DayView));
        SetActiveButton(BtnDay);
        ContentFrame.Navigate(typeof(DayView), new CalendarNavParam(ViewModel, d, focus));
        UpdateDateLabel();
        DiskCache.WriteViewState("Day", d, focus);
    }

    private void NavigateWeek(DateTime? date = null, TimeSpan? explicitFocus = null)
    {
        var d = date ?? GetCurrentDate();
        var focus = explicitFocus ?? ResolveFocusTime(d, typeof(WeekView));
        SetActiveButton(BtnWeek);
        ContentFrame.Navigate(typeof(WeekView), new CalendarNavParam(ViewModel, d, focus));
        UpdateDateLabel();
        DiskCache.WriteViewState("Week", d, focus);
    }

    private void NavigateMonth(DateTime? date = null)
    {
        var d = date ?? GetCurrentDate();
        SetActiveButton(BtnMonth);
        ContentFrame.Navigate(typeof(MonthView), new CalendarNavParam(ViewModel, d, null));
        UpdateDateLabel();
        DiskCache.WriteViewState("Month", d, null);
    }

    private void UpdateDateLabel()
    {
        if (ContentFrame.Content is ICalendarView view)
            DateLabel.Text = view.GetLabel();
    }

    private void OnDayClick(object sender, RoutedEventArgs e) => NavigateDay();
    private void OnWeekClick(object sender, RoutedEventArgs e) => NavigateWeek();
    private void OnMonthClick(object sender, RoutedEventArgs e) => NavigateMonth();

    private void OnPrevClick(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigatePrevious();
        UpdateDateLabel();
        SaveCurrentViewState();
    }

    private void OnTodayClick(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateToToday();
        UpdateDateLabel();
        SaveCurrentViewState();
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateNext();
        UpdateDateLabel();
        SaveCurrentViewState();
    }

    private void SaveCurrentViewState()
    {
        if (ContentFrame.Content is not ICalendarView view) return;
        var viewName = view switch
        {
            WeekView  => "Week",
            MonthView => "Month",
            _         => "Day",
        };
        DiskCache.WriteViewState(viewName, view.GetCurrentDate(), view.GetFocusTime());
    }

    private void RestoreWindowState()
    {
        var saved = DiskCache.ReadWindowState();
        if (saved == null) return;

        if (saved.Maximized)
        {
            _restoreMaximized = true;
            _normalBounds = (saved.Width, saved.Height, saved.X, saved.Y);
            return;
        }

        // Check that the saved position is on a connected display (use Bounds, not WorkArea,
        // because snap positions can place a window partly outside the work area near the taskbar).
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromPoint(
            new PointInt32(saved.X, saved.Y),
            Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
        bool onScreen = area != null &&
            saved.X >= area.OuterBounds.X &&
            saved.X < area.OuterBounds.X + area.OuterBounds.Width &&
            saved.Y >= area.OuterBounds.Y &&
            saved.Y < area.OuterBounds.Y + area.OuterBounds.Height;

        if (onScreen)
            AppWindow.MoveAndResize(new RectInt32(saved.X, saved.Y, saved.Width, saved.Height));
        else
            AppWindow.Resize(new SizeInt32(saved.Width, saved.Height));
    }

    private void SaveWindowState()
    {
        // When maximized or minimized, AppWindow.Size/Position do not report normal bounds
        // (maximized geometry, or the -32000 off-screen icon strip for minimized).
        // Fall back to the last known normal bounds; if we have none, skip the save
        // entirely rather than persist garbage that would shrink the window on next launch.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var minimized = NativeMethods.IsIconic(hwnd);
        if ((_isMaximized || minimized) && _normalBounds == default) return;

        var (w, h, x, y) = (_isMaximized || minimized) && _normalBounds != default
            ? _normalBounds
            : (AppWindow.Size.Width, AppWindow.Size.Height, AppWindow.Position.X, AppWindow.Position.Y);

        var data = new WindowStateData
        {
            Width     = w,
            Height    = h,
            X         = x,
            Y         = y,
            Maximized = _isMaximized,
        };
        DiskCache.WriteWindowState(data);
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => ViewModel.RefreshFromServer();

    private void OnTestToastClick(object sender, RoutedEventArgs e)
    {
        // Diagnostic affordance: fires a Show()-path toast in 3 seconds —
        // just enough to lose focus to another window for an honest test
        // of the pop, but short enough to feel "immediate".
        ToastTester.FireIn(TimeSpan.FromSeconds(3));
    }

    private void OnTestScheduledClick(object sender, RoutedEventArgs e)
    {
        // Companion to OnTestToastClick — exercises the sanctioned
        // ScheduledToastNotification path so we can tell at a glance whether
        // it's actually delivering on this Windows build. Title prefix
        // "[sched]" distinguishes its toasts from the Show()-based ones.
        ToastTester.ScheduleIn(TimeSpan.FromSeconds(30));
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // AppWindow.Changed can still fire after Closed, when Window.Content has
        // already been torn down — accessing it then throws COMException.
        _closed = true;
        AppWindow.Changed -= OnAppWindowChanged;
        ViewModel.Dispose();
    }

    private async void OnAddAccountClick(object sender, RoutedEventArgs e)
    {
        AccountsFlyout.Hide();
        try
        {
            await _accountManager.AddAccountAsync(GoogleOAuthClient.ProviderName);
            ViewModel.InvalidateAndRefresh();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Ошибка добавления аккаунта", ex.Message);
        }
    }

    private async void OnRemoveAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not AccountId id) return;
        try
        {
            await _accountManager.RemoveAccountAsync(id);
            ViewModel.InvalidateAccountAndRefresh(id);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Ошибка удаления аккаунта", ex.Message);
        }
    }

    private async void OnReauthAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not AccountId id) return;
        AccountsFlyout.Hide();
        try
        {
            var refreshed = await _accountManager.ReauthenticateAccountAsync(id);
            // If the user signed in with a different Google account in the
            // browser, the saved token now belongs to that other account —
            // the originally-expired one is still bad. Treat that as a
            // partial success: clear nothing for the original, surface the
            // new account through the normal flow.
            if (refreshed == id)
                ViewModel.ClearAuthExpired(id);
            else
                ViewModel.InvalidateAndRefresh();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Ошибка входа", ex.Message);
        }
    }
}
