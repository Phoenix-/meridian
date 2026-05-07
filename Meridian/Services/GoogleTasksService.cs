using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Tasks.v1;
using Meridian.Models;

namespace Meridian.Services;

public class GoogleTasksService(UserCredential credential)
{
    private readonly TasksService _service = new(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "Meridian",
    });

    public async Task<List<TaskItem>> GetTasksAsync(string accountEmail)
    {
        var listsResult = await _service.Tasklists.List().ExecuteAsync();
        var allTasks = new List<TaskItem>();

        foreach (var list in listsResult.Items ?? [])
        {
            var req = _service.Tasks.List(list.Id);
            req.ShowCompleted = false;
            req.ShowHidden = false;

            var result = await req.ExecuteAsync();
            foreach (var task in result.Items ?? [])
            {
                // Google Tasks returns due as "2026-05-07T00:00:00.000Z" — always midnight UTC,
                // representing a plain date with no time component. Parse as DateOnly to avoid timezone shift.
                DateOnly? due = null;
                if (task.Due != null && DateOnly.TryParse(task.Due[..10], out var d))
                    due = d;

                allTasks.Add(new TaskItem
                {
                    Id = task.Id,
                    Title = task.Title ?? "(без названия)",
                    Notes = task.Notes,
                    Due = due,
                    Completed = task.Status == "completed",
                    TaskListId = list.Id,
                    TaskListTitle = list.Title,
                    AccountEmail = accountEmail,
                });
            }
        }

        return allTasks;
    }
}
