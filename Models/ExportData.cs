using System.Collections.Generic;

namespace ReportManagement.Models;

public class ExportData
{
    public List<DailyEntry> DailyEntries { get; set; } = new();
    public List<Note> Notes { get; set; } = new();
    public List<TodoTask> TodoTasks { get; set; } = new();
}
