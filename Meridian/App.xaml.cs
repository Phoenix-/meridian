using Meridian.Auth;
using Meridian.Services;
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
        // Toast prerequisite for unpackaged apps: AUMID + Start Menu shortcut.
        // Must run before any ScheduledToastNotification is added.
        // Skip when running under an isolated data dir (UI tests) — toasts
        // aren't exercised there, and we must not stomp the user's real
        // system-wide registrations from a test process.
        if (!AppPaths.IsIsolated)
            ToastSetup.EnsureRegistered();

        var providers = new ProviderRegistry();
        providers.Register(new GoogleCalendarProvider());

        MainWindow = new MainWindow(providers);
        MainWindow.Activate();
    }
}
