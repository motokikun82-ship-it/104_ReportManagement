using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReportManagement.Helpers;
using ReportManagement.Services;

namespace ReportManagement.ViewModels;

/// <summary>
/// カレンダー表示を管理するViewModel。
/// 月ナビゲーション・日付選択・日報ありマーカー表示を担当する。
/// </summary>
public partial class CalendarViewModel : ObservableObject
{
    private readonly DatabaseService _db = DatabaseService.Instance;

    // ─── プロパティ ─────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthLabel))]
    private int _displayYear;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthLabel))]
    private int _displayMonth;

    /// <summary>カレンダーヘッダー表示用ラベル（例：「2026年5月」）</summary>
    public string MonthLabel => $"{DisplayYear}年{DisplayMonth}月";

    /// <summary>カレンダーに表示するセル（日付または空白）のコレクション</summary>
    public ObservableCollection<CalendarCell> Cells { get; } = new();

    /// <summary>現在選択されている日付（YYYY-MM-DD）</summary>
    [ObservableProperty]
    private string _selectedDate = string.Empty;

    /// <summary>日付が選択されたときに親VMへ通知するイベント</summary>
    public event Action<string>? DateSelected;

    // ─── コンストラクタ ──────────────────────────────────────────

    public CalendarViewModel()
    {
        var today = DateTime.Today;
        _displayYear  = today.Year;
        _displayMonth = today.Month;
        _selectedDate = today.ToString("yyyy-MM-dd");
        RefreshCells();
    }

    // ─── コマンド ────────────────────────────────────────────────

    /// <summary>前月に移動する。</summary>
    [RelayCommand]
    private void PrevMonth()
    {
        if (DisplayMonth == 1) { DisplayYear--; DisplayMonth = 12; }
        else DisplayMonth--;
        RefreshCells();
    }

    /// <summary>翌月に移動する。</summary>
    [RelayCommand]
    private void NextMonth()
    {
        if (DisplayMonth == 12) { DisplayYear++; DisplayMonth = 1; }
        else DisplayMonth++;
        RefreshCells();
    }

    /// <summary>「今日」ボタン：今月今日に戻る。</summary>
    [RelayCommand]
    private void GoToToday()
    {
        var today = DateTime.Today;
        DisplayYear  = today.Year;
        DisplayMonth = today.Month;
        SelectDate(today.ToString("yyyy-MM-dd"));
        RefreshCells();
    }

    /// <summary>日付セルをクリックしたとき。</summary>
    [RelayCommand]
    private void SelectDay(CalendarCell cell)
    {
        if (cell.Day <= 0) return;
        SelectDate(cell.DateString);
    }

    // ─── 内部メソッド ────────────────────────────────────────────

    /// <summary>カレンダーセルを再構築する。</summary>
    public void RefreshCells()
    {
        Cells.Clear();

        // 日報・メモ・未完了ToDo がある日のセットを取得（マーカー表示用）
        HashSet<int> daysWithEntries = _db.GetDaysWithEntries(DisplayYear, DisplayMonth);
        HashSet<int> daysWithNotes = _db.GetDaysWithNotes(DisplayYear, DisplayMonth);
        HashSet<int> daysWithIncompleteTodos = _db.GetDaysWithIncompleteTodos(DisplayYear, DisplayMonth);

        // パフォーマンス改善: 月のカレンダーを描画する前に会社祝日を全件取得しておく
        var allCompanyHolidays = _db.GetAllCompanyHolidays();

        var firstDay = new DateTime(DisplayYear, DisplayMonth, 1);
        int startOffset = (int)firstDay.DayOfWeek; // 0=日, 1=月, ...
        int daysInMonth = DateTime.DaysInMonth(DisplayYear, DisplayMonth);
        var today = DateTime.Today;

        // 先頭の空白セルを追加
        for (int i = 0; i < startOffset; i++)
            Cells.Add(new CalendarCell { Day = 0 });

        // 日付セルを追加
        for (int d = 1; d <= daysInMonth; d++)
        {
            var dt = new DateTime(DisplayYear, DisplayMonth, d);
            string dateStr = dt.ToString("yyyy-MM-dd");
            bool isJp  = JapaneseHoliday.IsHoliday(dt, out var jpHolidayName);
            bool hasOverride = _db.HasHolidayOverride(dt, allCompanyHolidays);
            string? companyHolidayName = null;
            bool isCmp = !hasOverride && _db.IsCompanyHoliday(dt, allCompanyHolidays, out companyHolidayName);
            if (hasOverride) { isJp = false; jpHolidayName = null; }
            Cells.Add(new CalendarCell
            {
                Day          = d,
                DateString   = dateStr,
                IsToday      = dt == today,
                IsSelected   = dateStr == SelectedDate,
                HasEntries           = daysWithEntries.Contains(d),
                HasNotes             = daysWithNotes.Contains(d),
                HasIncompleteTodos   = daysWithIncompleteTodos.Contains(d),
                HasContent           = daysWithEntries.Contains(d) || daysWithNotes.Contains(d),
                IsHoliday    = isJp || isCmp,
                HolidayName  = string.Join(" / ", new[] { jpHolidayName, companyHolidayName }.OfType<string>()).Trim(),
                IsWeekend    = dt.DayOfWeek == DayOfWeek.Sunday || dt.DayOfWeek == DayOfWeek.Saturday,
                IsSaturday   = dt.DayOfWeek == DayOfWeek.Saturday,
            });
        }
    }

    /// <summary>指定日付を選択状態にし、親VMへ通知する。</summary>
    public void SelectDate(string dateStr)
    {
        SelectedDate = dateStr;
        // 全セルの選択状態を更新
        foreach (var cell in Cells)
            cell.IsSelected = cell.DateString == dateStr;
        DateSelected?.Invoke(dateStr);
    }

    /// <summary>
    /// 指定日付が表示月内にある場合、その月のカレンダーへ移動して選択する。
    /// </summary>
    public void NavigateTo(DateTime dt)
    {
        DisplayYear  = dt.Year;
        DisplayMonth = dt.Month;
        RefreshCells();
        SelectDate(dt.ToString("yyyy-MM-dd"));
    }
}

// ─── カレンダーセルモデル ─────────────────────────────────────────

/// <summary>
/// カレンダーの1マスを表すデータモデル。
/// ObservableObjectを継承し、IsSelected等の変更が即座にUIへ反映される。
/// </summary>
public partial class CalendarCell : ObservableObject
{
    /// <summary>日付の数値（0 = 空白セル）</summary>
    public int Day { get; set; }

    /// <summary>日付文字列（YYYY-MM-DD）</summary>
    public string DateString { get; set; } = string.Empty;

    /// <summary>今日かどうか</summary>
    public bool IsToday { get; set; }

    /// <summary>土曜日か日曜日か</summary>
    public bool IsWeekend { get; set; }

    /// <summary>土曜日か（色分け用）</summary>
    public bool IsSaturday { get; set; }

    /// <summary>日報が1件以上あるか（●マーカー表示用）</summary>
    [ObservableProperty]
    private bool _hasEntries;

    /// <summary>メモが1件以上あるか（●マーカー表示用）</summary>
    [ObservableProperty]
    private bool _hasNotes;

    /// <summary>未完了のToDoがあるか（●マーカー表示用）</summary>
    [ObservableProperty]
    private bool _hasIncompleteTodos;

    /// <summary>日報またはメモがあるか（◯で囲む表示用）</summary>
    [ObservableProperty]
    private bool _hasContent;

    /// <summary>祝日かどうか</summary>
    [ObservableProperty]
    private bool _isHoliday;

    /// <summary>祝日名</summary>
    public string HolidayName { get; set; } = string.Empty;

    /// <summary>現在選択されている日付か</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>表示するテキスト（Day=0なら空文字）</summary>
    public string DayText => Day > 0 ? Day.ToString() : string.Empty;
}
