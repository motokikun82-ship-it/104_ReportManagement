using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using ReportManagement.Models;
using ReportManagement.Services;

namespace ReportManagement.Views;

public partial class SettingsWindow : Window
{
    private readonly DatabaseService _db = DatabaseService.Instance;
    private readonly ObservableCollection<string> _themes = new()
    {
        "DarkTheme (ミントグリーン)",
        "ClassicDarkTheme (クラシックダーク)",
        "OceanDarkTheme (オーシャンブルー)",
        "SunsetDarkTheme (サンセットオレンジ)",
        "LightTheme (クリアホワイト)"
    };

    private static readonly string[] ThemeFiles = ["DarkTheme", "ClassicDarkTheme", "OceanDarkTheme", "SunsetDarkTheme", "LightTheme"];

    public SettingsWindow()
    {
        InitializeComponent();
        CmbTheme.ItemsSource = _themes;

        string current = _db.GetSetting("Theme", "DarkTheme");
        int idx = ThemeFiles.ToList().IndexOf(current);
        CmbTheme.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "データのエクスポート",
            Filter = "JSONファイル (*.json)|*.json",
            FileName = $"ReportManagement_{DateTime.Now:yyyyMMdd}.json"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var data = new ExportData
            {
                DailyEntries = _db.GetAllEntries(),
                Notes        = _db.GetAllNotes(),
                TodoTasks    = _db.GetAllTodoTasks(),
            };
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);
            MessageBox.Show($"エクスポート完了\n{data.DailyEntries.Count}件のエントリー\n{data.Notes.Count}件のメモ\n{data.TodoTasks.Count}件のToDo", "完了",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"エクスポートに失敗しました: {ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "データのインポート",
            Filter = "JSONファイル (*.json)|*.json",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            string json = File.ReadAllText(dlg.FileName);
            var data = JsonSerializer.Deserialize<ExportData>(json);
            if (data == null) { MessageBox.Show("ファイルの読み込みに失敗しました。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error); return; }

            int entryInserted = 0, entryUpdated = 0, noteInserted = 0, noteUpdated = 0, todoInserted = 0, todoUpdated = 0;

            // エントリー：Upsert（日付＋項目名が一致すればUPDATE、なければINSERT）
            foreach (var entry in data.DailyEntries)
            {
                var existingId = _db.GetEntryIdByKey(entry.EntryDate, entry.ItemName);
                if (existingId.HasValue)
                {
                    entry.Id = existingId.Value;
                    _db.UpdateEntry(entry);
                    entryUpdated++;
                }
                else
                {
                    _db.InsertEntry(entry);
                    entryInserted++;
                }
            }

            // メモ：Upsert（タイトル＋日付が一致すればUPDATE、なければINSERT）
            foreach (var note in data.Notes)
            {
                var existingId = _db.GetNoteIdByKey(note.Title, note.NoteDate);
                if (existingId.HasValue)
                {
                    note.Id = existingId.Value;
                    _db.UpdateNote(note);
                    noteUpdated++;
                }
                else
                {
                    _db.InsertNote(note);
                    noteInserted++;
                }
            }

            // ToDo：Upsert（日付＋タスク名が一致すればUPDATE、なければINSERT）
            foreach (var todo in data.TodoTasks)
            {
                var existingId = _db.GetTodoIdByKey(todo.TaskDate, todo.TaskName);
                if (existingId.HasValue)
                {
                    todo.Id = existingId.Value;
                    _db.UpdateTodo(todo);
                    todoUpdated++;
                }
                else
                {
                    _db.InsertTodo(todo);
                    todoInserted++;
                }
            }

            MessageBox.Show(
                $"インポート完了\n" +
                $"  エントリー: {entryInserted}件 新規, {entryUpdated}件 更新\n" +
                $"  メモ:       {noteInserted}件 新規, {noteUpdated}件 更新\n" +
                $"  ToDo:       {todoInserted}件 新規, {todoUpdated}件 更新",
                "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"インポートに失敗しました: {ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        string themeFile = ThemeFiles[CmbTheme.SelectedIndex];
        _db.SetSetting("Theme", themeFile);

        // テーマを即時適用して再起動
        App.ApplyTheme(themeFile);

        // アプリケーションを再起動
        System.Diagnostics.Process.Start(
            System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName);
        Application.Current.Shutdown();
    }

    private void BtnHoliday_Click(object sender, RoutedEventArgs e)
    {
        var win = new HolidaySettingsWindow { Owner = this };
        win.ShowDialog();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
