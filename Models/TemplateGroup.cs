using System.Collections.Generic;

namespace ReportManagement.Models;

/// <summary>
/// テンプレートのグループ（分類）を表すモデル。
/// 複数のテンプレート項目をまとめて管理し、
/// 朝の日報自動生成時に使用するグループを指定できる。
/// </summary>
public class TemplateGroup
{
    /// <summary>SQLite自動採番ID</summary>
    public int Id { get; set; }

    /// <summary>グループ名（例：「通常業務」「月次処理」）</summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>グループの表示順（小さい数値が上）</summary>
    public int SortOrder { get; set; } = 0;

    /// <summary>
    /// このグループをアプリ起動時の自動生成に使用するか。
    /// trueのグループのみ、当日の日報が未作成の場合に自動展開される。
    /// </summary>
    public bool IsDefault { get; set; } = false;

    /// <summary>このグループに属するテンプレート項目一覧（ナビゲーションプロパティ）</summary>
    public List<TemplateItem> Items { get; set; } = new();

    public override string ToString() => GroupName;
}
