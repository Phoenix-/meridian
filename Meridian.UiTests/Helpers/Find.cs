using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace Meridian.UiTests.Helpers;

internal static class Find
{
    // Find a button by its AutomationId. Preferred over ByName so the test
    // doesn't break when the visible label is localized or renamed.
    public static Button ButtonById(this Window window, string automationId)
    {
        var el = window.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.Button).And(cf.ByAutomationId(automationId)));
        if (el == null)
            throw new InvalidOperationException(
                $"Button with AutomationId='{automationId}' not found in the window. " +
                "If you just added the control, rebuild the Meridian project first.");
        return el.AsButton();
    }

    // The date label (TextBlock) — we read its Text and assert it changes.
    public static Label LabelById(this Window window, string automationId)
    {
        var el = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
        if (el == null)
            throw new InvalidOperationException(
                $"Element with AutomationId='{automationId}' not found in the window.");
        return el.AsLabel();
    }

    // Polls `getValue` every `interval` until `predicate` becomes false or
    // `timeout` elapses. Returns whether the predicate cleared in time.
    // This is the standard UI-sync primitive — never Thread.Sleep in tests.
    public static async Task<bool> WaitUntilAsync<T>(
        Func<T> getValue,
        Func<T, bool> condition,
        TimeSpan timeout,
        TimeSpan? interval = null)
    {
        var step = interval ?? TimeSpan.FromMilliseconds(100);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition(getValue())) return true;
            await Task.Delay(step);
        }
        return condition(getValue());
    }
}
