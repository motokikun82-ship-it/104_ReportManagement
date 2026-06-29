using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReportManagement.Models;
using ReportManagement.Services;

namespace ReportManagement.ViewModels;

/// <summary>
/// 日報の1行エントリーを表すViewModel。
/// 件数の変更などが発生した瞬間にSQLiteへリアルタイム自動保存する。
/// </summary>
public partial class DailyEntryViewModel : ObservableObject
{
    private readonly DatabaseService _db = DatabaseService.Instance;

    // 自動保存の連続呼び出しを防ぐフラグ（ループ防止）
    private bool _isSaving = false;

    // ─── プロパティ ─────────────────────────────────────────────

    /// <summary>SQLite上のID（0 = 未保存）</summary>
    public int Id { get; private set; }

    /// <summary>対象日付（YYYY-MM-DD）</summary>
    public string EntryDate { get; private set; } = string.Empty;

    [ObservableProperty]
    private string _itemName = string.Empty;

    [ObservableProperty]
    private int _count = 0;

    [ObservableProperty]
    private bool _isCountable = true;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private bool _isExpanded;

    public int SortOrder { get; set; }

    /// <summary>この行を削除するよう親VMに通知するイベント</summary>
    public event Action<DailyEntryViewModel>? DeleteRequested;

    /// <summary>この行を上に移動するよう親VMに通知するイベント</summary>
    public event Action<DailyEntryViewModel>? MoveUpRequested;

    /// <summary>この行を下に移動するよう親VMに通知するイベント</summary>
    public event Action<DailyEntryViewModel>? MoveDownRequested;

    // ─── コンストラクタ ──────────────────────────────────────────

    /// <summary>既存DBレコードからViewModelを生成する。</summary>
    public DailyEntryViewModel(DailyEntry entry)
    {
        Id          = entry.Id;
        EntryDate   = entry.EntryDate;
        _itemName   = entry.ItemName;
        _count      = entry.Count;
        _isCountable = entry.IsCountable;
        _note       = entry.Note;
        SortOrder   = entry.SortOrder;
    }

    /// <summary>新規エントリーとしてViewModelを生成し、即座にDBに保存する。</summary>
    public DailyEntryViewModel(string date, string itemName, int count, bool isCountable, int sortOrder)
    {
        EntryDate   = date;
        _itemName   = itemName;
        _count      = count;
        _isCountable = isCountable;
        SortOrder   = sortOrder;

        // 新規なので即座にInsert
        var entry = new DailyEntry
        {
            EntryDate   = date,
            ItemName    = itemName,
            Count       = count,
            IsCountable = isCountable,
            Note        = string.Empty,
            SortOrder   = sortOrder,
        };
        Id = _db.InsertEntry(entry);
    }

    // ─── プロパティ変更 → 自動保存 ──────────────────────────────

    /// <summary>ItemName が変更されたらDBに保存する。</summary>
    partial void OnItemNameChanged(string value) => AutoSave();

    /// <summary>Count が変更されたらDBに保存する。</summary>
    partial void OnCountChanged(int value) => AutoSave();

    /// <summary>Note が変更されたらDBに保存する。</summary>
    partial void OnNoteChanged(string value) => AutoSave();

    /// <summary>変更内容をSQLiteに即座に反映する（リアルタイム自動保存）。</summary>
    private void AutoSave()
    {
        if (_isSaving || Id == 0) return;
        _isSaving = true;
        try
        {
            _db.UpdateEntry(ToModel());
        }
        finally
        {
            _isSaving = false;
        }
    }

    // ─── コマンド ────────────────────────────────────────────────

    /// <summary>件数を1増やす（リアルタイム保存付き）。</summary>
    [RelayCommand]
    private void Increment()
    {
        Count++;
    }

    /// <summary>件数を1減らす（0未満にはならない）。</summary>
    [RelayCommand]
    private void Decrement()
    {
        if (Count > 0) Count--;
    }

    /// <summary>この行を削除するよう親VMへ通知する。</summary>
    [RelayCommand]
    private void RequestDelete()
    {
        DeleteRequested?.Invoke(this);
    }

    [RelayCommand]
    private void MoveUp()
    {
        MoveUpRequested?.Invoke(this);
    }

    [RelayCommand]
    private void MoveDown()
    {
        MoveDownRequested?.Invoke(this);
    }

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    // ─── ヘルパー ────────────────────────────────────────────────

    /// <summary>現在のVMの状態をDailyEntryモデルへ変換する。</summary>
    public DailyEntry ToModel() => new()
    {
        Id          = Id,
        EntryDate   = EntryDate,
        ItemName    = ItemName,
        Count       = Count,
        IsCountable = IsCountable,
        Note        = Note,
        SortOrder   = SortOrder,
    };
}
