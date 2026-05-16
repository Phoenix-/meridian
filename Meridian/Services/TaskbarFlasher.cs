using Meridian.Diagnostics;

namespace Meridian.Services;

// Thin wrapper over FlashWindowEx that resolves the main window's HWND on
// demand from App.MainWindow. Centralized so the call sites (ReminderScheduler,
// MainWindow.Activated, ToastActivator hop) don't each chase the window
// handle themselves.
//
// Idempotent: Start() on an already-flashing window is a no-op from the
// shell's perspective (FLASHW_TIMERNOFG re-asserts the same state); Stop()
// on a non-flashing window is a cheap no-op too.
//
// All public methods are safe to call from any thread — the underlying API
// is thread-safe, and resolving HWND via WindowNative on a Window object is
// allowed off-thread (the handle itself doesn't move).
internal static class TaskbarFlasher
{
    public static void Start()
    {
        // Const-typed gate produces an unreachable-code warning today; the
        // gate is here to document the wire-up point for when AppSettings
        // gains a real backing store. Suppress locally, not project-wide.
#pragma warning disable CS0162
        if (!AppSettings.FlashTaskbarOnReminder) return;
#pragma warning restore CS0162

        var hwnd = TryGetHwnd();
        if (hwnd == 0) return;

        var info = new NativeMethods.FLASHWINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.FLASHWINFO>(),
            hwnd   = hwnd,
            // ALL = caption + tray. TIMERNOFG = keep flashing until the
            // window is brought to the foreground, then auto-stop. On modern
            // Windows the user's "flash count" setting collapses this to a
            // static amber highlight on the taskbar button, which is what
            // we want — a persistent indicator, not a blink.
            dwFlags = NativeMethods.FLASHW_ALL | NativeMethods.FLASHW_TIMERNOFG,
            uCount  = uint.MaxValue,
            dwTimeout = 0,
        };
        NativeMethods.FlashWindowEx(ref info);
    }

    public static void Stop()
    {
        var hwnd = TryGetHwnd();
        if (hwnd == 0) return;

        var info = new NativeMethods.FLASHWINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.FLASHWINFO>(),
            hwnd   = hwnd,
            dwFlags = NativeMethods.FLASHW_STOP,
        };
        NativeMethods.FlashWindowEx(ref info);
    }

    private static nint TryGetHwnd()
    {
        try
        {
            var window = App.MainWindow;
            if (window is null) return 0;
            return WinRT.Interop.WindowNative.GetWindowHandle(window);
        }
        catch (Exception ex)
        {
            Log.Error("Flash", ex, "resolve HWND");
            return 0;
        }
    }
}
