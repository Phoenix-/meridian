using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Auth;
using Meridian.Models;
using Meridian.Services;

namespace Meridian.Testing;

// In-process fake of ICalendarProvider for UI tests. Registered in App.xaml.cs
// when AppPaths.IsIsolated (i.e. running under MERIDIAN_DATA_DIR), so the test
// harness gets deterministic data without touching Google.
//
// Data source: a JSON fixture file pointed to by MERIDIAN_FAKE_FIXTURE.
// Shape:
//   { "events": [ /* CalendarEvent objects, camelCase */ ] }
// If the env is missing or the file doesn't exist, the provider acts as if
// the calendar is empty — useful for tests that just want the UI to come up.
public sealed class FakeCalendarProvider : ICalendarProvider
{
    public const string ProviderId = "fake";
    public const string FixtureEnv = "MERIDIAN_FAKE_FIXTURE";

    private static readonly AccountId FakeAccount = new(ProviderId, "fake@test.local");
    private const string FakeCalendarId = "fake-cal";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public string ProviderName => ProviderId;

    public IEnumerable<AccountId> GetSavedAccounts() => [FakeAccount];

    public void LoadAccount(AccountId id) { /* no-op — fake is always "valid" */ }

    public Task<AccountId> AddAccountAsync(CancellationToken ct = default) =>
        Task.FromResult(FakeAccount);

    public Task<AccountId> ReauthenticateAccountAsync(AccountId id, CancellationToken ct = default) =>
        Task.FromResult(FakeAccount);

    public Task RevokeAccountAsync(AccountId id, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<EventSyncResult> InitialSyncEventsAsync(
        AccountId id, string calendarId, DateTime from, DateTime to,
        IReadOnlyList<int>? defaultPopupMinutes = null, CancellationToken ct = default)
    {
        var events = LoadFixtureEvents()
            .Where(e => e.Start.Date < to.Date && e.End.Date > from.Date)
            .ToList();
        return Task.FromResult(new EventSyncResult(
            Upserts: events,
            CancelledIds: [],
            NextSyncToken: "fake-sync-token-1",
            SyncTokenExpired: false,
            MasterRecurrenceSeen: false));
    }

    // Incremental: nothing changes — the fixture is the immutable source of truth
    // for the test. Returning a fresh non-null token keeps CalendarCache happy.
    public Task<EventSyncResult> IncrementalSyncEventsAsync(
        AccountId id, string calendarId, string syncToken,
        IReadOnlyList<int>? defaultPopupMinutes = null, CancellationToken ct = default) =>
        Task.FromResult(new EventSyncResult([], [], syncToken, false, false));

    public Task<List<CalendarInfo>> GetCalendarsAsync(AccountId id, CancellationToken ct = default) =>
        Task.FromResult(new List<CalendarInfo>
        {
            new()
            {
                Id = FakeCalendarId,
                Title = "Fake Calendar",
                BackgroundColor = "#9fe1e7",
                ForegroundColor = "#000000",
                Selected = true,
                AccessRole = "owner",
                DefaultPopupReminderMinutes = null,
            }
        });

    public Task<List<TaskItem>> GetTasksAsync(AccountId id, CancellationToken ct = default) =>
        Task.FromResult(new List<TaskItem>());

    private static List<CalendarEvent> LoadFixtureEvents()
    {
        var path = Environment.GetEnvironmentVariable(FixtureEnv);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return [];

        try
        {
            var json = File.ReadAllText(path);
            var fixture = JsonSerializer.Deserialize<Fixture>(json, JsonOpts);
            return fixture?.Events ?? [];
        }
        catch
        {
            return [];
        }
    }

    private sealed class Fixture
    {
        [JsonPropertyName("events")]
        public List<CalendarEvent>? Events { get; set; }
    }
}
