using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Meridian.Auth;
using Meridian.Models;

namespace Meridian.Services;

// ── JSON DTOs ──────────────────────────────────────────────────────────────────

internal class GraphEventPage
{
    [JsonPropertyName("value")]            public List<GraphEventDto>? Value { get; set; }
    [JsonPropertyName("@odata.nextLink")]  public string? NextLink  { get; set; }
    [JsonPropertyName("@odata.deltaLink")] public string? DeltaLink { get; set; }
}

internal class GraphEventDto
{
    [JsonPropertyName("id")]              public string? Id { get; set; }
    [JsonPropertyName("subject")]         public string? Subject { get; set; }
    [JsonPropertyName("bodyPreview")]     public string? BodyPreview { get; set; }
    [JsonPropertyName("type")]            public string? Type { get; set; }
    [JsonPropertyName("isAllDay")]        public bool? IsAllDay { get; set; }
    [JsonPropertyName("isCancelled")]     public bool? IsCancelled { get; set; }
    [JsonPropertyName("webLink")]         public string? WebLink { get; set; }
    [JsonPropertyName("start")]           public GraphDateTimeTz? Start { get; set; }
    [JsonPropertyName("end")]             public GraphDateTimeTz? End { get; set; }
    [JsonPropertyName("location")]        public GraphLocation? Location { get; set; }
    [JsonPropertyName("isReminderOn")]    public bool? IsReminderOn { get; set; }
    [JsonPropertyName("reminderMinutesBeforeStart")] public int? ReminderMinutesBeforeStart { get; set; }
    [JsonPropertyName("onlineMeetingUrl")] public string? OnlineMeetingUrl { get; set; }
    [JsonPropertyName("onlineMeeting")]   public GraphOnlineMeeting? OnlineMeeting { get; set; }
    [JsonPropertyName("organizer")]       public GraphRecipient? Organizer { get; set; }
    [JsonPropertyName("attendees")]       public List<GraphAttendee>? Attendees { get; set; }
    [JsonPropertyName("seriesMasterId")]  public string? SeriesMasterId { get; set; }
    // Present on delta tombstones: {"id": "...", "@removed": {"reason": "deleted"}}
    [JsonPropertyName("@removed")]        public GraphRemoved? Removed { get; set; }
}

internal class GraphRemoved
{
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

internal class GraphDateTimeTz
{
    [JsonPropertyName("dateTime")] public string? DateTime { get; set; }
    [JsonPropertyName("timeZone")] public string? TimeZone { get; set; }
}

internal class GraphLocation
{
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
}

internal class GraphOnlineMeeting
{
    [JsonPropertyName("joinUrl")] public string? JoinUrl { get; set; }
}

internal class GraphRecipient
{
    [JsonPropertyName("emailAddress")] public GraphEmailAddress? EmailAddress { get; set; }
}

internal class GraphEmailAddress
{
    [JsonPropertyName("name")]    public string? Name { get; set; }
    [JsonPropertyName("address")] public string? Address { get; set; }
}

internal class GraphAttendee
{
    [JsonPropertyName("type")]         public string? Type { get; set; }
    [JsonPropertyName("status")]       public GraphResponseStatus? Status { get; set; }
    [JsonPropertyName("emailAddress")] public GraphEmailAddress? EmailAddress { get; set; }
}

internal class GraphResponseStatus
{
    [JsonPropertyName("response")] public string? Response { get; set; }
}

internal class GraphCalendarPage
{
    [JsonPropertyName("value")]           public List<GraphCalendarDto>? Value { get; set; }
    [JsonPropertyName("@odata.nextLink")] public string? NextLink { get; set; }
}

internal class GraphCalendarDto
{
    [JsonPropertyName("id")]                public string? Id { get; set; }
    [JsonPropertyName("name")]              public string? Name { get; set; }
    [JsonPropertyName("hexColor")]          public string? HexColor { get; set; }
    [JsonPropertyName("canEdit")]           public bool? CanEdit { get; set; }
    [JsonPropertyName("isDefaultCalendar")] public bool? IsDefaultCalendar { get; set; }
}

internal class GraphTodoListPage
{
    [JsonPropertyName("value")]           public List<GraphTodoListDto>? Value { get; set; }
    [JsonPropertyName("@odata.nextLink")] public string? NextLink { get; set; }
}

internal class GraphTodoListDto
{
    [JsonPropertyName("id")]          public string? Id { get; set; }
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
}

internal class GraphTodoTaskPage
{
    [JsonPropertyName("value")]           public List<GraphTodoTaskDto>? Value { get; set; }
    [JsonPropertyName("@odata.nextLink")] public string? NextLink { get; set; }
}

internal class GraphTodoTaskDto
{
    [JsonPropertyName("id")]          public string? Id { get; set; }
    [JsonPropertyName("title")]       public string? Title { get; set; }
    [JsonPropertyName("status")]      public string? Status { get; set; }
    [JsonPropertyName("body")]        public GraphTaskBody? Body { get; set; }
    [JsonPropertyName("dueDateTime")] public GraphDateTimeTz? DueDateTime { get; set; }
}

internal class GraphTaskBody
{
    [JsonPropertyName("content")] public string? Content { get; set; }
}

internal class GraphRsvpBody
{
    [JsonPropertyName("sendResponse")] public bool SendResponse { get; set; }
}

[JsonSerializable(typeof(GraphEventPage))]
[JsonSerializable(typeof(GraphEventDto))]
[JsonSerializable(typeof(GraphCalendarPage))]
[JsonSerializable(typeof(GraphTodoListPage))]
[JsonSerializable(typeof(GraphTodoTaskPage))]
[JsonSerializable(typeof(GraphRsvpBody))]
internal partial class GraphApiJsonContext : JsonSerializerContext { }

// ── Client ─────────────────────────────────────────────────────────────────────

// Microsoft Graph twin of GoogleApiClient, feeding the same EventSyncResult /
// CalendarInfo / TaskItem shapes. The sync token is Graph's @odata.deltaLink —
// an opaque URL bound to the (calendar, window) it was issued for, so it slots
// into the string-token contract unchanged.
public sealed class GraphApiClient(AccountId id)
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0";

    private static readonly HttpClient _http = new();

    // Outlook resources allow only ~4 concurrent requests per mailbox, and the
    // cache layer fans sync out per (calendar, year) — an ungated startup burst
    // trips 429s instantly. 3 leaves headroom for user actions (RSVP). Keyed by
    // email because a new GraphApiClient is constructed per call.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _mailboxGates = new();

    private SemaphoreSlim MailboxGate => _mailboxGates.GetOrAdd(id.Email, _ => new SemaphoreSlim(3));

    // Sends a request through the per-mailbox gate, retrying 429s with the
    // server-provided Retry-After (Graph always sends one). The factory is
    // re-invoked per attempt — HttpRequestMessage is single-use.
    private async Task<HttpResponseMessage> SendThrottledAsync(
        Func<HttpRequestMessage> makeRequest, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            await MailboxGate.WaitAsync(ct);
            HttpResponseMessage resp;
            try
            {
                using var req = makeRequest();
                resp = await _http.SendAsync(req, ct);
            }
            finally { MailboxGate.Release(); }

            if ((int)resp.StatusCode != 429 || attempt >= 3)
                return resp;

            var delay = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 << attempt);
            resp.Dispose();
            await Task.Delay(delay, ct);
        }
    }

    // Full sync for a calendar window via calendarView/delta. calendarView
    // expands recurring series into instances (Google's singleEvents=true
    // equivalent), and the final page carries a deltaLink for incrementals.
    public async Task<EventSyncResult> InitialSyncEventsAsync(
        string calendarId, DateTime from, DateTime to,
        IReadOnlyList<int>? defaultPopupMinutes = null, CancellationToken ct = default)
    {
        var url = $"{GraphBase}/me/calendars/{Uri.EscapeDataString(calendarId)}/calendarView/delta" +
                  $"?startDateTime={Uri.EscapeDataString(from.ToUniversalTime().ToString("o"))}" +
                  $"&endDateTime={Uri.EscapeDataString(to.ToUniversalTime().ToString("o"))}";
        return await PageSyncAsync(url, ct);
    }

    // Incremental sync: GET the stored deltaLink. 410 Gone => token too old,
    // caller re-runs InitialSyncEventsAsync (same contract as Google).
    public async Task<EventSyncResult> IncrementalSyncEventsAsync(
        string calendarId, string syncToken,
        IReadOnlyList<int>? defaultPopupMinutes = null, CancellationToken ct = default)
    {
        return await PageSyncAsync(syncToken, ct);
    }

    // Sets the signed-in user's RSVP. Graph has dedicated actions per response,
    // and unlike Google's attendee-array PATCH they don't need the attendee
    // list sent back — guests/rooms parameters are accepted for interface
    // parity and ignored. sendResponse=true notifies the organizer, matching
    // the Google path's sendUpdates=all.
    public async Task SetMyResponseStatusAsync(
        string calendarId, string eventId,
        IReadOnlyList<EventAttendee> guests, IReadOnlyList<EventAttendee>? rooms,
        string status, CancellationToken ct = default)
    {
        var action = status switch
        {
            "accepted"  => "accept",
            "declined"  => "decline",
            "tentative" => "tentativelyAccept",
            _ => throw new ArgumentException($"Unknown response status '{status}'", nameof(status)),
        };

        var token = await MicrosoftOAuthClient.GetAccessTokenAsync(id, ct);
        var url = $"{GraphBase}/me/events/{Uri.EscapeDataString(eventId)}/{action}";

        using var resp = await SendThrottledAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new GraphRsvpBody { SendResponse = true },
                                             GraphApiJsonContext.Default.GraphRsvpBody),
            };
            req.Headers.Authorization = new("Bearer", token);
            return req;
        }, ct);
        await EnsureSuccessOrAuthExpiredAsync(resp, ct);
    }

    private async Task<EventSyncResult> PageSyncAsync(string startUrl, CancellationToken ct)
    {
        var token = await MicrosoftOAuthClient.GetAccessTokenAsync(id, ct);

        // Collect every page before mapping: delta sends occurrences of a
        // recurring series as skeletons (id, start/end, seriesMasterId — no
        // subject) with the display fields only on the seriesMaster item,
        // which may land on a different page than its occurrences.
        var items = new List<GraphEventDto>();
        string? url = startUrl;
        string? deltaLink = null;

        while (url is not null)
        {
            var pageUrl = url;
            using var resp = await SendThrottledAsync(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, pageUrl);
                req.Headers.Authorization = new("Bearer", token);
                // Events arrive already converted to the machine's wall-clock
                // zone, so the mapping below needs no TZ arithmetic. Windows TZ
                // ids are exactly what Graph accepts here.
                req.Headers.Add("Prefer", $"outlook.timezone=\"{TimeZoneInfo.Local.Id}\"");
                req.Headers.Add("Prefer", "odata.maxpagesize=200");
                return req;
            }, ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.Gone)
                return new EventSyncResult([], [], null, true, false);

            await EnsureSuccessOrAuthExpiredAsync(resp, ct);
            var page = await resp.Content.ReadFromJsonAsync(GraphApiJsonContext.Default.GraphEventPage, ct);

            items.AddRange(page?.Value ?? []);
            deltaLink = page?.DeltaLink;
            url = page?.NextLink;
        }

        var masters = new Dictionary<string, GraphEventDto>();
        foreach (var item in items)
            if (item.Type == "seriesMaster" && item.Id is not null)
                masters[item.Id] = item;

        var upserts = new List<CalendarEvent>();
        var cancelled = new List<string>();

        foreach (var item in items)
        {
            if (item.Id is null) continue;
            if (item.Removed is not null || item.IsCancelled == true)
            {
                cancelled.Add(item.Id);
                continue;
            }

            // Masters aren't renderable instances themselves — they only feed
            // the backfill above/below.
            if (item.Type == "seriesMaster") continue;

            // Skeleton occurrence: pull display fields from its master. When
            // the delta batch has no master (e.g. a single instance moved),
            // fetch it once — subsequent siblings hit the dictionary.
            if (item.Subject is null && item.SeriesMasterId is { } masterId)
            {
                if (!masters.TryGetValue(masterId, out var master))
                {
                    master = await FetchEventAsync(masterId, token, ct);
                    if (master is not null) masters[masterId] = master;
                }
                if (master is not null) BackfillFromMaster(item, master);
            }

            var isAllDay = item.IsAllDay == true;
            var start = ParseGraphTime(item.Start, isAllDay);
            var end   = ParseGraphTime(item.End, isAllDay);

            var (eventGuests, rooms) = SplitAttendees(item.Attendees, item.Organizer);
            upserts.Add(new CalendarEvent
            {
                Id          = item.Id,
                Title       = item.Subject ?? "(без названия)",
                Description = string.IsNullOrWhiteSpace(item.BodyPreview) ? null : item.BodyPreview,
                Location    = string.IsNullOrWhiteSpace(item.Location?.DisplayName) ? null : item.Location.DisplayName,
                Start       = start,
                End         = end,
                IsAllDay    = isAllDay,
                CalendarId  = null,
                Color       = null, // Graph colors events via categories; out of scope
                AccountEmail = id.Email,
                HtmlLink    = item.WebLink,
                ReminderMinutes = item.IsReminderOn == true && item.ReminderMinutesBeforeStart is { } m
                                      ? [m] : null,
                MeetJoinUrl = FirstNonEmpty(item.OnlineMeeting?.JoinUrl, item.OnlineMeetingUrl),
                Attendees   = eventGuests,
                Rooms       = rooms,
            });
        }

        return new EventSyncResult(upserts, cancelled, deltaLink, false, false);
    }

    // Copies the display fields a delta skeleton omits. Occurrence-own values
    // win: an "exception" instance carries exactly the fields that were
    // overridden for that one occurrence.
    private static void BackfillFromMaster(GraphEventDto o, GraphEventDto m)
    {
        o.Subject                    ??= m.Subject;
        o.BodyPreview                ??= m.BodyPreview;
        o.Location                   ??= m.Location;
        o.WebLink                    ??= m.WebLink;
        o.IsAllDay                   ??= m.IsAllDay;
        o.IsReminderOn               ??= m.IsReminderOn;
        o.ReminderMinutesBeforeStart ??= m.ReminderMinutesBeforeStart;
        o.OnlineMeetingUrl           ??= m.OnlineMeetingUrl;
        o.OnlineMeeting              ??= m.OnlineMeeting;
        o.Organizer                  ??= m.Organizer;
        o.Attendees                  ??= m.Attendees;
    }

    // Fetches a single event (used for series masters missing from a delta
    // batch). 404 => the series is gone; caller just leaves the skeleton bare.
    private async Task<GraphEventDto?> FetchEventAsync(string eventId, string token, CancellationToken ct)
    {
        using var resp = await SendThrottledAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{GraphBase}/me/events/{Uri.EscapeDataString(eventId)}");
            req.Headers.Authorization = new("Bearer", token);
            req.Headers.Add("Prefer", $"outlook.timezone=\"{TimeZoneInfo.Local.Id}\"");
            return req;
        }, ct);

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrAuthExpiredAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync(GraphApiJsonContext.Default.GraphEventDto, ct);
    }

    // Fetches all calendars visible to this account (own + shared).
    public async Task<List<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default)
    {
        var token = await MicrosoftOAuthClient.GetAccessTokenAsync(id, ct);
        var result = new List<CalendarInfo>();
        string? url = $"{GraphBase}/me/calendars";

        while (url is not null)
        {
            var page = await SendGetAsync(url, token, GraphApiJsonContext.Default.GraphCalendarPage, ct);

            foreach (var item in page?.Value ?? [])
            {
                if (item.Id is null) continue;
                result.Add(new CalendarInfo
                {
                    Id = item.Id,
                    Title = item.Name ?? item.Id,
                    // hexColor is "" for the default auto color — fall back to
                    // Outlook blue so the UI never paints with a null brush.
                    BackgroundColor = string.IsNullOrWhiteSpace(item.HexColor) ? "#0078d4" : item.HexColor,
                    ForegroundColor = null,
                    Selected = true,
                    AccessRole = item.CanEdit == true ? "owner" : "reader",
                    DefaultPopupReminderMinutes = null, // Graph events always carry concrete reminders
                });
            }

            url = page?.NextLink;
        }

        return result;
    }

    // Microsoft To Do: all lists, then open tasks per list. Completed tasks are
    // filtered client-side to sidestep $filter support differences per account
    // type — lists are small enough that the extra payload doesn't matter.
    public async Task<List<TaskItem>> GetTasksAsync(CancellationToken ct = default)
    {
        var token = await MicrosoftOAuthClient.GetAccessTokenAsync(id, ct);
        var allTasks = new List<TaskItem>();

        var lists = new List<GraphTodoListDto>();
        string? url = $"{GraphBase}/me/todo/lists";
        while (url is not null)
        {
            var page = await SendGetAsync(url, token, GraphApiJsonContext.Default.GraphTodoListPage, ct);
            lists.AddRange(page?.Value ?? []);
            url = page?.NextLink;
        }

        foreach (var list in lists)
        {
            if (list.Id is null) continue;
            string? taskUrl = $"{GraphBase}/me/todo/lists/{Uri.EscapeDataString(list.Id)}/tasks";
            while (taskUrl is not null)
            {
                var page = await SendGetAsync(taskUrl, token, GraphApiJsonContext.Default.GraphTodoTaskPage, ct);

                foreach (var t in page?.Value ?? [])
                {
                    if (t.Id is null) continue;
                    if (t.Status == "completed") continue;

                    DateOnly? due = null;
                    if (t.DueDateTime?.DateTime is { Length: >= 10 } d && DateOnly.TryParse(d[..10], out var parsed))
                        due = parsed;

                    allTasks.Add(new TaskItem
                    {
                        Id            = t.Id,
                        Title         = t.Title ?? "(без названия)",
                        Notes         = string.IsNullOrWhiteSpace(t.Body?.Content) ? null : t.Body.Content.Trim(),
                        Due           = due,
                        Completed     = false,
                        TaskListId    = list.Id,
                        TaskListTitle = list.DisplayName,
                        AccountEmail  = id.Email,
                    });
                }

                taskUrl = page?.NextLink;
            }
        }

        return allTasks;
    }

    // start/end arrive in the machine's local zone (Prefer header above) as
    // bare "2026-07-24T10:00:00.0000000" strings. All-day events are
    // midnight-to-midnight; keep just the date to avoid TZ-conversion drift.
    private static DateTime ParseGraphTime(GraphDateTimeTz? t, bool isAllDay)
    {
        if (t?.DateTime is not { } s || !DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return DateTime.Today;
        return isAllDay ? dt.Date : dt;
    }

    private static string? FirstNonEmpty(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) ? a : (!string.IsNullOrWhiteSpace(b) ? b : null);

    // Graph statuses → the Google-shaped strings the rest of the app speaks
    // (EventActions / RSVP chips compare against these).
    private static string? MapResponse(string? graphResponse) => graphResponse switch
    {
        "accepted"            => "accepted",
        "declined"            => "declined",
        "tentativelyAccepted" => "tentative",
        "organizer"           => "accepted",
        "none" or "notResponded" or null => "needsAction",
        _ => "needsAction",
    };

    // Splits Graph attendees[] into people vs resources (rooms/equipment).
    // Graph has no self/organizer flags on attendee entries, so both are
    // derived by address comparison.
    private (List<EventAttendee>? Guests, List<EventAttendee>? Rooms) SplitAttendees(
        List<GraphAttendee>? src, GraphRecipient? organizer)
    {
        if (src is null || src.Count == 0) return (null, null);
        var organizerEmail = organizer?.EmailAddress?.Address;
        List<EventAttendee>? guests = null;
        List<EventAttendee>? rooms = null;
        foreach (var a in src)
        {
            if (a.EmailAddress?.Address is not { Length: > 0 } email) continue;
            var entry = new EventAttendee
            {
                Email = email,
                DisplayName = a.EmailAddress.Name,
                ResponseStatus = MapResponse(a.Status?.Response),
                IsOrganizer = string.Equals(email, organizerEmail, StringComparison.OrdinalIgnoreCase),
                IsSelf = string.Equals(email, id.Email, StringComparison.OrdinalIgnoreCase),
                IsOptional = a.Type == "optional",
            };
            if (a.Type == "resource") (rooms ??= []).Add(entry);
            else (guests ??= []).Add(entry);
        }
        return (guests, rooms);
    }

    private async Task<T?> SendGetAsync<T>(
        string url, string accessToken,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken ct)
    {
        using var resp = await SendThrottledAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new("Bearer", accessToken);
            return req;
        }, ct);
        await EnsureSuccessOrAuthExpiredAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync(typeInfo, ct);
    }

    // 401 = token rejected; 403 = a consented scope is missing (e.g. after we
    // widen Scopes in a future version) — both resolve through the same
    // re-auth path, so both map to AccountAuthExpiredException. Graph signals
    // throttling as 429, which falls through as a transient HttpRequestException
    // (unlike Google, where rate limits hide inside 403 and need a carve-out).
    private async Task EnsureSuccessOrAuthExpiredAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;

        var code = (int)resp.StatusCode;
        if (code == 401 || code == 403)
        {
            string body = "";
            try { body = await resp.Content.ReadAsStringAsync(ct); } catch { }
            throw new AccountAuthExpiredException(id, $"api: HTTP {code} ({Trim(body)})");
        }

        resp.EnsureSuccessStatusCode();

        static string Trim(string s) => s.Length <= 200 ? s : s[..200];
    }
}
