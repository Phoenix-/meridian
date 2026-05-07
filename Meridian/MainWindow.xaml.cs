using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Meridian.Auth;
using Meridian.ViewModels;
using Meridian.Views;

namespace Meridian;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private readonly AccountManager _accountManager;

    private enum ViewMode { Day, Week, Month }
    private ViewMode _currentMode = ViewMode.Day;

    public MainWindow()
    {
        InitializeComponent();

        _accountManager = new AccountManager();
        ViewModel = new MainViewModel(_accountManager);

        AccountsList.ItemsSource = _accountManager.Accounts;

        _ = InitAsync();
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

    private void NavigateDay()
    {
        _currentMode = ViewMode.Day;
        SetActiveButton(BtnDay);
        DateLabel.Text = ViewModel.CurrentDate.ToString("d MMMM yyyy");
        ContentFrame.Navigate(typeof(DayView), ViewModel);
        _ = ViewModel.LoadDayCommand.ExecuteAsync(null);
    }

    private void NavigateWeek()
    {
        _currentMode = ViewMode.Week;
        SetActiveButton(BtnWeek);
        DateLabel.Text = ViewModel.CurrentDate.ToString("d MMMM yyyy");
        ContentFrame.Navigate(typeof(WeekView), ViewModel);
        _ = ViewModel.LoadWeekCommand.ExecuteAsync(null);
    }

    private void NavigateMonth()
    {
        _currentMode = ViewMode.Month;
        SetActiveButton(BtnMonth);
        DateLabel.Text = ViewModel.CurrentDate.ToString("MMMM yyyy");
        ContentFrame.Navigate(typeof(DayView), ViewModel);
        _ = ViewModel.LoadMonthCommand.ExecuteAsync(null);
    }

    private void OnDayClick(object sender, RoutedEventArgs e) => NavigateDay();
    private void OnWeekClick(object sender, RoutedEventArgs e) => NavigateWeek();
    private void OnMonthClick(object sender, RoutedEventArgs e) => NavigateMonth();

    private void OnPrevClick(object sender, RoutedEventArgs e)
    {
        ViewModel.PreviousDayCommand.Execute(null);
        Refresh();
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        ViewModel.NextDayCommand.Execute(null);
        Refresh();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Refresh();

    private async void OnAddAccountClick(object sender, RoutedEventArgs e)
    {
        AccountsFlyout.Hide();
        try
        {
            await _accountManager.AddAccountAsync();
            Refresh();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Ошибка добавления аккаунта: {ex.Message}";
        }
    }

    private async void OnRemoveAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string email)
        {
            await _accountManager.RemoveAccountAsync(email);
            Refresh();
        }
    }

    private void Refresh()
    {
        DateLabel.Text = _currentMode == ViewMode.Month
            ? ViewModel.CurrentDate.ToString("MMMM yyyy")
            : ViewModel.CurrentDate.ToString("d MMMM yyyy");

        switch (_currentMode)
        {
            case ViewMode.Day: _ = ViewModel.LoadDayCommand.ExecuteAsync(null); break;
            case ViewMode.Week:
                if (ContentFrame.Content is not WeekView) ContentFrame.Navigate(typeof(WeekView), ViewModel);
                _ = ViewModel.LoadWeekCommand.ExecuteAsync(null);
                break;
            case ViewMode.Month: _ = ViewModel.LoadMonthCommand.ExecuteAsync(null); break;
        }
    }
}
