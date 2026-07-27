using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using ReportManagement.ViewModels;
using ReportManagement.Views;

namespace ReportManagement;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SetWindowIcon();
    }

    /// <summary>アイコンファイルを設定する。</summary>
    private void SetWindowIcon()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
        if (File.Exists(path))
            Icon = new BitmapImage(new Uri(path));
    }

    // ─────────────────────────────────────────
    // カスタムタイトルバー操作
    // ─────────────────────────────────────────

    /// <summary>タイトルバーをドラッグしてウィンドウを移動する。</summary>
    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // ダブルクリックで最大化/復元
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        DragMove();
    }

    /// <summary>最小化ボタン。</summary>
    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    /// <summary>最大化/復元ボタン。</summary>
    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        => ToggleMaximize();

    /// <summary>閉じるボタン。</summary>
    private void BtnClose_Click(object sender, RoutedEventArgs e)
        => Close();

    /// <summary>最大化と通常サイズを切り替える。</summary>
    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        // ボタンアイコンを切り替え
        BtnMaximize.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    // ─────────────────────────────────────────
    // 各ウィンドウを開くハンドラ
    // ─────────────────────────────────────────

    private void OpenTemplateManager_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        var win = new TemplateManagerWindow { Owner = this };
        win.ShowDialog();
        vm.LoadTemplateGroups();
    }

    private void OpenFavoriteItems_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        // メイン画面で選択中の日付をスケジュール登録画面のデフォルトとして渡す
        var win = new ScheduleTemplateWindow(vm.SelectedDate) { Owner = this };

        if (win.ShowDialog() == true)
        {
            vm.LoadEntriesForDate(vm.SelectedDate);
            vm.Calendar.RefreshCells();
        }
    }

    private void OpenNotesWindow_Click(object sender, RoutedEventArgs e)
    {
        var win = new NotesWindow { Owner = this };
        win.ShowDialog();
        var vm = (MainViewModel)DataContext;
        vm.Calendar.RefreshCells();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow { Owner = this };
        win.ShowDialog();
    }

    private void OpenAggregation_Click(object sender, RoutedEventArgs e)
    {
        var win = new Views.AggregationWindow { Owner = this };
        win.ShowDialog();
    }
}
