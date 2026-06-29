namespace ReportManagement.Models;

public class Note
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string NoteDate { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;

    public string Preview => string.IsNullOrEmpty(Title)
        ? Content.Length > 40 ? Content[..40] + "…" : Content
        : Title;

    public string DateLabel
    {
        get
        {
            if (DateTime.TryParse(NoteDate, out var dt))
                return dt.ToString("yyyy/MM/dd");
            return NoteDate;
        }
    }
}
