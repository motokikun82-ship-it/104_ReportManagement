namespace ReportManagement.Models;

public class EntryHistoryItem
{
    public string ItemName { get; set; } = string.Empty;
    public int TimesUsed { get; set; }
    public string LastUsed { get; set; } = string.Empty;

    public string LastUsedLabel
    {
        get
        {
            if (DateTime.TryParse(LastUsed, out var dt))
                return dt.ToString("yyyy/MM/dd");
            return LastUsed;
        }
    }

    public string DisplayText => $"{ItemName}（{TimesUsed}回 / {LastUsedLabel}）";
}
