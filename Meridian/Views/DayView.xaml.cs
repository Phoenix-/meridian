using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Meridian.Models;
using Meridian.ViewModels;
using System.Collections.ObjectModel;

namespace Meridian.Views;

public sealed partial class DayView : Page, ICalendarView
{
    public DayViewModel LocalViewModel { get; } = new();

    private MainViewModel? _vm;
    private DateTime _date;

    public DayView()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _vm = e.Parameter as MainViewModel;
        _date = DateTime.Today;
        Bindings.Update();
        _vm?.SetActiveView(this);
    }

    public (DateTime From, DateTime To) GetRange() =>
        (_date.Date, _date.Date.AddDays(1));

    public string GetLabel() => _date.ToString("d MMMM yyyy");

    public void NavigatePrevious() { _date = _date.AddDays(-1); }
    public void NavigateNext()     { _date = _date.AddDays(1); }

    public void ApplySnapshot(CalendarSnapshot snapshot)
    {
        LocalViewModel.IsLoading = !snapshot.IsComplete && snapshot.Events.Count == 0;
        LocalViewModel.ErrorMessage = snapshot.ErrorMessage;

        LocalViewModel.Events.Clear();
        foreach (var e in snapshot.Events) LocalViewModel.Events.Add(e);

        LocalViewModel.Tasks.Clear();
        foreach (var t in snapshot.Tasks) LocalViewModel.Tasks.Add(t);
    }
}

public partial class DayViewModel : ObservableObject
{
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    public ObservableCollection<CalendarEvent> Events { get; } = [];
    public ObservableCollection<TaskItem> Tasks { get; } = [];
}
