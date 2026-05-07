using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Meridian.ViewModels;

namespace Meridian.Views;

public sealed partial class DayView : Page
{
    public MainViewModel? ViewModel { get; private set; }

    public DayView()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel = e.Parameter as MainViewModel;
        Bindings.Update();
    }
}
