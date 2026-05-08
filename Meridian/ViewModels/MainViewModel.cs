using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Auth;
using Meridian.Models;
using Meridian.Services;
using System.Collections.ObjectModel;

namespace Meridian.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AccountManager _accounts;

    [ObservableProperty]
    private DateTime _currentDate = DateTime.Today;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<string> Accounts => _accounts.Accounts;
    public ObservableCollection<CalendarEvent> Events { get; } = [];
    public ObservableCollection<TaskItem> Tasks { get; } = [];

    public MainViewModel(AccountManager accounts)
    {
        _accounts = accounts;
    }

    [RelayCommand]
    private async Task LoadDayAsync()
    {
        await LoadRangeAsync(CurrentDate.Date, CurrentDate.Date.AddDays(1));
    }

    [RelayCommand]
    private async Task LoadWeekAsync()
    {
        var monday = CurrentDate.AddDays(-(int)CurrentDate.DayOfWeek + (int)DayOfWeek.Monday);
        if (CurrentDate.DayOfWeek == DayOfWeek.Sunday)
            monday = CurrentDate.AddDays(-6);
        await LoadRangeAsync(monday.Date, monday.Date.AddDays(7));
    }

    [RelayCommand]
    private async Task LoadMonthAsync()
    {
        var first = new DateTime(CurrentDate.Year, CurrentDate.Month, 1);
        await LoadRangeAsync(first, first.AddMonths(1));
    }

    private async Task LoadRangeAsync(DateTime from, DateTime to)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var calendarServices = _accounts.Credentials
                .Select(kv => (email: kv.Key, svc: new GoogleCalendarService(kv.Value)))
                .ToList();

            var eventTasks = calendarServices.Select(x => x.svc.GetEventsAsync(from, to, x.email));
            var taskTasks = _accounts.Credentials.Select(kv =>
                new GoogleTasksService(kv.Value).GetTasksAsync(kv.Key));
            var reminderTasks = calendarServices.Select(x => x.svc.GetTaskReminderTimesAsync(from, to));

            var allEvents = (await Task.WhenAll(eventTasks)).SelectMany(x => x);
            var allTasksRaw = (await Task.WhenAll(taskTasks)).SelectMany(x => x).ToList();
            var reminderMaps = await Task.WhenAll(reminderTasks);

            // Merge all reminder maps and apply to tasks by Id
            var allReminders = reminderMaps.SelectMany(d => d).ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var t in allTasksRaw)
                if (t.Id != null && allReminders.TryGetValue(t.Id, out var rt))
                    t.ReminderTime = rt;

            IEnumerable<TaskItem> allTasks = allTasksRaw;

            var dateFrom = DateOnly.FromDateTime(from);
            var dateTo = DateOnly.FromDateTime(to.AddDays(-1));
            var filteredTasks = allTasks.Where(t => t.Due == null || (t.Due >= dateFrom && t.Due <= dateTo));

            Events.Clear();
            foreach (var e in allEvents.OrderBy(e => e.Start)) Events.Add(e);

            Tasks.Clear();
            foreach (var t in filteredTasks) Tasks.Add(t);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка загрузки: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void PreviousDay() { CurrentDate = CurrentDate.AddDays(-1); }

    [RelayCommand]
    private void NextDay() { CurrentDate = CurrentDate.AddDays(1); }

    [RelayCommand]
    private void PreviousWeek() { CurrentDate = CurrentDate.AddDays(-7); }

    [RelayCommand]
    private void NextWeek() { CurrentDate = CurrentDate.AddDays(7); }
}
