using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ReportManagement.Helpers;
using ReportManagement.Models;
using ReportManagement.Services;

namespace ReportManagement.Views;

public partial class TemplateManagerWindow : Window
{
    private readonly DatabaseService _db = DatabaseService.Instance;
    private ObservableCollection<TemplateGroup> _groups = new();
    private ObservableCollection<TemplateItem> _currentItems = new();
    private TemplateGroup? _selectedGroup => CmbGroups.SelectedItem as TemplateGroup;
    private bool _isUpdating = false;

    public TemplateManagerWindow()
    {
        InitializeComponent();
        DgItems.ItemsSource = _currentItems;
        LoadGroups();
        Closing += (_, _) => DgItems.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private void LoadGroups(int? selectGroupId = null)
    {
        _isUpdating = true;
        _groups.Clear();
        foreach (var grp in _db.GetAllTemplateGroups())
            _groups.Add(grp);
        CmbGroups.ItemsSource = _groups;

        if (_groups.Count > 0)
        {
            if (selectGroupId.HasValue)
                CmbGroups.SelectedItem = _groups.FirstOrDefault(g => g.Id == selectGroupId.Value);
            if (CmbGroups.SelectedItem == null)
                CmbGroups.SelectedIndex = 0;
        }
        else
        {
            LoadItems(null);
        }
        _isUpdating = false;

        if (_groups.Count > 0)
            LoadItems(_selectedGroup);
    }

    private void CmbGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdating) return;
        LoadItems(_selectedGroup);
    }

    private void LoadItems(TemplateGroup? group)
    {
        _isUpdating = true;
        _currentItems.Clear();
        if (group != null)
        {
            ChkIsDefault.IsChecked = group.IsDefault;
            var items = _db.GetItemsByGroupId(group.Id);
            foreach (var item in items)
                _currentItems.Add(item);
        }
        else
        {
            ChkIsDefault.IsChecked = false;
        }
        _isUpdating = false;
    }

    private void BtnAddGroup_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Show("新規グループ", "グループ名を入力してください。", "新しいグループ");
        if (string.IsNullOrWhiteSpace(name)) return;

        var group = new TemplateGroup { GroupName = name };
        group.Id = _db.InsertTemplateGroup(group);
        LoadGroups();
        CmbGroups.SelectedItem = _groups.FirstOrDefault(g => g.Id == group.Id);
    }

    private void BtnRenameGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGroup == null) return;
        var newName = InputDialog.Show("グループ名変更", "新しいグループ名を入力してください。", _selectedGroup.GroupName);
        if (string.IsNullOrWhiteSpace(newName) || newName == _selectedGroup.GroupName) return;

        _selectedGroup.GroupName = newName;
        _db.UpdateTemplateGroup(_selectedGroup);

        // ComboBox の表示を更新
        var idx = CmbGroups.SelectedIndex;
        CmbGroups.ItemsSource = null;
        CmbGroups.ItemsSource = _groups;
        CmbGroups.SelectedIndex = idx;
    }

    private void BtnDeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGroup == null) return;
        if (MessageBox.Show($"「{_selectedGroup.GroupName}」を削除しますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            _db.DeleteTemplateGroup(_selectedGroup.Id);
            LoadGroups();
        }
    }

    private void ChkIsDefault_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdating || _selectedGroup == null) return;
        
        bool isDefault = ChkIsDefault.IsChecked == true;
        
        // 他のグループのデフォルトフラグを落とす
        if (isDefault)
        {
            foreach (var g in _groups.Where(g => g.Id != _selectedGroup.Id))
            {
                g.IsDefault = false;
                _db.UpdateTemplateGroup(g);
            }
        }
        
        _selectedGroup.IsDefault = isDefault;
        _db.UpdateTemplateGroup(_selectedGroup);
    }

    private void BtnAddItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGroup == null) return;
        int maxSort = _currentItems.Count > 0 ? _currentItems.Max(i => i.SortOrder) + 10 : 0;
        var item = new TemplateItem
        {
            GroupId = _selectedGroup.Id,
            ItemName = "新規項目",
            IsCountable = true,
            DefaultCount = 1,
            SortOrder = maxSort
        };
        item.Id = _db.InsertTemplateItem(item);
        LoadItems(_selectedGroup);
    }

    private void BtnDeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is TemplateItem item)
        {
            _db.DeleteTemplateItem(item.Id);
            _currentItems.Remove(item);
        }
    }

    private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is TemplateItem item)
            MoveItem(item, -1);
    }

    private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is TemplateItem item)
            MoveItem(item, +1);
    }

    private void MoveItem(TemplateItem item, int direction)
    {
        // direction: -1 = 上に移動, +1 = 下に移動
        var sorted = _currentItems.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToList();
        int idx = sorted.IndexOf(item);
        int target = idx + direction;
        if (target < 0 || target >= sorted.Count) return;

        var other = sorted[target];
        (item.SortOrder, other.SortOrder) = (other.SortOrder, item.SortOrder);

        _db.UpdateTemplateItem(item);
        _db.UpdateTemplateItem(other);

        LoadItems(_selectedGroup);
        // 移動後の項目を選択状態にする
        DgItems.SelectedItem = _currentItems.FirstOrDefault(i => i.Id == item.Id);
    }

    private void DgItems_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not TemplateItem item) return;

        // 編集中のセルのバインディングを強制的に反映してから保存
        if (e.Column is DataGridTextColumn textColumn)
        {
            var element = textColumn.GetCellContent(e.Row.Item);
            if (element is TextBox textBox)
            {
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            }
        }

        _db.UpdateTemplateItem(item);
    }

    // ─── グループ並び替え ────────────────────────────────────────

    private void BtnGroupMoveUp_Click(object sender, RoutedEventArgs e)
    {
        MoveGroup(-1);
    }

    private void BtnGroupMoveDown_Click(object sender, RoutedEventArgs e)
    {
        MoveGroup(+1);
    }

    private void MoveGroup(int direction)
    {
        if (_selectedGroup == null) return;
        int idx = _groups.IndexOf(_selectedGroup);
        if (idx < 0) return;
        int target = idx + direction;
        if (target < 0 || target >= _groups.Count) return;

        // ObservableCollection 内で入れ替え（UI が即時反映される）
        _groups.Move(idx, target);

        // SortOrder をインデックス通りに振り直して DB 保存
        for (int i = 0; i < _groups.Count; i++)
            _groups[i].SortOrder = i * 10;
        foreach (var g in _groups)
            _db.UpdateTemplateGroup(g);

        // 選択を維持
        CmbGroups.SelectedItem = _selectedGroup;
    }

    // ─── 項目のグループ移動 ──────────────────────────────────────

    private void BtnMoveItemGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not TemplateItem item) return;

        var groups = _groups.Where(g => g.Id != item.GroupId).ToList();
        if (groups.Count == 0)
        {
            MessageBox.Show("移動先のグループがありません。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var picker = new Window
        {
            Title = "移動先グループを選択",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Padding = new Thickness(15),
        };
        var sp = new StackPanel { Margin = new Thickness(10) };
        sp.Children.Add(new TextBlock
        {
            Text = $"「{item.ItemName}」の移動先:",
            Margin = new Thickness(0, 0, 0, 10),
            FontWeight = FontWeights.Bold,
        });

        var cmb = new ComboBox
        {
            ItemsSource = groups,
            DisplayMemberPath = "GroupName",
            SelectedIndex = 0,
            MinWidth = 250,
            Margin = new Thickness(0, 0, 0, 10),
        };
        sp.Children.Add(cmb);

        var btnOk = new Button
        {
            Content = "移動",
            HorizontalAlignment = HorizontalAlignment.Right,
            Width = 80,
        };
        btnOk.Click += (_, _) =>
        {
            if (cmb.SelectedItem is TemplateGroup target)
            {
                item.GroupId = target.Id;
                _db.UpdateTemplateItem(item);
                LoadItems(_selectedGroup);
            }
            picker.DialogResult = true;
        };
        sp.Children.Add(btnOk);
        picker.Content = sp;
        picker.ShowDialog();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
