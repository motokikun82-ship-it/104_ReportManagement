# 祝日・休日設定画面のクラッシュ問題とパフォーマンス改善の実装計画

## 目標
「祝日・休日設定」画面を開いたり操作したりする際にアプリが強制終了（StackOverflowException）する問題を解決し、同時にカレンダー描画時のデータベース過負荷（パフォーマンス問題）を解消します。

## 提案される変更内容

### 1. 例外ハンドリングの修正（無限ループの回避）
WPFのレイアウトやデータバインディング処理中に発生した例外が `App.xaml.cs` の `DispatcherUnhandledException` に渡された際、同期的に `MessageBox.Show` を呼び出すことで「エラー → メッセージ表示 → 再描画試行 → エラー」の無限ループ（StackOverflowException）に陥る既知の不具合を修正します。

* **[MODIFY] [App.xaml.cs](file:///C:/Users/BEST/Desktop/morinaga/Script/104_ReportManagement/App.xaml.cs)**
  * `ev.Handled = true;` を `MessageBox.Show` の前に移動。
  * `MessageBox.Show` の呼び出しを `Dispatcher.InvokeAsync` を用いて非同期化し、メッセージループ中の描画再試行を防ぎます。

### 2. リソース参照の修正（クラッシュの根本原因の解決）
サブウィンドウ表示時に発生しやすい、テーマリソースの解決失敗（XamlParseException）を防ぐため、静的リソース参照を動的リソース参照に変更します。

* **[MODIFY] [HolidaySettingsWindow.xaml](file:///C:/Users/BEST/Desktop/morinaga/Script/104_ReportManagement/Views/HolidaySettingsWindow.xaml)**
  * `StaticResource` で指定されているリソース参照（ブラシなど）を `DynamicResource` に変更し、実行時のテーマ解決に柔軟に対応させます。

### 3. パフォーマンスの抜本的改善（データベース通信の削減）
現在、`CalendarViewModel` でカレンダーを描画する際、全ての日付（約35日分）に対して `DatabaseService.IsCompanyHoliday` を都度呼び出し、その中で毎回全件取得クエリが走っています。これを1回の処理で済むように改善します。

* **[MODIFY] [CalendarViewModel.cs](file:///C:/Users/BEST/Desktop/morinaga/Script/104_ReportManagement/ViewModels/CalendarViewModel.cs)**
  * カレンダー描画の直前に `DatabaseService.GetAllCompanyHolidays()` を1度だけ呼び出して変数に保持し、ループ内ではその変数を使用して祝日判定を行います。
* **[MODIFY] [DatabaseService.cs](file:///C:/Users/BEST/Desktop/morinaga/Script/104_ReportManagement/Services/DatabaseService.cs)**
  * `IsCompanyHoliday` メソッドのロジックを見直し、DBとの不必要な往復通信を削減します。

## 検証計画
### 手動検証
1. アプリを起動し、設定画面から「祝日・休日を編集」ボタンを押して画面が正常に開くことを確認する。
2. 祝日の追加、編集、削除を行い、エラーなく保存されることを確認する。
3. 意図的に不正な値を入力しても、アプリが落ちずに適切なエラーメッセージや入力拒否が行われるか確認する。
4. メイン画面のカレンダー表示がスムーズに行われ、パフォーマンス上のもたつきがないか確認する。
