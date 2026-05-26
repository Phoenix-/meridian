using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Meridian.Auth;
using Meridian.Models;

namespace Meridian.Services;

// ── JSON DTOs ──────────────────────────────────────────────────────────────────

internal class EventList
{
    [JsonPropertyName("items")]         public List<EventDto>? Items { get; set; }
    [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
    [JsonPropertyName("nextSyncToken")] public string? NextSyncToken { get; set; }
}

internal class EventDto
{
    [JsonPropertyName("id")]          public string? Id { get; set; }
    [JsonPropertyName("status")]      public string? Status { get; set; }
    [JsonPropertyName("summary")]     public string? Summary { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("colorId")]     public string? ColorId { get; set; }
    [JsonPropertyName("start")]       public EventTime? Start { get; set; }
    [JsonPropertyName("end")]         public EventTime? End { get; set; }
    [JsonPropertyName("reminders")]   public ReminderInfo? Reminders { get; set; }
    [JsonPropertyName("recurrence")]  public List<string>? Recurrence { get; set; }
    [JsonPropertyName("htmlLink")]    public string? HtmlLink { get; set; }
}

internal class ReminderInfo
{
    [JsonPropertyName("useDefault")] public bool? UseDefault { get; set; }
    [JsonPropertyName("overrides")]  public List<ReminderOverride>? Overrides { get; set; }
}

internal class ReminderOverride
{
    [JsonPropertyName("method")]  public string? Method { get; set; }
    [JsonPropertyName("minutes")] public int? Minutes { get; set; }
}

internal class EventTime
{
    [JsonPropertyName("dateTime")] public DateTimeOffset? DateTime { get; set; }
    [JsonPropertyName("date")]     public string? Date { get; set; }
}

internal class CalendarListResponse
{
    [JsonPropertyName("items")]         public List<CalendarListEntry>? Items { get; set; }
    [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
}

internal class CalendarListEntry
{
    [JsonPropertyName("id")]              public string? Id { get; set; }
    [JsonPropertyName("summary")]         public string? Summary { get; set; }
    [JsonPropertyName("summaryOverride")] public string? SummaryOverride { get; set; }
    [JsonPropertyName("backgroundColor")] public string? BackgroundColor { get; set; }
    [JsonPropertyName("foregroundColor")] public string? ForegroundColor { get; set; }
    [JsonPropertyName("selected")]        public bool? Selected { get; set; }
    [JsonPropertyName("accessRole")]      public string? AccessRole { get; set; }
    [JsonPropertyName("primary")]         public bool? Primary { get; set; }
    [JsonPropertyName("deleted")]         public bool? Deleted { get; set; }
    [JsonPropertyName("defaultReminders")] public List<ReminderOverride>? DefaultReminders { get; set; }
}

internal class TaskListList
{
    [JsonPropertyName("items")]         public List<TaskListDto>? Items { get; set; }
    [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
}

internal class TaskListDto
{
    [JsonPropertyName("id")]    public string? Id    { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
}

internal class TaskList
{
    [JsonPropertyName("items")]         public List<TaskDto>? Items { get; set; }
    [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
}

internal class TaskDto
{
    [JsonPropertyName("id")]     public string? Id     { get; set; }
    [JsonPropertyName("title")]  public string? Title  { get; set; }
    [JsonPropertyName("notes")]  public string? Notes  { get; set; }
    [JsonPropertyName("due")]    public string? Due    { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

[JsonSerializable(typeof(EventList))]
[JsonSerializable(typeof(CalendarListResponse))]
[JsonSerializable(typeof(TaskListList))]
[JsonSerializable(typeof(TaskList))]
internal partial class GoogleApiJsonContext : JsonSerializerContext { }

// ── Client ─────────────────────────────────────────────────────────────────────

// Result of a sync (initial or incremental) for a single calendar stream.
// Upserts are events to insert/replace by Id. CancelledIds are tombstones to
// remove from the local cache. NextSyncToken is what to persist for the next
// incremental call. SyncTokenExpired = true means caller must perform initial
// sync; in that case the other fields are empty. MasterRecurrenceSeen = true
// means at least one returned item carried a `recurrence` rule (i.e. is the
// master of a series rather than a single instance) — Google does this under
// singleEvents=true on incremental sync after a new series is created, and
// the caller must re-run InitialSyncEventsAsync to get the expanded instances.
public readonly record struct EventSyncResult(
    List<CalendarEvent> Upserts,
    List<string> CancelledIds,
    string? NextSyncToken,
    bool SyncTokenExpired,
    bool MasterRecurrenceSeen);

public sealed class GoogleApiClient(AccountId id)
{
    private const string CalendarBase = "https://www.googleapis.com/calendar/v3";
    private const string TasksBase    = "https://www.googleapis.com/tasks/v1";

    private static readonly HttpClient _http = new();

    // Full sync for a calendar window. Returns all events plus a nextSyncToken
    // bound to this exact (calendarId, timeMin, timeMax, singleEvents) tuple.
    public async Task<EventSyncResult> InitialSyncEventsAsync(
        string calendarId, DateTime from, DateTime to,
        IReadOnlyList<int>? defaultPopupMinutes = null, CancellationToken ct = default)
    {
        var baseUrl = $"{CalendarBase}/calendars/{Uri.EscapeDataString(calendarId)}/events" +
                      $"?singleEvents=true" +
                      $"&timeMin={Uri.EscapeDataString(from.ToUniversalTime().ToString("o"))}" +
                      $"&timeMax={Uri.EscapeDataString(to.ToUniversalTime().ToString("o"))}";
        // orderBy=startTime is incompatible with sync tokens, so we omit it and
        // let the caller sort. Same query shape will be used for incremental.
        return await PageSyncAsync(baseUrl, defaultPopupMinutes, ct);
    }

    // Incremental sync using a previously-stored token. If Google returns 410
    // Gone the token is too old; caller must re-run InitialSyncEventsAsync.
    public async Task<EventSyncResult> IncrementalSyncEventsAsync(
        string calendarId, string syncToken,
        IReadOnlyList<int>? defaultPopupMinutes = null, CancellationToken ct = default)
    {
        var baseUrl = $"{CalendarBase}/calendars/{Uri.EscapeDataString(calendarId)}/events" +
                      $"?syncToken={Uri.EscapeDataString(syncToken)}";
        return await PageSyncAsync(baseUrl, defaultPopupMinutes, ct);
    }

    private async Task<EventSyncResult> PageSyncAsync(
        string baseUrl, IReadOnlyList<int>? defaultPopupMinutes, CancellationToken ct)
    {
        var token = await GoogleOAuthClient.GetAccessTokenAsync(id, ct);
        var upserts = new List<CalendarEvent>();
        var cancelled = new List<string>();
        string? pageToken = null;
        string? nextSyncToken = null;
        bool masterRecurrenceSeen = false;

        do
        {
            var url = baseUrl + (pageToken is null ? "" : $"&pageToken={Uri.EscapeDataString(pageToken)}");

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new("Bearer", token);
            using var resp = await _http.SendAsync(req, ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.Gone)
                return new EventSyncResult([], [], null, true, false);

            await EnsureSuccessOrAuthExpiredAsync(resp, ct);
            var list = await resp.Content.ReadFromJsonAsync(GoogleApiJsonContext.Default.EventList, ct);

            foreach (var item in list?.Items ?? [])
            {
                if (item.Id is null) continue;
                if (item.Status == "cancelled")
                {
                    cancelled.Add(item.Id);
                    continue;
                }

                // A `recurrence` array means this is the master of a series.
                // Under singleEvents=true Google should be returning expanded
                // instances instead, but on incremental sync of a newly-created
                // series it sometimes returns the master once. Signal the
                // caller to refresh by initial sync rather than persisting an
                // unexpanded master as if it were a one-off event.
                if (item.Recurrence is { Count: > 0 })
                {
                    masterRecurrenceSeen = true;
                    continue;
                }

                var start = item.Start?.DateTime?.LocalDateTime
                            ?? (item.Start?.Date is { } sd ? DateTime.Parse(sd) : DateTime.Today);
                var end   = item.End?.DateTime?.LocalDateTime
                            ?? (item.End?.Date is { } ed ? DateTime.Parse(ed) : DateTime.Today);

                upserts.Add(new CalendarEvent
                {
                    Id          = item.Id,
                    Title       = item.Summary ?? "(без названия)",
                    Description = item.Description,
                    Start       = start,
                    End         = end,
                    IsAllDay    = item.Start?.DateTime is null,
                    CalendarId  = null,
                    Color       = item.ColorId,
                    AccountEmail = id.Email,
                    HtmlLink    = item.HtmlLink,
                    ReminderMinutes = ResolveReminderMinutes(item.Reminders, defaultPopupMinutes),
                });
            }

            pageToken = list?.NextPageToken;
            // nextSyncToken only appears on the last page.
            if (pageToken is null) nextSyncToken = list?.NextSyncToken;
        }
        while (pageToken is not null);

        return new EventSyncResult(upserts, cancelled, nextSyncToken, false, masterRecurrenceSeen);
    }

    // Fetches all calendars (own + shared + subscribed) visible to this account.
    // Deleted calendars are filtered out; the rest is returned as-is.
    public async Task<List<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default)
    {
        var token = await GoogleOAuthClient.GetAccessTokenAsync(id, ct);
        var result = new List<CalendarInfo>();
        string? pageToken = null;

        do
        {
            var url = $"{CalendarBase}/users/me/calendarList" +
                      (pageToken is null ? "" : $"?pageToken={Uri.EscapeDataString(pageToken)}");

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new("Bearer", token);
            using var resp = await _http.SendAsync(req, ct);
            await EnsureSuccessOrAuthExpiredAsync(resp, ct);

            var page = await resp.Content.ReadFromJsonAsync(GoogleApiJsonContext.Default.CalendarListResponse, ct);

            foreach (var item in page?.Items ?? [])
            {
                if (item.Id is null || item.Deleted == true) continue;
                result.Add(new CalendarInfo
                {
                    Id = item.Id,
                    Title = item.SummaryOverride ?? item.Summary ?? item.Id,
                    BackgroundColor = item.BackgroundColor,
                    ForegroundColor = item.ForegroundColor,
                    Selected = item.Selected ?? false,
                    AccessRole = item.AccessRole ?? "",
                    DefaultPopupReminderMinutes = ExtractPopupMinutes(item.DefaultReminders),
                });
            }

            pageToken = page?.NextPageToken;
        }
        while (pageToken is not null);

        return result;
    }

    public async Task<List<TaskItem>> GetTasksAsync(CancellationToken ct = default)
    {
        var token = await GoogleOAuthClient.GetAccessTokenAsync(id, ct);
        var allTasks = new List<TaskItem>();

        // 1. Fetch all task lists
        var lists = new List<TaskListDto>();
        string? pageToken = null;
        do
        {
            var url = $"{TasksBase}/users/@me/lists?maxResults=100" +
                      (pageToken is null ? "" : $"&pageToken={Uri.EscapeDataString(pageToken)}");
            var page = await GetTaskListListAsync(url, token, ct);
            lists.AddRange(page?.Items ?? []);
            pageToken = page?.NextPageToken;
        }
        while (pageToken is not null);

        // 2. Fetch tasks for each list
        foreach (var list in lists)
        {
            if (list.Id is null) continue;
            string? taskPage = null;
            do
            {
                var url = $"{TasksBase}/lists/{Uri.EscapeDataString(list.Id)}/tasks" +
                          $"?showCompleted=false&showHidden=false&maxResults=100" +
                          (taskPage is null ? "" : $"&pageToken={Uri.EscapeDataString(taskPage)}");

                var page = await GetTaskListAsync(url, token, ct);

                foreach (var t in page?.Items ?? [])
                {
                    DateOnly? due = null;
                    if (t.Due is { Length: >= 10 } d && DateOnly.TryParse(d[..10], out var parsed))
                        due = parsed;

                    allTasks.Add(new TaskItem
                    {
                        Id            = t.Id ?? "",
                        Title         = t.Title ?? "(без названия)",
                        Notes         = t.Notes,
                        Due           = due,
                        Completed     = t.Status == "completed",
                        TaskListId    = list.Id,
                        TaskListTitle = list.Title,
                        AccountEmail  = id.Email,
                    });
                }

                taskPage = page?.NextPageToken;
            }
            while (taskPage is not null);
        }

        return allTasks;
    }

    private async Task<TaskListList?> GetTaskListListAsync(string url, string token, CancellationToken ct)
        => await SendGetAsync(url, token, GoogleApiJsonContext.Default.TaskListList, ct);

    private async Task<TaskList?> GetTaskListAsync(string url, string token, CancellationToken ct)
        => await SendGetAsync(url, token, GoogleApiJsonContext.Default.TaskList, ct);

    // Returns the popup-method reminder minutes to attach to an event. Returns
    // null when there are no popup reminders so the scheduler can skip cheaply.
    //
    // Google's contract: the `reminders` block is optional. When absent, the
    // event uses the calendar defaults — same as `{useDefault: true}`. In
    // practice the API omits the block for the overwhelming majority of
    // events, so missing-block must fall through to defaults, not to "no
    // reminders". Treating absent-block as no-reminders was a long-standing
    // bug that silently swallowed the calendar defaults for ~570/570 events
    // on the author's account.
    private static List<int>? ResolveReminderMinutes(
        ReminderInfo? reminders, IReadOnlyList<int>? defaultPopupMinutes)
    {
        if (reminders is null || reminders.UseDefault == true)
            return defaultPopupMinutes is { Count: > 0 } d ? [..d] : null;
        return ExtractPopupMinutes(reminders.Overrides);
    }

    private static List<int>? ExtractPopupMinutes(List<ReminderOverride>? src)
    {
        if (src is null || src.Count == 0) return null;
        List<int>? result = null;
        foreach (var r in src)
        {
            if (r.Method != "popup" || r.Minutes is not { } m) continue;
            (result ??= []).Add(m);
        }
        return result;
    }

    private async Task<T?> SendGetAsync<T>(
        string url, string accessToken,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new("Bearer", accessToken);
        var resp = await _http.SendAsync(req, ct);
        await EnsureSuccessOrAuthExpiredAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync(typeInfo, ct);
    }

    // Translates 401/403 (unauthorized / forbidden) responses from Google APIs
    // into the typed AccountAuthExpiredException so the caches can short-circuit
    // sync for this account. Everything else falls through to the standard
    // HttpRequestException via EnsureSuccessStatusCode().
    private async Task EnsureSuccessOrAuthExpiredAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;

        var code = (int)resp.StatusCode;
        if (code == 401 || code == 403)
        {
            string body = "";
            try { body = await resp.Content.ReadAsStringAsync(ct); } catch { }
            throw new Meridian.Auth.AccountAuthExpiredException(id, $"api: HTTP {code} ({Trim(body)})");
        }

        resp.EnsureSuccessStatusCode();

        static string Trim(string s) => s.Length <= 200 ? s : s[..200];
    }
}
