using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using ReportManagement.Models;
using ReportManagement.Services;

namespace ReportManagement.Views;

public partial class ScheduleTemplateWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private readonly DatabaseService _db = DatabaseService.Instance;
    private readonly ObservableCollection<TemplateGroup> _groups = new();
    private List<TemplateItem> _currentItems = new();
    private readonly List<DateTime> _selectedDates = new();
    private readonly DateTime _initialDate;  // メイン画面から渡された初期日付

    /// <summary>メイン画面で選択中の日付を初期値として受け取るコンストラクタ。</summary>
    public ScheduleTemplateWindow(string initialDate = "")
    {
        InitializeComponent();
        // 渡された日付をパースして保持（パース失敗時は今日）
        _initialDate = DateTime.TryParse(initialDate, out var dt) ? dt.Date : DateTime.Today;
        Loaded += OnLoaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int useDark = 1;
        DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        try
        {
            LoadGroups();
            // メイン画面で選択していた日付をデフォルトとして設定
            CalMain.SelectedDate = _initialDate;
            CalMain.DisplayDate = _initialDate;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"初期化エラー: {ex.Message}\n\n{ex}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void LoadGroups()
    {
        _groups.Clear();
        foreach (var g in _db.GetAllTemplateGroups())
            _groups.Add(g);
        CmbGroup.ItemsSource = _groups;
        if (_groups.Count > 0) CmbGroup.SelectedIndex = 0;
    }

    private void CmbGroup_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbGroup.SelectedItem is not TemplateGroup grp) return;
        _currentItems = _db.GetItemsByGroupId(grp.Id);
        LbItems.ItemsSource = _currentItems;
        TxtItemCount.Text = $"{_currentItems.Count} 項目";
        UpdatePreview();
    }

    private void DateMode_Changed(object sender, RoutedEventArgs e)
    {
        if (RbRange == null || RbMulti == null || SpRange == null || CalMain == null) return;
        bool isRange = RbRange.IsChecked == true;
        SpRange.Visibility = isRange ? Visibility.Visible : Visibility.Collapsed;
        CalMain.SelectionMode = RbMulti.IsChecked == true ? CalendarSelectionMode.MultipleRange : CalendarSelectionMode.SingleDate;
        UpdatePreview();
    }

    private void CalMain_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RbRange == null || RbMulti == null || CalMain == null || DpStart == null || DpEnd == null) return;
        _selectedDates.Clear();
        if (RbRange.IsChecked == true)
        {
            if (DpStart.SelectedDate.HasValue && DpEnd.SelectedDate.HasValue)
            {
                var start = DpStart.SelectedDate.Value.Date;
                var end = DpEnd.SelectedDate.Value.Date;
                if (start > end) (start, end) = (end, start);
                for (var d = start; d <= end; d = d.AddDays(1))
                    _selectedDates.Add(d);
            }
        }
        else if (RbMulti.IsChecked == true)
        {
            _selectedDates.AddRange(CalMain.SelectedDates.Cast<DateTime>());
        }
        else
        {
            if (CalMain.SelectedDate.HasValue)
                _selectedDates.Add(CalMain.SelectedDate.Value.Date);
        }
        var distinct = _selectedDates.Distinct().OrderBy(d => d).ToList();
        _selectedDates.Clear();
        _selectedDates.AddRange(distinct);
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var targetItems = GetTargetItems();
        if (targetItems.Count == 0 || _selectedDates.Count == 0)
        {
            LblSelectedDates.Text = "グループ・項目・日付を選択してください";
            return;
        }
        int total = targetItems.Count * _selectedDates.Count;
        string dates = string.Join(", ", _selectedDates.Take(5).Select(d => d.ToString("MM/dd")));
        if (_selectedDates.Count > 5) dates += $" ... 計{_selectedDates.Count}日";
        LblSelectedDates.Text = $"{targetItems.Count}項目 × {_selectedDates.Count}日 = {total}件登録予定  ({dates})";
    }

    private List<TemplateItem> GetTargetItems()
    {
        // 選択されている項目があればそれ、なければ全項目
        var selected = LbItems.SelectedItems.Cast<TemplateItem>().ToList();
        return selected.Count > 0 ? selected : _currentItems;
    }

    private void BtnPreview_Click(object sender, RoutedEventArgs e)
    {
        var targetItems = GetTargetItems();
        if (targetItems.Count == 0 || _selectedDates.Count == 0) return;

        var lines = new List<string>();
        foreach (var d in _selectedDates)
        {
            lines.Add($"=== {d:yyyy/MM/dd (ddd)} ===");
            foreach (var item in targetItems)
            {
                string cnt = item.IsCountable ? $"{item.DefaultCount}件" : "メモ";
                lines.Add($"  {item.ItemName} ({cnt})");
            }
            lines.Add("");
        }
        MessageBox.Show(string.Join("\n", lines), "登録プレビュー", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnRegister_Click(object sender, RoutedEventArgs e)
    {
        var targetItems = GetTargetItems();
        if (targetItems.Count == 0 || _selectedDates.Count == 0)
        {
            MessageBox.Show("グループ・項目・日付を選択してください。", "確認", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int inserted = 0, skipped = 0;
        foreach (var date in _selectedDates)
        {
            string dateStr = date.ToString("yyyy-MM-dd");

            // その日のIsCountableごとの最大SortOrderを取得して末尾に追加する
            int maxCountable = _db.GetMaxSortOrderForDate(dateStr, true);
            int maxMemo = _db.GetMaxSortOrderForDate(dateStr, false);
            int nextCountable = maxCountable;
            int nextMemo = maxMemo;

            foreach (var item in targetItems)
            {
                // 既に同名項目があればスキップ
                if (_db.EntryExists(dateStr, item.ItemName))
                {
                    skipped++;
                    continue;
                }

                // IsCountableごとに末尾のSortOrderを計算
                int sortOrder;
                if (item.IsCountable)
                {
                    nextCountable += 10;
                    sortOrder = nextCountable;
                }
                else
                {
                    nextMemo += 10;
                    sortOrder = nextMemo;
                }

                var entry = new DailyEntry
                {
                    EntryDate = dateStr,
                    ItemName = item.ItemName,
                    Count = item.DefaultCount,
                    IsCountable = item.IsCountable,
                    Note = string.Empty,
                    SortOrder = sortOrder   // テンプレートの固定値ではなく動的に計算した値を使用
                };
                _db.InsertEntry(entry);
                inserted++;
            }
        }

        if (inserted > 0) DialogResult = true;
    }
}