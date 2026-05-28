using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Xunit;

namespace Meridian.UiTests.Fixtures;

// Per-test fixture: launches Meridian under an isolated data directory
// (MERIDIAN_DATA_DIR), seeded with a known view state, and tears it all down.
//
// Why isolated: the SUT looks for cache/tokens/logs/viewstate under that env
// var when set (see Meridian.Services.AppPaths). With a per-test temp root,
// the test process never touches the user's real %APPDATA%\Meridian and can
// safely run in parallel with the user's actual instance — no killing,
// no shared file conflicts.
//
// Pattern: IAsyncLifetime — kill+launch are naturally async and the dispose
// path needs to wait for the SUT to exit before deleting the data dir.
public sealed class MeridianAppFixture : IAsyncLifetime
{
    private Application? _app;
    private Process? _process;
    private UIA3Automation? _automation;
    private Window? _window;
    private string? _dataDir;

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

        // Fresh isolated data dir for this test run. The SUT will read/write
        // here exclusively because we set MERIDIAN_DATA_DIR before launching.
        _dataDir = Path.Combine(Path.GetTempPath(), "Meridian-uitests-" + Guid.NewGuid().ToString("N"));
        var paths = MeridianPaths.Under(_dataDir);

        // Seed a known view (Day) so the test's "click Месяц" assertion has a
        // guaranteed delta on the label format.
        ViewState.Write(paths.ViewStateJson, SeedView, SeedDate);
        // No windowstate.json → app uses default window size (reproducible).

        var psi = new ProcessStartInfo(MeridianPaths.DebugExe) { UseShellExecute = false };
        psi.Environment["MERIDIAN_DATA_DIR"] = _dataDir;
        _process = Process.Start(psi) ?? throw new InvalidOperationException(
            $"Failed to start {MeridianPaths.DebugExe}");

        _app = Application.Attach(_process);
        _automation = new UIA3Automation();
        _window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(15))
            ?? throw new InvalidOperationException(
                "Main window did not appear within 15s. " +
                $"Check {Path.Combine(_dataDir, "logs", "meridian.log")}.");
    }

    public async Task DisposeAsync()
    {
        try
        {
            _app?.Close();
            await Task.Delay(500);
        }
        catch { /* ignore — we're cleaning up */ }
        finally
        {
            _automation?.Dispose();
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(2000);
                }
                _process?.Dispose();
            }
            catch { /* best-effort */ }

            // Best-effort cleanup of the isolated data dir. Windows can still
            // hold a handle on the log file briefly after exit — swallow that.
            if (_dataDir != null && Directory.Exists(_dataDir))
            {
                try { Directory.Delete(_dataDir, recursive: true); } catch { /* leak to %TEMP% */ }
            }
        }
    }
}
