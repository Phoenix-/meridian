using Meridian.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace Meridian;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        InstallCrashHandlers();
        Log.Write("App", $"start pid={Environment.ProcessId} exe={Environment.ProcessPath}");

        ComWrappersSupport.InitializeComWrappers();

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }

    // Captures the failure modes that otherwise vanish silently:
    //   * AppDomain.UnhandledException — process-wide fatal exceptions
    //   * TaskScheduler.UnobservedTaskException — Tasks that faulted and were
    //     garbage-collected without anyone awaiting them. Common in our
    //     fire-and-forget sync paths.
    private static void InstallCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Log.Error("Crash", ex, e.IsTerminating ? "terminating" : "non-terminating");
            else
                Log.Write("Crash", $"non-Exception thrown: {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Crash", e.Exception, "unobserved task");
            e.SetObserved();
        };
    }
}
