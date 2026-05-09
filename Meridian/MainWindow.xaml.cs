using System.Collections.Specialized;
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

    public MainWindow(ProviderRegistry providers)
    {
        InitializeComponent();

        SystemBackdrop = new DesktopAcrylicBackdrop();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.Changed += (_, _) => UpdateTitleBarPadding();
        UpdateTitleBarPadding();

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
        try
        {
            await _accountManager.LoadSavedAccountsAsync();

            if (_accountManager.Accounts.Count == 0)
                await _accountManager.AddAccountAsync(GoogleOAuthClient.ProviderName);

            NavigateDay();
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
