using Meridian.Auth;
using Meridian.Services;
using Meridian.Testing;
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
        // First packaged launch: copy tokens/cache from the old unpackaged
        // %APPDATA%\Meridian so the user isn't silently logged out. No-op when
        // unpackaged or already migrated. Must run BEFORE any token read.
        DataMigration.MigrateFromUnpackagedIfNeeded();

        // Toast prerequisite for unpackaged apps: AUMID + Start Menu shortcut.
        // Must run before any ScheduledToastNotification is added.
        // Skip when running under an isolated data dir (UI tests) — toasts
        // aren't exercised there, and we must not stomp the user's real
        // system-wide registrations from a test process.
        // Skip when packaged too: the package owns identity (EnsureRegistered
        // still sets ResolvedAumid to the package AUMID and returns early).
        // The user can opt out of registering with the Windows notification
        // platform entirely (e.g. a debug build deferring to the installed
        // Nightly so they don't both register and double-notify).
        if (!AppPaths.IsIsolated && AppSettings.RegisterForNotifications)
            ToastSetup.EnsureRegistered();

        var providers = new ProviderRegistry();
        // Under MERIDIAN_DATA_DIR (UI tests) swap Google for an in-process fake:
        // deterministic data via MERIDIAN_FAKE_FIXTURE, no OAuth, no network.
        if (AppPaths.IsIsolated)
            providers.Register(new FakeCalendarProvider());
        else
            providers.Register(new GoogleCalendarProvider());

        MainWindow = new MainWindow(providers);
        MainWindow.Activate();
    }
}
