namespace ReportManagement.Models;

/// <summary>
/// 日報の1エントリー（1行）を表すデータモデル。
/// 「項目名」と「件数」が独立したフィールドとして管理され、
/// 将来の集計・統計クエリにも完全対応している。
/// </summary>
public class DailyEntry
{
    /// <summary>SQLite自動採番ID</summary>
    public int Id { get; set; }

    /// <summary>対象日付（YYYY-MM-DD 形式）</summary>
    public string EntryDate { get; set; } = string.Empty;

    /// <summary>業務項目名（例：「商品登録」「受注処理」）</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// 実施件数。数値として独立管理することで集計が容易。
    /// 件数概念がないメモ行の場合は 0。
    /// </summary>
    public int Count { get; set; } = 0;

    /// <summary>
    /// 件数管理の有無。
    /// true = [項目名][件数]件 の標準形式
    /// false = 突発メモ（自由テキストのみ）
    /// </summary>
    public bool IsCountable { get; set; } = true;

    /// <summary>補足メモ（任意）。突発メモの場合はここに内容を記入。</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>表示順（小さい数値が上）</summary>
    public int SortOrder { get; set; } = 0;

    /// <summary>レコード作成日時（ISO8601形式）</summary>
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>レコード最終更新日時（ISO8601形式）</summary>
    public string UpdatedAt { get; set; } = string.Empty;
}
