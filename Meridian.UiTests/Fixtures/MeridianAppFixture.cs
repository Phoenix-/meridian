using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Meridian.UiTests.Helpers;
using Xunit;

namespace Meridian.UiTests.Fixtures;

// Per-test fixture: ensures a clean Meridian process started in a known view
// state, gives the test the main window, then tears everything down.
//
// Pattern: IAsyncLifetime is the right xUnit hook because kill + launch are
// naturally async (we WaitForExit and poll for the main window).
//
// The fixture is created PER TEST (NavigationTests instantiates it in its
// own ctor wrapper) — UI tests must not share live state.
public sealed class MeridianAppFixture : IAsyncLifetime
{
    private Application? _app;
    private UIA3Automation? _automation;
    private Window? _window;

    // The seeded starting view. Default is "Day" so a click on the "Месяц"
    // button is guaranteed to change the date label (Day uses `d MMMM yyyy`,
    // Month uses `MMMM yyyy` — different strings even for the same date).
    public string SeedView { get; init; } = "Day";
    public DateTime SeedDate { get; init; } = DateTime.Today;

    public Window Window => _window ?? throw new InvalidOperationException(
        "App not initialized. Did InitializeAsync run?");

    public async Task InitializeAsync()
    {
        if (!File.Exists(MeridianPaths.DebugExe))
            throw new FileNotFoundException(
                $"Meridian debug exe not found at {MeridianPaths.DebugExe}. " +
                "Run `dotnet build Calendar.sln -c Debug` first.",
                MeridianPaths.DebugExe);

        await ProcessHelpers.KillAllMeridianAsync();

        // Wipe windowstate.json so the app uses default window size — keeps
        // screenshot dimensions reproducible.
        if (File.Exists(MeridianPaths.WindowStateJson))
            File.Delete(MeridianPaths.WindowStateJson);

        ViewState.Write(SeedView, SeedDate);

        _app = Application.Launch(MeridianPaths.DebugExe);
        _automation = new UIA3Automation();
        _window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(15))
            ?? throw new InvalidOperationException(
                "Main window did not appear within 15s. Check %APPDATA%\\Meridian\\error.log.");
    }

    public async Task DisposeAsync()
    {
        try
        {
            _app?.Close();
            // Give the WM time to flush; then guarantee exit.
            await Task.Delay(500);
        }
        catch { /* ignore — we're cleaning up */ }
        finally
        {
            _automation?.Dispose();
            try { await ProcessHelpers.KillAllMeridianAsync(); } catch { /* best-effort */ }
        }
    }
}
