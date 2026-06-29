using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using ReportManagement.Helpers;
using ReportManagement.Models;
using ReportManagement.Services;

namespace ReportManagement.Views;

public partial class HolidaySettingsWindow : Window
{
    private readonly DatabaseService _db = DatabaseService.Instance;
    private int _currentYear;
    private readonly ObservableCollection<HolidayItem> _items = new();

    public HolidaySettingsWindow()
    {
        InitializeComponent();
        _currentYear = DateTime.Today.Year;
        LblYear.Text = $"{_currentYear}年";
        DgHolidays.ItemsSource = _items;
        LoadHolidays();
    }

    private void LoadHolidays()
    {
        _items.Clear();

        // 日本の祝日（読み取り専用）
        var jpMap = JapaneseHoliday.GetHolidays(_currentYear);
        foreach (var kv in jpMap.OrderBy(k => k.Key))
        {
            var dt2 = kv.Key;
            _items.Add(new HolidayItem
            {
                HolidayDate = dt2,
                OriginalDate = dt2,
                Name = kv.Value,
                Type = "日本の祝日",
                IsReadOnly = true,
            });
        }

        // 会社独自祝日（編集可能）— 空名の上書きエントリーは表示しない
        var allCompany = _db.GetAllCompanyHolidays();
        foreach (var ch in allCompany)
        {
            if (string.IsNullOrEmpty(ch.Name)) continue; // 上書きエントリーは非表示
            if (!TryMatchYear(ch, _currentYear, out var dt)) continue;
            _items.Add(new HolidayItem
            {
                Id = ch.Id,
                HolidayDate = dt,
                OriginalDate = dt,
                Name = ch.Name,
                Note = ch.Note,
                IsRecurring = ch.IsRecurring,
                Type = "会社",
                IsReadOnly = false,
            });
        }

        int companyCount = allCompany.Count(c => !string.IsNullOrEmpty(c.Name) && TryMatchYear(c, _currentYear, out _));
        LblStatus.Text = $"祝日総数: {_items.Count}件（日本の祝日: {jpMap.Count}件, 会社: {companyCount}件）";
    }

    private static bool TryMatchYear(CompanyHoliday ch, int year, out DateTime dt)
    {
        if (DateTime.TryParse(ch.HolidayDate, out var parsed))
        {
            if (ch.IsRecurring)
            {
                dt = new DateTime(year, parsed.Month, parsed.Day);
                return true;
            }
            if (parsed.Year == year)
            {
                dt = parsed;
                return true;
            }
        }
        dt = default;
        return false;
    }

    private void BtnPrevYear_Click(object sender, RoutedEventArgs e)
    {
        _currentYear--;
        LblYear.Text = $"{_currentYear}年";
        LoadHolidays();
    }

    private void BtnNextYear_Click(object sender, RoutedEventArgs e)
    {
        _currentYear++;
        LblYear.Text = $"{_currentYear}年";
        LoadHolidays();
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            $"今年({_currentYear}年)の日本の祝日を会社祝日として追加しますか？\n" +
            "既に存在する祝日はスキップされます。",
            "祝日の取り込み", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        var jpMap = JapaneseHoliday.GetHolidays(_currentYear);
        int added = 0;
        foreach (var kv in jpMap.OrderBy(k => k.Key))
        {
            var dateStr = kv.Key.ToString("yyyy-MM-dd");
            // 重複チェック：同じ日付・同じ名前のものがないか
            var existing = _db.GetAllCompanyHolidays()
                .FirstOrDefault(c =>
                    string.Equals(c.HolidayDate, dateStr, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.Name, kv.Value, StringComparison.OrdinalIgnoreCase));
            if (existing != null) continue;

            _db.InsertCompanyHoliday(new CompanyHoliday
            {
                HolidayDate = dateStr,
                Name = kv.Value,
                IsRecurring = false,
                Note = "日本の祝日から取り込み",
            });
            added++;
        }

        MessageBox.Show($"取り込み完了: {added}件追加", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        LoadHolidays();
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "会社祝日データのエクスポート",
            Filter = "JSONファイル (*.json)|*.json",
            FileName = $"CompanyHolidays_{_currentYear}.json"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var data = _db.GetAllCompanyHolidays()
                .Where(c => !string.IsNullOrEmpty(c.Name))
                .ToList();
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);
            MessageBox.Show($"エクスポート完了: {data.Count}件", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"エクスポート失敗: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnAddNew_Click(object sender, RoutedEventArgs e)
    {
        var newItem = new HolidayItem
        {
            HolidayDate = new DateTime(_currentYear, 1, 1),
            Name = "",
            Note = "",
            IsRecurring = false,
            Type = "会社",
            IsReadOnly = false,
        };
        _items.Add(newItem);
        DgHolidays.ScrollIntoView(newItem);
        LblStatus.Text = $"祝日総数: {_items.Count}件";
    }

    internal void RemoveHoliday(HolidayItem item)
    {
        if (item.IsReadOnly) return;
        if (item.Id.HasValue)
        {
            _db.DeleteCompanyHoliday(item.Id.Value);
        }
        _items.Remove(item);
        LblStatus.Text = $"祝日総数: {_items.Count}件";
    }

    private void SaveItem(HolidayItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Name)) return;

        var dateStr = item.HolidayDate.ToString("yyyy-MM-dd");

        // 既存の会社祝日がある場合は上書き
        // 日本の祝日などIdがないものは新規追加（重複チェック）
        if (item.Id.HasValue)
        {
            _db.UpdateCompanyHoliday(new CompanyHoliday
            {
                Id = item.Id.Value,
                HolidayDate = dateStr,
                Name = item.Name,
                Note = item.Note ?? "",
                IsRecurring = item.IsRecurring,
            });
        }
        else
        {
            // 既に同じ日付＋名前の会社祝日があれば上書きする（重複防止）
            var existing = _db.GetAllCompanyHolidays()
                .FirstOrDefault(c =>
                    c.HolidayDate == dateStr &&
                    c.Name == item.Name);
            if (existing != null)
            {
                existing.Name = item.Name;
                existing.Note = item.Note ?? "";
                existing.IsRecurring = item.IsRecurring;
                _db.UpdateCompanyHoliday(existing);
                item.Id = existing.Id;
            }
            else
            {
                int newId = _db.InsertCompanyHoliday(new CompanyHoliday
                {
                    HolidayDate = dateStr,
                    Name = item.Name,
                    Note = item.Note ?? "",
                    IsRecurring = item.IsRecurring,
                });
                item.Id = newId;
            }
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        // 日付が変わった日本の祝日がある場合、元の日付を祝日除外する上書きを追加
        foreach (var item in _items)
        {
            if (item.Id.HasValue) continue;
            if (string.IsNullOrWhiteSpace(item.Name)) continue;
            if (item.OriginalDate != default && item.OriginalDate != item.HolidayDate)
            {
                var origStr = item.OriginalDate.ToString("yyyy-MM-dd");
                var existing = _db.GetAllCompanyHolidays()
                    .FirstOrDefault(c => c.HolidayDate == origStr && string.IsNullOrEmpty(c.Name));
                if (existing == null)
                {
                    _db.InsertCompanyHoliday(new CompanyHoliday
                    {
                        HolidayDate = origStr,
                        Name = "",
                        IsRecurring = item.IsRecurring,
                        Note = "",
                    });
                }
            }
        }

        int saved = 0;
        foreach (var item in _items)
        {
            if (string.IsNullOrWhiteSpace(item.Name)) continue;
            // Idがなく、OriginalDateが設定済みで日付未変更の日本の祝日はスキップ（未編集）
            if (!item.Id.HasValue && item.OriginalDate != default && item.OriginalDate == item.HolidayDate)
                continue;
            SaveItem(item);
            if (item.Id.HasValue) item.IsReadOnly = false;
            saved++;
        }
        MessageBox.Show($"保存しました（{saved}件）", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "全ての会社祝日を削除し、日本の祝日のみの状態に戻します。よろしいですか？",
            "デフォルトに戻す", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        // 全ての会社祝日を削除
        foreach (var ch in _db.GetAllCompanyHolidays())
        {
            _db.DeleteCompanyHoliday(ch.Id);
        }
        LoadHolidays();
        MessageBox.Show("デフォルト状態に戻しました", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

/// <summary>
/// 祝日リスト表示用のデータモデル
/// </summary>
public class HolidayItem
{
    public int? Id { get; set; }
    public DateTime HolidayDate { get; set; }
    /// <summary>日本の祝日など、読み込み時の元の日付（変更検知用）</summary>
    public DateTime OriginalDate { get; set; }
    public string Name { get; set; } = "";
    public string Note { get; set; } = "";
    public bool IsRecurring { get; set; }
    public string Type { get; set; } = "";
    public bool IsReadOnly { get; set; }
    public ICommand RemoveCommand { get; }

    public HolidayItem()
    {
        RemoveCommand = new HolidayRemoveCommand(this);
    }
}

internal class HolidayRemoveCommand : ICommand
{
    private readonly HolidayItem _item;
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public HolidayRemoveCommand(HolidayItem item) => _item = item;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter)
    {
        var window = Application.Current.Windows.OfType<HolidaySettingsWindow>().FirstOrDefault();
        window?.RemoveHoliday(_item);
    }
}
