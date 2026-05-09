using Meridian.Auth;
using Microsoft.UI.Xaml;

namespace Meridian;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var providers = new ProviderRegistry();
        providers.Register(new GoogleCalendarProvider());

        MainWindow = new MainWindow(providers);
        MainWindow.Activate();
    }
}
