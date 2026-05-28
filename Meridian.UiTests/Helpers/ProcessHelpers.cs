using System.Diagnostics;
using Meridian.UiTests.Fixtures;

namespace Meridian.UiTests.Helpers;

internal static class ProcessHelpers
{
    // Force-quits ALL Meridian.exe processes on the machine. Not used by the
    // default test flow — fixtures launch their own isolated process by PID
    // and clean it up themselves (MERIDIAN_DATA_DIR isolates state, so the
    // user's parallel instance is harmless). Kept here for emergency
    // teardown in case a future test gets stuck holding a window.
    public static async Task KillAllMeridianAsync(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

        foreach (var p in Process.GetProcessesByName(MeridianPaths.ProcessName))
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already gone, or access denied — fall through; we re-poll below.
            }
            finally
            {
                p.Dispose();
            }
        }

        while (DateTime.UtcNow < deadline)
        {
            if (Process.GetProcessesByName(MeridianPaths.ProcessName).Length == 0) return;
            await Task.Delay(100);
        }

        var remaining = Process.GetProcessesByName(MeridianPaths.ProcessName).Length;
        if (remaining > 0)
            throw new InvalidOperationException(
                $"{remaining} Meridian process(es) still running after kill timeout.");
    }
}
