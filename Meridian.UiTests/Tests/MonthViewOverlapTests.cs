using FlaUI.Core.Definitions;
using Meridian.UiTests.Fixtures;
using Meridian.UiTests.Helpers;
using Xunit;

namespace Meridian.UiTests.Tests;

// Each xUnit class hosts a per-test MeridianAppFixture via IAsyncLifetime,
// so tests that need different seeded views/fixtures live in different
// classes. This one covers the "many overlapping bands in one week" scenario
// — separate from the cross-week scenario in MonthViewTests so each gets a
// clean, deterministic load.
public sealed class MonthViewOverlapTests : IAsyncLifetime
{
    private readonly MeridianAppFixture _fx = new()
    {
        SeedView = "Month",
        SeedDate = new DateTime(2026, 5, 1),
        Fixture = "overlapping-bands",
    };

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    // Regression for the dup-as-chip bug discovered when first verifying the
    // 97c11bb fix manually: in the initial implementation a band that didn't
    // fit the lane cap was inlined as a per-day chip in EVERY day it covered,
    // so "Оплачиваемый отпуск" (Tue-Fri) showed up four times — once per cell.
    // The final design (commit 3347c29's predecessor) makes bands atomic:
    // either drawn once across all their days as a single Border with
    // ColumnSpan, or hidden entirely into "+N ещё" on each covered day.
    //
    // Either outcome is fine; what must NOT happen is per-day duplication of
    // the title. An UIA element count ≤ 2 keeps the test stable against the
    // "+N" decision (the band may or may not fit depending on cell height /
    // DPI / accent settings) while still catching a regression that would
    // show 3+ duplicates.
    [Fact]
    public async Task MultiDaySpan_NotDuplicatedAsChip()
    {
        var window = _fx.Window;

        // Wait for the calendar to load with the fixture's events.
        // Use "Тест 2" (the full-week span) as the readiness probe: it's
        // present in any layout that didn't crash, so once it's there the
        // month view is fully rendered.
        var loaded = await Find.WaitUntilAsync(
            () => window.FindAllDescendants(cf =>
                cf.ByName("Тест 2").And(cf.ByControlType(ControlType.Text))).Length,
            count => count >= 1,
            timeout: TimeSpan.FromSeconds(10));

        if (!loaded)
        {
            var shot = Screenshot.SaveWindow(window, "MultiDaySpan_LoadTimeout");
            Assert.Fail($"Calendar didn't load any band within 10s. Screenshot: {shot}");
        }

        // The actual regression assertion: "Оплачиваемый отпуск" (Tue-Fri)
        // must NOT appear once per day it covers. 0 (folded into "+N") or 1
        // (single atomic band) is correct. 4 (one per Tue/Wed/Thu/Fri cell)
        // would be the dup-as-chip bug coming back.
        var occurrences = window.FindAllDescendants(cf =>
            cf.ByName("Оплачиваемый отпуск").And(cf.ByControlType(ControlType.Text))).Length;

        if (occurrences > 2)
        {
            var shot = Screenshot.SaveWindow(window, nameof(MultiDaySpan_NotDuplicatedAsChip));
            Assert.Fail(
                $"'Оплачиваемый отпуск' appears {occurrences} times in the UIA tree — " +
                $"a multi-day band must be atomic (≤2 elements: one continuous bar + " +
                $"optional repeated label across week wraps), not duplicated per day. " +
                $"Screenshot: {shot}");
        }
    }
}
