namespace ReportManagement.Models;

/// <summary>
/// テンプレートグループに属する個別の業務項目を表すモデル。
/// SortOrder により画面上の表示順が固定され、
/// 毎日の日報生成時に一定の順序で項目が並ぶ。
/// </summary>
public class TemplateItem
{
    /// <summary>SQLite自動採番ID</summary>
    public int Id { get; set; }

    /// <summary>所属するグループのID（外部キー）</summary>
    public int GroupId { get; set; }

    /// <summary>業務項目名（例：「商品登録」「受注処理」）</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// 日報生成時の初期件数（通常は0）。
    /// 毎日必ず一定件数実施する業務がある場合に初期値を設定できる。
    /// </summary>
    public int DefaultCount { get; set; } = 0;

    /// <summary>
    /// 件数管理の有無。
    /// true = 件数入力欄を表示（標準形式）
    /// false = 突発メモ形式（テキストのみ）
    /// </summary>
    public bool IsCountable { get; set; } = true;

    /// <summary>グループ内での表示順（小さい数値が上）</summary>
    public int SortOrder { get; set; } = 0;
}
