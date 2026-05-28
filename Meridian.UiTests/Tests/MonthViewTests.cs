using FlaUI.Core.Definitions;
using Meridian.UiTests.Fixtures;
using Meridian.UiTests.Helpers;
using Xunit;

namespace Meridian.UiTests.Tests;

// Content-dependent tests for the month view. Use the FakeCalendarProvider via
// a JSON fixture so we don't depend on the user's real Google data.
public sealed class MonthViewTests : IAsyncLifetime
{
    private readonly MeridianAppFixture _fx = new()
    {
        SeedView = "Month",
        SeedDate = new DateTime(2026, 6, 1),
        Fixture = "armenia-cross-week",
    };

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    // Regression: prior to commit 97c11bb a multi-day event whose Sunday→Monday
    // span crossed a week boundary lost its continuation segment (the second
    // week rendered nothing). After the gCal-style rewrite the title repeats
    // on every week, so the UIA tree should contain at least two "Армения"
    // elements: one on Sun 14 June, one on the Mon 15 – Fri 19 segment.
    [Fact]
    public async Task ArmeniaSpan_RendersOnBothWeeks()
    {
        var window = _fx.Window;

        // The calendar loads asynchronously after launch (RefreshAll →
        // InitialSyncEventsAsync → snapshot → render). Poll the UIA tree until
        // at least one matching element appears, with a generous ceiling.
        var found = await Find.WaitUntilAsync(
            () => window.FindAllDescendants(cf =>
                cf.ByName("Армения").And(cf.ByControlType(ControlType.Text))).Length,
            count => count >= 2,
            timeout: TimeSpan.FromSeconds(10));

        if (!found)
        {
            var seen = window.FindAllDescendants(cf => cf.ByName("Армения")).Length;
            var shot = Screenshot.SaveWindow(window, nameof(ArmeniaSpan_RendersOnBothWeeks));
            Assert.Fail(
                $"Expected ≥2 'Армения' band segments (one per week the span touches), " +
                $"observed {seen} after 10s. Screenshot: {shot}");
        }
    }
}
