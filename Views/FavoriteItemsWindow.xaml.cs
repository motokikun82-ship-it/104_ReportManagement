using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ReportManagement.Helpers;
using ReportManagement.Models;
using ReportManagement.Services;

namespace ReportManagement.Views;

public partial class FavoriteItemsWindow : Window
{
    private readonly DatabaseService _db = DatabaseService.Instance;
    private readonly ObservableCollection<TemplateGroup> _groups = new();
    private readonly ObservableCollection<TemplateItem> _groupItems = new();
    private readonly ObservableCollection<EntryHistoryItem> _history = new();
    private readonly string _targetDate;

    public FavoriteItemsWindow(string targetDate)
    {
        InitializeComponent();
        _targetDate = targetDate;
        CmbGroups.ItemsSource = _groups;
        LbGroupItems.ItemsSource = _groupItems;
        LbHistory.ItemsSource = _history;
        LoadData();
    }

    private void LoadData()
    {
        _groups.Clear();
        foreach (var g in _db.GetAllTemplateGroups())
            _groups.Add(g);

        _history.Clear();
        foreach (var item in _db.GetEntryHistory(50, _targetDate))
            _history.Add(item);

        UpdateGroupItemsVisibility();
    }

    private void CmbGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _groupItems.Clear();
        if (CmbGroups.SelectedItem is TemplateGroup group)
        {
            foreach (var item in _db.GetItemsByGroupId(group.Id))
                _groupItems.Add(item);
        }
        UpdateGroupItemsVisibility();
    }

    private void UpdateGroupItemsVisibility()
    {
        bool hasGroup = CmbGroups.SelectedItem != null;
        LblGroupItems.Visibility = hasGroup ? Visibility.Visible : Visibility.Collapsed;
        LbGroupItems.Visibility = hasGroup ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnOpenTemplateManager_Click(object sender, RoutedEventArgs e)
    {
        var win = new TemplateManagerWindow { Owner = this };
        win.ShowDialog();
        LoadData();
    }

    private void BtnAddToDate_Click(object sender, RoutedEventArgs e)
    {
        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var added = false;

        // グループ内の選択項目を追加
        var selectedGroupItems = LbGroupItems.SelectedItems.Cast<TemplateItem>().ToList();
        if (selectedGroupItems.Count > 0)
        {
            var existing = _db.GetEntriesByDate(_targetDate);
            int baseSort = existing.Count > 0 ? existing.Max(x => x.SortOrder) + 10 : 0;

            foreach (var item in selectedGroupItems.OrderBy(i => i.SortOrder))
            {
                _db.InsertEntry(new DailyEntry
                {
                    EntryDate   = _targetDate,
                    ItemName    = item.ItemName,
                    Count       = item.DefaultCount,
                    IsCountable = item.IsCountable,
                    Note        = "",
                    SortOrder   = baseSort,
                    CreatedAt   = now,
                    UpdatedAt   = now,
                });
                baseSort += 10;
            }
            added = true;
        }

        // 履歴から選択した項目を追加（テンプレートに存在すれば初期件数・IsCountableを継承）
        var selectedHistory = LbHistory.SelectedItems.Cast<EntryHistoryItem>().ToList();
        if (selectedHistory.Count > 0)
        {
            var existing = _db.GetEntriesByDate(_targetDate);
            int baseSort = existing.Count > 0 ? existing.Max(x => x.SortOrder) + 10 : 0;

            foreach (var item in selectedHistory)
            {
                var tmpl = _db.FindTemplateItem(item.ItemName);
                _db.InsertEntry(new DailyEntry
                {
                    EntryDate   = _targetDate,
                    ItemName    = item.ItemName,
                    Count       = tmpl?.DefaultCount ?? 1,
                    IsCountable = tmpl?.IsCountable ?? true,
                    Note        = "",
                    SortOrder   = baseSort,
                    CreatedAt   = now,
                    UpdatedAt   = now,
                });
                baseSort += 10;
            }
            added = true;
        }

        if (!added)
        {
            MessageBox.Show("グループの項目または履歴から追加するものを選択してください。", "情報",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
