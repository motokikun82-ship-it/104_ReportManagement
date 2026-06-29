using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ReportManagement.Helpers;
using ReportManagement.Services;

namespace ReportManagement;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 未捕捉例外をすべて捕捉
        DispatcherUnhandledException += (s, ev) =>
        {
            ev.Handled = true;
            Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show($"予期せぬエラー: {ev.Exception.Message}\n\n{ev.Exception}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        };
        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            if (ev.ExceptionObject is Exception ex)
                MessageBox.Show($"致命的エラー: {ex.Message}\n\n{ex}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        TaskScheduler.UnobservedTaskException += (s, ev) =>
        {
            MessageBox.Show($"タスクエラー: {ev.Exception.Message}\n\n{ev.Exception}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            ev.SetObserved();
        };

        var db = DatabaseService.Instance;
        db.InitializeDatabase();

        // 保存済みテーマ設定を読み込んで適用
        string themeName = db.GetSetting("Theme", "DarkTheme");
        ApplyTheme(themeName);

        // テーマのアクセントカラーでアプリアイコン(.ico)を生成
        GenerateAppIcon(themeName);

        // DB初期化完了後にMainWindowを生成・表示する
        // ※ StartupUri ではなくここで手動生成することで、
        //   InitializeDatabase() が MainViewModel より必ず先に実行されることを保証する
        var mainWindow = new MainWindow();
        mainWindow.Show();
    }

    public static void ApplyTheme(string themeName)
    {
        var dict = Current.Resources.MergedDictionaries;
        dict.Clear();

        var uri = new Uri($"Themes/{themeName}.xaml", UriKind.Relative);
        dict.Add(new ResourceDictionary { Source = uri });
    }

    /// <summary>現在のテーマに合わせたアプリアイコン(.ico)を生成する。</summary>
    private static void GenerateAppIcon(string themeName)
    {
        try
        {
            Color accent = themeName switch
            {
                "ClassicDarkTheme" => Color.FromRgb(0x00, 0x7A, 0xCC), // VS2022 blue
                "OceanDarkTheme" => Color.FromRgb(0x0E, 0xA5, 0xE9),   // Ocean blue
                "SunsetDarkTheme" => Color.FromRgb(0xFF, 0x57, 0x22),  // Sunset orange
                "LightTheme" => Color.FromRgb(0x3B, 0x82, 0xF6),       // Light blue
                _ => Color.FromRgb(0x00, 0xC8, 0x53)                   // Mint green (default DarkTheme)
            };

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            IconGenerator.Generate(iconPath, accent);
        }
        catch
        {
            // アイコン生成に失敗してもアプリは動作可能
        }
    }
}
