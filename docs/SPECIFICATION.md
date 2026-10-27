# 仕様書: 業務日報管理アプリ (ReportManagement)

## 📌 1. 目的
日々の業務（例：「商品登録 何件」「メール対応 何件」など）を記録・集計するための軽量なWindowsデスクトップアプリケーション。毎日の起動速度を重視し、WebViewなどのブラウザ技術ではなくネイティブのWPF（Windows Presentation Foundation）を採用。

## ✨ 2. 特徴
- **C# WPF + MVVMアーキテクチャ**: `CommunityToolkit.Mvvm` を活用したモダンで堅牢な設計。
- **SQLiteローカルDB**: ネットワーク不要、超軽量なデータ永続化（WALモード有効化済みで高速書き込み）。
- **完全リアルタイム保存**: 保存ボタンを排除し、ユーザー入力の変更検知と同時にバックグラウンドでDBを更新。
- **多機能統合デスクトップ**: 日報入力・ToDo管理・スケジュール通知・メモ帳・グラフ集計・祝日カスタマイズ・テーマ切替を1アプリで包括。

---

## 📋 3. 機能一覧と簡易説明

| 機能名 | 説明 |
|---|---|
| **カレンダーナビゲーション** | 日付を視覚的に選択。日報(青丸)・未完了ToDo(赤丸)・メモ(オレンジ丸)のマーカー表示、祝日着色、今日へのジャンプ。 |
| **日報自動生成** | アプリ起動時、当日のデータが空ならデフォルトテンプレートを自動展開。スケジュールテンプレートの自動適用・通知も対応。 |
| **日報入力（件数あり/なし）** | 「項目＋件数」または「テキストのみの突発メモ」の2種類の形式をサポート。実行チェック機能、並び替え、よく使う項目からの登録。 |
| **自動引き継ぎToDo** | 独立した残タスクToDoリスト。未完了タスクは翌日へ自動引き継ぎ。開始/終了時刻や詳細メモのインライン展開・検索対応。 |
| **横断検索** | 過去の日報から項目名やメモのキーワードで該当データを一覧表示＆該当日にジャンプ。一括削除にも対応。 |
| **テンプレート管理** | テンプレートグループと項目のカスタマイズ（デフォルト指定、要チェックフラグ、件数管理の有無、並び順）。 |
| **スケジュールテンプレート** | 曜日や特定日（日付指定・祝日除外等）に自動挿入する定期タスク・リマインダー通知の設定。 |
| **よく使う項目（お気に入り）** | 頻出する業務項目を登録しておき、ワンクリックで当日の日報に追加。 |
| **メモ帳機能** | 日付に紐づくメモ（日報とは別枠）および全体共通のピン留め・一般メモの作成・編集。 |
| **集計・グラフ出力 (LiveCharts2)** | 日別・週別・月別・年別の推移集計。折れ線/積み上げ棒グラフ、平均値算出、CSVエクスポート、項目ごとの絞り込み（全項目／グループ別）。 |
| **祝日設定** | 内閣府祝日API/CSVによる自動同期、カスタム休日（社内創立記念日等）の手動登録、カレンダー連動。 |
| **テーマ設定** | 複数テーマ（Dark, Classic Dark, Ocean Dark, Sunset Dark, Light）の切り替え。 |

---

## 📂 4. ファイル構成

```text
104_ReportManagement/
├── ReportManagement.csproj        # プロジェクト設定ファイル (.NET 8)
├── App.xaml / App.xaml.cs         # アプリケーションエントリ、テーマ適用、DB初期化
├── MainWindow.xaml / .xaml.cs     # メイン画面（カレンダー、日報、ToDo、検索、スケジュール通知）
├── docs/                          # プロジェクトドキュメント
│   ├── README.md                  # 取扱説明書・起動手順
│   ├── SPECIFICATION.md           # 本仕様書
│   └── Update_Aggregation_Selection/ # 今回の変更履歴・計画書
├── Models/                        # データモデル層
│   ├── DailyEntry.cs              # 日報エントリーモデル（実行チェック・要チェック等含む）
│   ├── FavoriteItem.cs            # よく使う項目モデル
│   ├── Holiday.cs                 # 祝日・休日モデル
│   ├── Note.cs                    # メモモデル
│   ├── ScheduleTemplate.cs        # スケジュールテンプレートモデル
│   ├── TemplateGroup.cs           # テンプレートグループ
│   ├── TemplateItem.cs            # テンプレート内の項目
│   └── TodoTask.cs                # ToDoタスクモデル（時刻・詳細・展開状態）
├── ViewModels/                    # プレゼンテーションロジック層 (MVVM)
│   ├── MainViewModel.cs           # メイン画面全体統括、検索処理、通知ダイアログ
│   ├── DailyEntryViewModel.cs     # 日報1行のUI状態管理、自動保存処理、並び替え
│   ├── CalendarViewModel.cs       # カレンダー月移動、セル状態・祝日・マーカー管理
│   ├── TodoViewModel.cs           # ToDoリスト全体の管理（引き継ぎ、検索）
│   └── AggregationViewModel.cs    # 集計画面ロジック（グラフ描画、全選択/グループ選択、CSV出力）
├── Views/                         # ウィンドウ・ダイアログ
│   ├── AggregationWindow.xaml / .cs     # 集計レポート画面（LiveChartsグラフ、クロス集計表）
│   ├── FavoriteItemsWindow.xaml / .cs   # よく使う項目選択ウィンドウ
│   ├── HolidaySettingsWindow.xaml / .cs # 祝日・社内休日設定ウィンドウ
│   ├── NotesWindow.xaml / .cs           # メモ一覧・編集ウィンドウ
│   ├── ScheduleTemplateWindow.xaml / .cs# スケジュールテンプレート設定ウィンドウ
│   ├── SettingsWindow.xaml / .cs        # 全体設定・テーマ切替ウィンドウ
│   └── TemplateManagerWindow.xaml / .cs # テンプレート管理ウィンドウ
├── Services/                      # ビジネスロジック・データアクセス層
│   ├── DatabaseService.cs         # SQLiteの全CRUD操作・マイグレーション
│   └── AggregationService.cs      # 集計用クエリ実行（日・週・月・年単位）
├── Converters/                    # UIバインディング変換処理（XAML用）
│   ├── ValueConverters.cs         # 各種表示・色・アイコン変換
│   └── AggregationConverters.cs   # 集計画面専用コンバーター
└── Themes/                        # スタイルリソース（XAML）
    ├── DarkTheme.xaml             # 標準ダークテーマ
    ├── ClassicDarkTheme.xaml      # クラシックダーク
    ├── OceanDarkTheme.xaml        # オーシャンブルー系ダーク
    ├── SunsetDarkTheme.xaml       # サンセット系ダーク
    └── LightTheme.xaml            # ライトテーマ
```

---

## ⚙️ 5. 詳細な機能説明

### データベース設計 (SQLite: report.db)
WALモード（`PRAGMA journal_mode=WAL;`）により、UIのブロックを起こさず高速に読み書きを行います。

1. **DailyEntries**:
   - `Id`, `EntryDate`, `ItemName`, `Count`, `IsCountable`, `Note`, `SortOrder`, `IsExecuted`, `ShowCheck`
   - `ShowCheck=1` の場合、未実行時に警告アイコンを表示し、実行チェック可能。
2. **TemplateGroups**:
   - `Id`, `GroupName`, `SortOrder`, `IsDefault`
3. **TemplateItems**:
   - `Id`, `GroupId`, `ItemName`, `DefaultCount`, `IsCountable`, `SortOrder`, `ShowCheck`
4. **TodoTasks**:
   - `Id`, `TaskDate`, `TaskName`, `IsCompleted`, `SortOrder`, `CreatedAt`, `StartTime`, `EndTime`, `Description`
5. **Notes**:
   - `Id`, `TargetDate`, `Title`, `Content`, `IsPinned`, `CreatedAt`, `UpdatedAt`
6. **Holidays**:
   - `HolidayDate`, `HolidayName`, `IsCustom`
7. **ScheduleTemplates**:
   - `Id`, `ScheduleType`, `TargetDay`, `ItemName`, `DefaultCount`, `IsCountable`, `ShowCheck`, `NotifyOnStartup`, `IsEnabled`
8. **FavoriteItems**:
   - `Id`, `ItemName`, `DefaultCount`, `IsCountable`, `ShowCheck`, `SortOrder`
9. **AppSettings**:
   - `Key`, `Value`（テーマ選択等のユーザー設定）

### 集計・グラフ表示ロジック
- **集計対象**: `IsCountable = 1` かつ実行済み（またはチェック不要）の項目を集計。
- **期間モード**: 月別（当年1〜12月）、年別（全過去年）、カスタム期間（任意範囲）から選択。
- **表示単位**: 日、週（月曜起算）、月、年単位に集計。
- **項目の選択・絞り込み**:
  - 「全項目を選択」「全項目を解除」ボタン：グループをまたいですべての項目を一括でON/OFF。
  - 「グループ内全選択」「グループ内全解除」ボタン：現在タブで選択されているグループの項目のみを一括でON/OFF。
  - チェック状態は `aggregation_state.json` にローカル保存され、次回起動時にも復元。
- **出力**:
  - LiveCharts2による動的な折れ線グラフまたは積み上げ棒グラフ。
  - 期間ごとの件数と平均値を算出したクロス集計表（DataTable）。
  - 集計表のCSVファイル書き出し機能。

---

## 🔀 6. 分岐による動作の違い（条件表）

| 条件・アクション | 動作結果 |
|---|---|
| **アプリ起動時（当日の日報が0件の場合）** | `IsDefault=1` のテンプレートグループから項目を自動展開し、さらに当日に合致する有効なスケジュールテンプレートを展開・通知する。 |
| **アプリ起動時（当日の日報が1件以上ある場合）** | テンプレートの自動展開は行わず、既存データを読み込む。未適用のスケジュール通知のみ必要に応じて表示。 |
| **アプリ起動時のToDoリスト** | 前日のToDoリストのうち未完了（`IsCompleted=0`）のものを検索し、当日の日付へ自動引き継ぎ作成（重複防止あり）。 |
| **日報行の IsCountable=True** | ➖ / ➕ 操作ボタンと件数ボックスが表示され、集計レポートの対象となる。 |
| **日報行の IsCountable=False** | 件数操作ボタンが非表示になり、複数行メモ欄を展開して長文記録が可能になる（集計レポート対象外）。 |
| **日報行の ShowCheck=True** | 行の先頭にチェックボックスが表示される。チェックを付けると `IsExecuted=1` となり項目名が薄く表示される。未チェック時は警告マークを表示。 |
| **集計画面「全項目を選択/解除」** | 全グループ・未分類に存在するすべての項目のチェック状態を一括更新し、表とグラフを再計算する。 |
| **集計画面「グループ内全選択/解除」** | 現在表示しているグループタブ内の項目のみを対象にチェック状態を更新し、表とグラフを再計算する。 |
| **カレンダーセル表示** | 青丸＝日報あり、赤丸＝未完了ToDoあり、オレンジ丸＝メモあり、祝日/日曜＝赤字、土曜＝青字、今日＝背景ハイライト。 |
