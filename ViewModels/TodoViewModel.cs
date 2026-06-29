using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReportManagement.Models;
using ReportManagement.Services;

namespace ReportManagement.ViewModels;

/// <summary>
/// ToDoリスト（残タスク）を管理するViewModel。
/// 日付ごとのToDoを管理し、完了/未完了の切り替えとリアルタイム保存を行う。
/// </summary>
public partial class TodoViewModel : ObservableObject
{
    private readonly DatabaseService _db = DatabaseService.Instance;

    // ─── プロパティ ─────────────────────────────────────────────

    /// <summary>現在表示中の日付（YYYY-MM-DD）</summary>
    [ObservableProperty]
    private string _currentDate = string.Empty;

    /// <summary>ToDoタスクのコレクション（UIへバインド）</summary>
    public ObservableCollection<TodoItemViewModel> Items { get; } = new();

    /// <summary>新規タスク入力用テキスト</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTodoCommand))]
    private string _newTaskText = string.Empty;

    /// <summary>新規タスクの開始時刻</summary>
    [ObservableProperty]
    private string _newStartTime = string.Empty;

    /// <summary>新規タスクの終了時刻</summary>
    [ObservableProperty]
    private string _newEndTime = string.Empty;

    /// <summary>新規タスクの本文</summary>
    [ObservableProperty]
    private string _newDescription = string.Empty;

    /// <summary>未完了タスクが1件以上あるか（UIのバッジ表示用）</summary>
    public bool HasIncompleteTasks => Items.Count > 0 && System.Linq.Enumerable.Any(Items, i => !i.IsCompleted);

    // ─── コンストラクタ ──────────────────────────────────────────

    public TodoViewModel()
    {
        CurrentDate = DateTime.Today.ToString("yyyy-MM-dd");
    }

    // ─── 日付変更時のデータ再読み込み ────────────────────────────

    /// <summary>指定日付のToDoリストをDBから読み込む（表示用）。</summary>
    public void LoadForDate(string date)
    {
        CurrentDate = date;
        Items.Clear();
        _allItems.Clear();
        var tasks = _db.GetTodosForDisplay(date);
        foreach (var task in tasks)
        {
            var vm = new TodoItemViewModel(task);
            vm.DeleteRequested    += OnDeleteRequested;
            vm.CompletedChanged   += OnCompletedChanged;
            _allItems.Add(vm);
            Items.Add(vm);
        }
        OnPropertyChanged(nameof(HasIncompleteTasks));
    }

    /// <summary>カレンダー全体からToDoを検索する（検索モード）。</summary>
    public void SearchAll(string text)
    {
        _filterText = text ?? string.Empty;
        Items.Clear();

        if (string.IsNullOrWhiteSpace(_filterText))
        {
            // 検索テキストが空なら現在日付の通常表示に戻す
            LoadForDate(CurrentDate);
            return;
        }

        var results = _db.SearchTodos(_filterText);
        foreach (var task in results)
        {
            var vm = new TodoItemViewModel(task);
            vm.DeleteRequested    += OnDeleteRequested;
            vm.CompletedChanged   += OnCompletedChanged;
            Items.Add(vm);
        }
        OnPropertyChanged(nameof(HasIncompleteTasks));
    }

    /// <summary>ToDoの追加・完了・削除時に発火（カレンダー更新用）</summary>
    public event Action? TodosChanged;

    private string _filterText = string.Empty;
    private readonly List<TodoItemViewModel> _allItems = new();

    // ─── コマンド ────────────────────────────────────────────────

    /// <summary>新規ToDoタスクを追加する。</summary>
    [RelayCommand(CanExecute = nameof(CanAddTodo))]
    private void AddTodo()
    {
        if (string.IsNullOrWhiteSpace(NewTaskText)) return;

        int sortOrder = Items.Count * 10;
        var task = new TodoTask
        {
            TaskDate    = CurrentDate,
            TaskName    = NewTaskText.Trim(),
            SortOrder   = sortOrder,
            StartTime   = NewStartTime.Trim(),
            EndTime     = NewEndTime.Trim(),
            Description = NewDescription.Trim(),
        };
        task.Id = _db.InsertTodo(task);
        AddItemViewModel(task);
        NewTaskText = string.Empty;
        NewStartTime = string.Empty;
        NewEndTime = string.Empty;
        NewDescription = string.Empty;
        OnPropertyChanged(nameof(HasIncompleteTasks));
        TodosChanged?.Invoke();
    }

    private bool CanAddTodo() => !string.IsNullOrWhiteSpace(NewTaskText);

    // ─── 内部ヘルパー ────────────────────────────────────────────

    private void AddItemViewModel(TodoTask task)
    {
        var vm = new TodoItemViewModel(task);
        vm.DeleteRequested    += OnDeleteRequested;
        vm.CompletedChanged   += OnCompletedChanged;
        _allItems.Add(vm);
        Items.Add(vm);
    }

    private void OnDeleteRequested(TodoItemViewModel item)
    {
        _db.DeleteTodo(item.Id);
        item.DeleteRequested  -= OnDeleteRequested;
        item.CompletedChanged -= OnCompletedChanged;
        _allItems.Remove(item);
        Items.Remove(item);
        OnPropertyChanged(nameof(HasIncompleteTasks));
        TodosChanged?.Invoke();
    }

    private void OnCompletedChanged(TodoItemViewModel item)
    {
        _db.UpdateTodo(item.ToModel());
        OnPropertyChanged(nameof(HasIncompleteTasks));
        TodosChanged?.Invoke();
    }
}

// ─── ToDoアイテムViewModel ────────────────────────────────────────

/// <summary>
/// ToDoリストの1行を表すViewModel。
/// 完了チェック変更時に自動でDBを更新する。
/// </summary>
public partial class TodoItemViewModel : ObservableObject
{
    public int    Id       { get; }
    public string TaskDate { get; }

    [ObservableProperty]
    private string _taskName;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private string _startTime = string.Empty;

    [ObservableProperty]
    private string _endTime = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isExpanded;

    public int SortOrder { get; set; }

    public string TimeLabel => !string.IsNullOrEmpty(StartTime) || !string.IsNullOrEmpty(EndTime)
        ? $"{(string.IsNullOrEmpty(StartTime) ? "???" : StartTime)}～{(string.IsNullOrEmpty(EndTime) ? "???" : EndTime)}"
        : string.Empty;

    public event Action<TodoItemViewModel>? DeleteRequested;
    public event Action<TodoItemViewModel>? CompletedChanged;

    public TodoItemViewModel(TodoTask task)
    {
        Id           = task.Id;
        TaskDate     = task.TaskDate;
        _taskName    = task.TaskName;
        _isCompleted = task.IsCompleted;
        SortOrder    = task.SortOrder;
        _startTime   = task.StartTime ?? string.Empty;
        _endTime     = task.EndTime ?? string.Empty;
        _description = task.Description ?? string.Empty;
    }

    /// <summary>このタスクが当日のものかどうか</summary>
    public bool IsToday => TaskDate == DateTime.Today.ToString("yyyy-MM-dd");

    /// <summary>過去のタスクは履歴として読み取り専用（当日のみ編集可）</summary>
    public bool IsReadOnly => !IsToday;

    partial void OnIsCompletedChanged(bool value) => CompletedChanged?.Invoke(this);
    partial void OnTaskNameChanged(string value)  => CompletedChanged?.Invoke(this);
    partial void OnStartTimeChanged(string value) => CompletedChanged?.Invoke(this);
    partial void OnEndTimeChanged(string value)   => CompletedChanged?.Invoke(this);
    partial void OnDescriptionChanged(string value) => CompletedChanged?.Invoke(this);

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void RequestDelete() => DeleteRequested?.Invoke(this);

    public TodoTask ToModel() => new()
    {
        Id          = Id,
        TaskDate    = TaskDate,
        TaskName    = TaskName,
        IsCompleted = IsCompleted,
        SortOrder   = SortOrder,
        StartTime   = StartTime,
        EndTime     = EndTime,
        Description = Description,
    };
}
