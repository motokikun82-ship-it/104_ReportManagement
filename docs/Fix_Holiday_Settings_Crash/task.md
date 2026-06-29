# タスクリスト: 祝日・休日設定画面のクラッシュ問題とパフォーマンス改善

- `[x]` App.xaml.cs の `DispatcherUnhandledException` で例外処理による無限ループを防ぐため、MessageBox.Showを非同期化し `ev.Handled = true` を先に設定する
- `[x]` HolidaySettingsWindow.xaml 内の静的リソース (`StaticResource`) を動的リソース (`DynamicResource`) に変更し、パース時のエラーを防ぐ
- `[x]` CalendarViewModel.cs 内で、カレンダー描画時に毎回DBにアクセスする非効率な処理を、ループ前にまとめて取得するようにリファクタリングする
- `[x]` アプリをビルドしてクラッシュせずに「祝日・休日設定」画面が開くか動作確認する
- `[x]` パフォーマンスが向上していることを確認する
