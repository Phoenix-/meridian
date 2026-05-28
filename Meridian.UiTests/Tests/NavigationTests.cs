using Meridian.UiTests.Fixtures;
using Meridian.UiTests.Helpers;
using Xunit;

namespace Meridian.UiTests.Tests;

// First test: pure navigation, no dependency on calendar content. Validates
// the whole pipeline (kill → seed state → launch → find → invoke → wait → assert
// → screenshot on failure → teardown) so any breakage in plumbing surfaces
// immediately when content-dependent tests are added later.
public sealed class NavigationTests : IAsyncLifetime
{
    private readonly MeridianAppFixture _fx = new() { SeedView = "Day" };

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task ClickMonth_ChangesDateLabel()
    {
        var window = _fx.Window;
        var dateLabel = window.LabelById("DateLabel");
        var before = dateLabel.Text;

        var monthBtn = window.ButtonById("BtnMonth");
        // Invoke() goes through the UIA pattern, bypassing the custom title bar's
        // hit-test (which is the usual source of flaky WinUI 3 + FlaUI clicks).
        monthBtn.Invoke();

        var changed = await Find.WaitUntilAsync(
            () => dateLabel.Text,
            text => text != before,
            timeout: TimeSpan.FromSeconds(3));

        if (!changed)
        {
            var shot = Screenshot.SaveWindow(window, nameof(ClickMonth_ChangesDateLabel));
            Assert.Fail(
                $"Date label did not change after clicking Месяц. " +
                $"before='{before}' after='{dateLabel.Text}'. Screenshot: {shot}");
        }
    }
}
