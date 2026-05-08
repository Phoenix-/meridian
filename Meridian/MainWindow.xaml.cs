using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using Meridian.Auth;
using Meridian.ViewModels;
using Meridian.Views;

namespace Meridian;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private readonly AccountManager _accountManager;

    public MainWindow()
    {
        InitializeComponent();

        SystemBackdrop = new DesktopAcrylicBackdrop();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.Changed += (_, _) => UpdateTitleBarPadding();
        UpdateTitleBarPadding();

        _accountManager = new AccountManager();
        ViewModel = new MainViewModel(_accountManager, DispatcherQueue);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.IsRefreshing))
            {
                RefreshIcon.Visibility = ViewModel.IsRefreshing ? Visibility.Collapsed : Visibility.Visible;
                RefreshRing.IsActive = ViewModel.IsRefreshing;
            }
        };

        AccountsList.ItemsSource = _accountManager.Accounts;

        _ = InitAsync();
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
        await _accountManager.LoadSavedAccountsAsync();

        if (_accountManager.Accounts.Count == 0)
            await _accountManager.AddAccountAsync();

        NavigateDay();
    }

    private void SetActiveButton(Button active)
    {
        var accent = (Style)Application.Current.Resources["AccentButtonStyle"];
        foreach (var btn in new[] { BtnDay, BtnWeek, BtnMonth })
            btn.Style = btn == active ? accent : null;
    }

    private DateTime GetCurrentDate() =>
        ContentFrame.Content is ICalendarView v ? v.GetCurrentDate() : DateTime.Today;

    private void NavigateDay()
    {
        SetActiveButton(BtnDay);
        ContentFrame.Navigate(typeof(DayView), (ViewModel, GetCurrentDate()));
        UpdateDateLabel();
    }

    private void NavigateWeek()
    {
        SetActiveButton(BtnWeek);
        ContentFrame.Navigate(typeof(WeekView), (ViewModel, GetCurrentDate()));
        UpdateDateLabel();
    }

    private void NavigateMonth()
    {
        SetActiveButton(BtnMonth);
        ContentFrame.Navigate(typeof(MonthView), (ViewModel, GetCurrentDate()));
        UpdateDateLabel();
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
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateNext();
        UpdateDateLabel();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => ViewModel.Refresh();

    private async void OnAddAccountClick(object sender, RoutedEventArgs e)
    {
        AccountsFlyout.Hide();
        try
        {
            await _accountManager.AddAccountAsync();
            ViewModel.Refresh();
        }
        catch (Exception ex)
        {
            // Surface error through active view on next snapshot
            _ = ex;
        }
    }

    private async void OnRemoveAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string email)
        {
            await _accountManager.RemoveAccountAsync(email);
            ViewModel.Refresh();
        }
    }
}
