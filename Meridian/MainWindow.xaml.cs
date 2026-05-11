using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Meridian.Auth;
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

        _ = InitAsync();
    }

    private void OnAccountsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    foreach (var item in e.NewItems!)
                        AccountsList.Items.Add(item);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    foreach (var item in e.OldItems!)
                    {
                        if (item is not AccountId removed) break;
                        for (int i = AccountsList.Items.Count - 1; i >= 0; i--)
                            if (AccountsList.Items[i] is AccountId aid && aid == removed)
                            { AccountsList.Items.RemoveAt(i); break; }
                    }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    AccountsList.Items.Clear();
                    break;
            }
        });
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        UpdateTitleBarPadding();
        if (args.DidSizeChange)
        {
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
        AppTitleBar.Padding = new Thickness(
            AppWindow.TitleBar.LeftInset + 8,
            8,
            AppWindow.TitleBar.RightInset + 8,
            8);
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
                switch (saved.View)
                {
                    case "Week":  NavigateWeek(saved.Date);  break;
                    case "Month": NavigateMonth(saved.Date); break;
                    default:      NavigateDay(saved.Date);   break;
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
            System.Diagnostics.Debug.WriteLine($"[{title}] {message}");
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

    private void NavigateDay(DateTime? date = null)
    {
        var d = date ?? GetCurrentDate();
        SetActiveButton(BtnDay);
        ContentFrame.Navigate(typeof(DayView), (ViewModel, d));
        UpdateDateLabel();
        DiskCache.WriteViewState("Day", d);
    }

    private void NavigateWeek(DateTime? date = null)
    {
        var d = date ?? GetCurrentDate();
        SetActiveButton(BtnWeek);
        ContentFrame.Navigate(typeof(WeekView), (ViewModel, d));
        UpdateDateLabel();
        DiskCache.WriteViewState("Week", d);
    }

    private void NavigateMonth(DateTime? date = null)
    {
        var d = date ?? GetCurrentDate();
        SetActiveButton(BtnMonth);
        ContentFrame.Navigate(typeof(MonthView), (ViewModel, d));
        UpdateDateLabel();
        DiskCache.WriteViewState("Month", d);
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
        DiskCache.WriteViewState(viewName, view.GetCurrentDate());
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
        // When maximized, AppWindow.Size/Position report the maximized geometry.
        // Save the last known normal bounds so we can restore correctly after un-maximizing.
        var (w, h, x, y) = _isMaximized && _normalBounds != default
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
        if (sender is Button btn && btn.Tag is AccountId id)
        {
            await _accountManager.RemoveAccountAsync(id);
            ViewModel.InvalidateAndRefresh();
        }
    }
}
