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
}

internal class EventDto
{
    [JsonPropertyName("id")]          public string? Id { get; set; }
    [JsonPropertyName("summary")]     public string? Summary { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("colorId")]     public string? ColorId { get; set; }
    [JsonPropertyName("start")]       public EventTime? Start { get; set; }
    [JsonPropertyName("end")]         public EventTime? End { get; set; }
}

internal class EventTime
{
    [JsonPropertyName("dateTime")] public DateTimeOffset? DateTime { get; set; }
    [JsonPropertyName("date")]     public string? Date { get; set; }
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
[JsonSerializable(typeof(TaskListList))]
[JsonSerializable(typeof(TaskList))]
internal partial class GoogleApiJsonContext : JsonSerializerContext { }

// ── Client ─────────────────────────────────────────────────────────────────────

public sealed class GoogleApiClient(AccountId id)
{
    private const string CalendarBase = "https://www.googleapis.com/calendar/v3";
    private const string TasksBase    = "https://www.googleapis.com/tasks/v1";

    private static readonly HttpClient _http = new();

    public async Task<List<CalendarEvent>> GetEventsAsync(
        string calendarId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var token = await GoogleOAuthClient.GetAccessTokenAsync(id, ct);
        var events = new List<CalendarEvent>();
        string? pageToken = null;

        do
        {
            var url = $"{CalendarBase}/calendars/{Uri.EscapeDataString(calendarId)}/events" +
                      $"?singleEvents=true" +
                      $"&orderBy=startTime" +
                      $"&timeMin={Uri.EscapeDataString(from.ToUniversalTime().ToString("o"))}" +
                      $"&timeMax={Uri.EscapeDataString(to.ToUniversalTime().ToString("o"))}" +
                      (pageToken is null ? "" : $"&pageToken={Uri.EscapeDataString(pageToken)}");

            var list = await GetEventListAsync(url, token, ct);

            foreach (var item in list?.Items ?? [])
            {
                var start = item.Start?.DateTime?.LocalDateTime
                            ?? (item.Start?.Date is { } sd ? DateTime.Parse(sd) : DateTime.Today);
                var end   = item.End?.DateTime?.LocalDateTime
                            ?? (item.End?.Date is { } ed ? DateTime.Parse(ed) : DateTime.Today);

                events.Add(new CalendarEvent
                {
                    Id          = item.Id ?? "",
                    Title       = item.Summary ?? "(без названия)",
                    Description = item.Description,
                    Start       = start,
                    End         = end,
                    IsAllDay    = item.Start?.DateTime is null,
                    Color       = item.ColorId,
                    AccountEmail = id.Email,
                });
            }

            pageToken = list?.NextPageToken;
        }
        while (pageToken is not null);

        return events;
    }

    // Returns taskId -> reminderDateTime from the special @tasks calendar.
    public async Task<Dictionary<string, DateTime>> GetTaskReminderTimesAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        var result = new Dictionary<string, DateTime>();
        try
        {
            var items = await GetEventsAsync("@tasks", from, to, ct);
            foreach (var e in items)
                if (!e.IsAllDay)
                    result[e.Id] = e.Start;
        }
        catch { }
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

    private static async Task<EventList?> GetEventListAsync(string url, string token, CancellationToken ct)
        => await SendGetAsync(url, token, GoogleApiJsonContext.Default.EventList, ct);

    private static async Task<TaskListList?> GetTaskListListAsync(string url, string token, CancellationToken ct)
        => await SendGetAsync(url, token, GoogleApiJsonContext.Default.TaskListList, ct);

    private static async Task<TaskList?> GetTaskListAsync(string url, string token, CancellationToken ct)
        => await SendGetAsync(url, token, GoogleApiJsonContext.Default.TaskList, ct);

    private static async Task<T?> SendGetAsync<T>(
        string url, string accessToken,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new("Bearer", accessToken);
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync(typeInfo, ct);
    }
}
