using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Meridian.Models;

namespace Meridian.Services;

public class GoogleCalendarService(UserCredential credential)
{
    private readonly CalendarService _service = new(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "Meridian",
    });

    public async Task<List<CalendarEvent>> GetEventsAsync(DateTime from, DateTime to, string accountEmail)
    {
        var request = _service.Events.List("primary");
        request.TimeMinDateTimeOffset = from;
        request.TimeMaxDateTimeOffset = to;
        request.SingleEvents = true;
        request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

        var result = await request.ExecuteAsync();
        var events = new List<CalendarEvent>();

        foreach (var item in result.Items ?? [])
        {
            var start = item.Start?.DateTimeDateTimeOffset?.LocalDateTime
                        ?? (item.Start?.Date != null ? DateTime.Parse(item.Start.Date) : DateTime.Today);
            var end = item.End?.DateTimeDateTimeOffset?.LocalDateTime
                      ?? (item.End?.Date != null ? DateTime.Parse(item.End.Date) : DateTime.Today);

            events.Add(new CalendarEvent
            {
                Id = item.Id,
                Title = item.Summary ?? "(без названия)",
                Description = item.Description,
                Start = start,
                End = end,
                IsAllDay = item.Start?.DateTimeDateTimeOffset == null,
                Color = item.ColorId,
                AccountEmail = accountEmail,
            });
        }

        return events;
    }

    // Returns a map of taskId -> reminder DateTime from the @tasks calendar.
    // The @tasks calendar exposes Google Tasks as events; each event's Id is the task ID,
    // and start.dateTime is the reminder/due time with the actual time component.
    public async Task<Dictionary<string, DateTime>> GetTaskReminderTimesAsync(DateTime from, DateTime to)
    {
        var result = new Dictionary<string, DateTime>();
        try
        {
            var request = _service.Events.List("@tasks");
            request.TimeMinDateTimeOffset = from;
            request.TimeMaxDateTimeOffset = to;
            request.SingleEvents = true;
            var items = await request.ExecuteAsync();
            foreach (var item in items.Items ?? [])
            {
                var startDt = item.Start?.DateTimeDateTimeOffset?.LocalDateTime;
                if (startDt.HasValue && item.Id != null)
                    result[item.Id] = startDt.Value;
            }
        }
        catch
        {
            // @tasks calendar may not be accessible — silently ignore
        }
        return result;
    }
}
