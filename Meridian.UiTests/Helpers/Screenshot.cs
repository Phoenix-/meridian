using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;

namespace Meridian.UiTests.Helpers;

internal static class Screenshot
{
    public static string ResultsDir { get; } = Path.Combine(
        AppContext.BaseDirectory, "TestResults", "screenshots");

    // Save a window-bounded PNG via BitBlt — works for WinUI 3 with a custom
    // title bar (no DWM extended-frame-bounds gymnastics required).
    // Returns the absolute path so test failure messages can name it.
    public static string SaveWindow(Window window, string testName)
    {
        Directory.CreateDirectory(ResultsDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(ResultsDir, $"{testName}_{stamp}.png");
        try
        {
            using var img = Capture.Element(window);
            img.ToFile(path);
        }
        catch
        {
            // Window may have died mid-test — fall back to full screen.
            using var img = Capture.Screen();
            img.ToFile(path);
        }
        return path;
    }
}
