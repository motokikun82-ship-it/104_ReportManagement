namespace ReportManagement.Models;

public class TodoTask
{
    public int Id { get; set; }

    public string TaskDate { get; set; } = string.Empty;

    public string TaskName { get; set; } = string.Empty;

    public bool IsCompleted { get; set; } = false;

    public int SortOrder { get; set; } = 0;

    public string CreatedAt { get; set; } = string.Empty;

    public string StartTime { get; set; } = string.Empty;

    public string EndTime { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
