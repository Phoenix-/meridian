using System.Diagnostics;
using Meridian.UiTests.Fixtures;

namespace Meridian.UiTests.Helpers;

internal static class ProcessHelpers
{
    // Force-quits any running Meridian.exe and waits for the process(es) to
    // actually exit before returning. Necessary before mutating viewstate.json
    // — otherwise the still-alive process can flush its state over our write
    // on shutdown.
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
