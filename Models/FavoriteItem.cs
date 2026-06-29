namespace ReportManagement.Models;

public class FavoriteItem
{
    public int Id { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int SortOrder { get; set; } = 0;
}
