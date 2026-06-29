namespace ReportManagement.Models;

/// <summary>
/// 会社独自の祝日・休日設定。
/// 日本の祝日（JapaneseHoliday）とは別に、会社独自の休日を管理する。
/// </summary>
public class CompanyHoliday
{
    /// <summary>SQLite自動採番ID</summary>
    public int Id { get; set; }

    /// <summary>日付（YYYY-MM-DD 形式）</summary>
    public string HolidayDate { get; set; } = string.Empty;

    /// <summary>祝日・休日名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>毎年繰り返すか（true: 毎年同じ日付、false: 特定年のみ）</summary>
    public bool IsRecurring { get; set; } = true;

    /// <summary>メモ・備考</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>作成日時</summary>
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>更新日時</summary>
    public string UpdatedAt { get; set; } = string.Empty;
}
