using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReportManagement.Models;
using ReportManagement.Services;

namespace ReportManagement.ViewModels;

/// <summary>
/// メイン画面全体を制御するViewModel。
/// 日報エントリー一覧・検索・日付ナビゲーション・テンプレート操作を統括する。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly DatabaseService _db = DatabaseService.Instance;

    // ─── サブViewModel ─────────────────────────────────────────
    public CalendarViewModel Calendar { get; } = new();
    public TodoViewModel     Todo     { get; } = new();

    // ─── プロパティ ─────────────────────────────────────────────

    /// <summary>現在表示・編集対象の日付（YYYY-MM-DD）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DateLabel))]
    private string _selectedDate = DateTime.Today.ToString("yyyy-MM-dd");

    /// <summary>画面上部に表示する日付ラベル（例：「2026年5月22日（木）」）</summary>
    public string DateLabel
    {
        get
        {
            if (DateTime.TryParse(SelectedDate, out var dt))
                return dt.ToString("yyyy年M月d日（ddd）", new System.Globalization.CultureInfo("ja-JP"));
            return SelectedDate;
        }
    }

    /// <summary>日報エントリーのコレクション（現在選択日分）</summary>
    public ObservableCollection<DailyEntryViewModel> Entries { get; } = new();

    /// <summary>検索テキスト</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>検索モード中かどうか（trueのとき左ペインに検索結果を表示）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNormalMode))]
    private bool _isSearchMode = false;

    /// <summary>ToDo検索テキスト（カレンダー側）</summary>
    [ObservableProperty]
    private string _todoSearchText = string.Empty;

    public bool IsNormalMode => !IsSearchMode;

    /// <summary>検索結果リスト（項目名・日付・件数の文字列リスト）</summary>
    public ObservableCollection<SearchResultItem> SearchResults { get; } = new();

    /// <summary>テンプレートグループ一覧（「テンプレから追加」ドロップダウン用）</summary>
    public ObservableCollection<TemplateGroup> TemplateGroups { get; } = new();

    /// <summary>テンプレートから追加する際に選択されたグループ</summary>
    [ObservableProperty]
    private TemplateGroup? _selectedTemplateGroup;

    /// <summary>ステータスバーメッセージ</summary>
    [ObservableProperty]
    private string _statusMessage = "準備完了";

    /// <summary>起動時に表示するスケジュール通知メッセージ</summary>
    [ObservableProperty]
    private string _startupMessage = string.Empty;

    /// <summary>スケジュール通知の表示/非表示フラグ</summary>
    [ObservableProperty]
    private bool _isStartupMessageVisible = false;

    // ─── コンストラクタ ──────────────────────────────────────────

    public MainViewModel()
    {
        // カレンダーの日付選択イベントを購読
        Calendar.DateSelected += OnCalendarDateSelected;

        // ToDoの変更をカレンダーに反映
        Todo.TodosChanged += () => Calendar.RefreshCells();

        // 初期化：当日の日報を読み込み（なければテンプレートから自動生成）
        InitializeForToday();
        LoadTemplateGroups();
    }

    // ─── 初期化 ──────────────────────────────────────────────────

    private void InitializeForToday()
    {
        string today = DateTime.Today.ToString("yyyy-MM-dd");

        // 過去の未完了タスクを今日へ引き継ぐ（元の日付を保持）
        _db.CarryOverIncompleteTodos(today);

        // 不足しているテンプレート項目があれば先頭に自動追加（重複スキップ）
        bool generated = _db.AutoGenerateFromTemplate(today);
        if (generated)
            SetStatus("テンプレートの不足分を先頭に追加しました。");

        LoadEntriesForDate(today);
        Todo.LoadForDate(today);
        Calendar.SelectDate(today);

        // 起動時のスケジュール通知ダイアログを構築
        BuildStartupMessage();
    }

    private void BuildStartupMessage()
    {
        int incompleteTodos = Todo.Items.Count(t => !t.IsCompleted);
        int entryCount = Entries.Count;

        if (incompleteTodos > 0 || entryCount > 0)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("📢 本日のスケジュール");
            sb.AppendLine();
            sb.AppendLine($"・未完了のToDoタスク: {incompleteTodos}件");
            sb.AppendLine($"・本日の予定/日報項目: {entryCount}件");
            sb.AppendLine();
            sb.AppendLine("今日も一日頑張りましょう！");

            StartupMessage = sb.ToString();
            IsStartupMessageVisible = true;
        }
    }

    [RelayCommand]
    private void CloseStartupMessage()
    {
        IsStartupMessageVisible = false;
    }

    /// <summary>テンプレートグループ一覧をDBから読み込む。</summary>
    public void LoadTemplateGroups()
    {
        TemplateGroups.Clear();
        foreach (var grp in _db.GetAllTemplateGroups())
            TemplateGroups.Add(grp);
        if (TemplateGroups.Count > 0)
            SelectedTemplateGroup = TemplateGroups[0];
    }

    // ─── 日付変更 ────────────────────────────────────────────────

    /// <summary>カレンダーで日付が選択されたときに呼ばれる。</summary>
    private void OnCalendarDateSelected(string dateStr)
    {
        if (dateStr == SelectedDate) return;
        SelectedDate = dateStr;
        LoadEntriesForDate(dateStr);
        Todo.LoadForDate(dateStr);
    }

    /// <summary>指定日付のエントリーをDBから読み込みEntries を更新する。</summary>
    public void LoadEntriesForDate(string date)
    {
        // 既存エントリーのイベント購読を解除
        foreach (var e in Entries)
        {
            e.DeleteRequested -= OnEntryDeleteRequested;
            e.MoveUpRequested -= OnEntryMoveUpRequested;
            e.MoveDownRequested -= OnEntryMoveDownRequested;
        }
        Entries.Clear();

        var entries = _db.GetEntriesByDate(date)
            .OrderBy(e => e.IsCountable ? 0 : 1)  // 件数あり(テンプレ・通常)を上、メモを下
            .ThenBy(e => e.SortOrder)
            .ThenBy(e => e.Id);
        foreach (var entry in entries)
            AddEntryViewModel(new DailyEntryViewModel(entry));

        SetStatus($"{DateLabel} の日報を表示中（{Entries.Count}件）");
    }

    private void AddEntryViewModel(DailyEntryViewModel vm)
    {
        vm.DeleteRequested += OnEntryDeleteRequested;
        vm.MoveUpRequested += OnEntryMoveUpRequested;
        vm.MoveDownRequested += OnEntryMoveDownRequested;
        Entries.Add(vm);
    }

    private void OnEntryDeleteRequested(DailyEntryViewModel vm)
    {
        _db.DeleteEntry(vm.Id);
        vm.DeleteRequested -= OnEntryDeleteRequested;
        vm.MoveUpRequested -= OnEntryMoveUpRequested;
        vm.MoveDownRequested -= OnEntryMoveDownRequested;
        Entries.Remove(vm);
        SetStatus($"「{vm.ItemName}」を削除しました。");
    }

    private void OnEntryMoveUpRequested(DailyEntryViewModel vm)
    {
        MoveEntry(vm, -1);
    }

    private void OnEntryMoveDownRequested(DailyEntryViewModel vm)
    {
        MoveEntry(vm, +1);
    }

    private void MoveEntry(DailyEntryViewModel vm, int direction)
    {
        var sorted = Entries.OrderBy(e => e.SortOrder).ThenBy(e => e.Id).ToList();
        int idx = sorted.IndexOf(vm);
        int target = idx + direction;
        if (target < 0 || target >= sorted.Count) return;

        var other = sorted[target];
        (vm.SortOrder, other.SortOrder) = (other.SortOrder, vm.SortOrder);
        _db.UpdateEntry(vm.ToModel());
        _db.UpdateEntry(other.ToModel());

        // 並び替え後、コレクションを再構築して順序を反映
        var date = vm.EntryDate;
        LoadEntriesForDate(date);
        SetStatus("項目の順序を変更しました。");
    }

    // ─── 日付ナビゲーションコマンド ─────────────────────────────

    /// <summary>前日へ移動する。</summary>
    [RelayCommand]
    private void PrevDay()
    {
        if (DateTime.TryParse(SelectedDate, out var dt))
        {
            var prev = dt.AddDays(-1);
            Calendar.NavigateTo(prev);
            // NavigateTo が DateSelected イベントを発火するので OnCalendarDateSelected が呼ばれる
        }
    }

    /// <summary>翌日へ移動する。</summary>
    [RelayCommand]
    private void NextDay()
    {
        if (DateTime.TryParse(SelectedDate, out var dt))
        {
            var next = dt.AddDays(1);
            Calendar.NavigateTo(next);
        }
    }

    /// <summary>今日へ戻る。</summary>
    [RelayCommand]
    private void GoToday()
    {
        Calendar.GoToTodayCommand.Execute(null);
    }

    // ─── エントリー追加コマンド ──────────────────────────────────

    /// <summary>空の自由入力エントリーを追加する（一番下に追加）。</summary>
    [RelayCommand]
    private void AddFreeEntry()
    {
        int sortOrder = _db.GetMaxSortOrderForDate(SelectedDate, isCountable: true) + 10;
        var vm = new DailyEntryViewModel(SelectedDate, "新規項目", 1, true, sortOrder);
        AddEntryViewModel(vm);
        SetStatus("新規エントリーを追加しました。");
    }

    /// <summary>突発メモ（件数なし）エントリーを追加する（一番下に追加）。</summary>
    [RelayCommand]
    private void AddMemoEntry()
    {
        int sortOrder = _db.GetMaxSortOrderForDate(SelectedDate, isCountable: false) + 10;
        var vm = new DailyEntryViewModel(SelectedDate, "メモ", 0, false, sortOrder);
        AddEntryViewModel(vm);
        SetStatus("突発メモを追加しました。");
    }

    /// <summary>選択中のテンプレートグループの未登録項目を当日の先頭に追加する（重複スキップ）。</summary>
    [RelayCommand(CanExecute = nameof(CanAddFromTemplate))]
    private void AddFromTemplate()
    {
        if (SelectedTemplateGroup == null) return;
        int added = _db.AddTemplateGroupToDate(SelectedTemplateGroup.Id, SelectedDate);
        // 再読み込みして反映
        LoadEntriesForDate(SelectedDate);
        // カレンダーのマーカーを更新
        Calendar.RefreshCells();
        SetStatus(added > 0
            ? $"「{SelectedTemplateGroup.GroupName}」の不足分 {added} 件を先頭に追加しました。"
            : $"「{SelectedTemplateGroup.GroupName}」は既に全て登録済みです。");
    }

    private bool CanAddFromTemplate() => SelectedTemplateGroup != null;

    partial void OnSelectedTemplateGroupChanged(TemplateGroup? value)
        => AddFromTemplateCommand.NotifyCanExecuteChanged();

    partial void OnSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ClearSearch();
        }
        else
        {
            Search();
        }
    }

    partial void OnTodoSearchTextChanged(string value)
    {
        Todo.SearchAll(value);
    }

    // ─── 検索コマンド ────────────────────────────────────────────

    /// <summary>検索を実行する（フリーワード＋項目名の両方を横断）。</summary>
    [RelayCommand]
    private void Search()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            ClearSearch();
            return;
        }

        SearchResults.Clear();
        IsSearchMode = true;

        // 項目名での集計検索
        var byName = _db.SearchByItemName(SearchText);
        foreach (var (date, name, total) in byName)
        {
            SearchResults.Add(new SearchResultItem
            {
                DateStr  = date,
                ItemName = name,
                Count    = total,
                Source   = "項目",
            });
        }

        // フリーワード検索（項目名検索と重複しない日付+項目名のもの）
        var freeResults = _db.SearchFreeWord(SearchText);
        var existingKeys = new System.Collections.Generic.HashSet<string>(
            byName.Select(r => $"{r.Date}_{r.ItemName}"));

        foreach (var entry in freeResults)
        {
            string key = $"{entry.EntryDate}_{entry.ItemName}";
            if (existingKeys.Contains(key)) continue;
            SearchResults.Add(new SearchResultItem
            {
                DateStr  = entry.EntryDate,
                ItemName = entry.ItemName,
                Count    = entry.Count,
                Note     = entry.Note,
                Source   = "メモ",
            });
        }

        SetStatus($"「{SearchText}」の検索結果：{SearchResults.Count}件");
    }

    /// <summary>検索モードを終了して通常ビューに戻る。</summary>
    [RelayCommand]
    private void ClearSearch()
    {
        SearchText   = string.Empty;
        IsSearchMode = false;
        SearchResults.Clear();
        SetStatus("検索を終了しました。");
    }

    /// <summary>検索結果の日付をクリックしてその日の日報に遷移する。</summary>
    [RelayCommand]
    private void NavigateToResult(SearchResultItem item)
    {
        if (DateTime.TryParse(item.DateStr, out var dt))
        {
            ClearSearch();
            Calendar.NavigateTo(dt);
        }
    }

    /// <summary>選択された検索結果を一括削除する。</summary>
    [RelayCommand]
    private void DeleteSearchResults()
    {
        var selected = SearchResults.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("削除する項目を選択してください。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"選択された {selected.Count} 件のエントリーを削除しますか？",
            "削除の確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        int deleted = 0;
        foreach (var item in selected)
        {
            _db.DeleteEntryByKey(item.DateStr, item.ItemName);
            SearchResults.Remove(item);
            deleted++;
        }

        // カレンダーのマーカーを再描画
        if (DateTime.TryParse(selected[0].DateStr, out var dt))
            Calendar.RefreshCells();

        SetStatus($"{deleted} 件のエントリーを削除しました。");
    }

    // ─── ユーティリティ ──────────────────────────────────────────

    private void SetStatus(string message)
    {
        StatusMessage = message;
    }
}

// ─── 検索結果アイテム ────────────────────────────────────────────

/// <summary>検索結果の1件を表すデータクラス。</summary>
public class SearchResultItem : ObservableObject
{
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (SetProperty(ref _isSelected, value)) OnSelectionChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>選択状態変化を外部に通知（ViewModelで全選択/解除の制御用）。</summary>
    public event EventHandler? OnSelectionChanged;

    public string DateStr  { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int    Count    { get; set; }
    public string Note     { get; set; } = string.Empty;
    public string Source   { get; set; } = string.Empty;

    /// <summary>日付を日本語フォーマットで表示する。</summary>
    public string DateLabel
    {
        get
        {
            if (DateTime.TryParse(DateStr, out var dt))
                return dt.ToString("yyyy年M月d日（ddd）", new System.Globalization.CultureInfo("ja-JP"));
            return DateStr;
        }
    }

    /// <summary>検索結果行の表示テキスト。</summary>
    public string DisplayText => Count > 0
        ? $"{ItemName}  {Count}件"
        : string.IsNullOrEmpty(Note) ? ItemName : $"{ItemName}  {Note}";
}
