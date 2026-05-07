namespace Meridian.Models;

public class TaskItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public DateOnly? Due { get; set; }
    public DateTime? ReminderTime { get; set; }
    public bool Completed { get; set; }
    public string? TaskListId { get; set; }
    public string? TaskListTitle { get; set; }
    public string? AccountEmail { get; set; }
}
