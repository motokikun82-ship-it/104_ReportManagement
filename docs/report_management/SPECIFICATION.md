# 仕様書: 業務日報管理アプリ (ReportManagement)

## 📌 1. 目的
日々の業務（例：「商品登録 何件」「メール対応 何件」など）を記録・集計するための軽量なWindowsデスクトップアプリケーション。毎日の起動速度を重視し、WebViewなどのブラウザ技術ではなくネイティブのWPF（Windows Presentation Foundation）を採用。

## ✨ 2. 特徴
- **C# WPF + MVVMアーキテクチャ**: `CommunityToolkit.Mvvm` を活用したモダンで堅牢な設計。
- **SQLiteローカルDB**: ネットワーク不要、超軽量なデータ永続化（WALモード有効化済みで高速書き込み）。
- **完全リアルタイム保存**: 保存ボタンを排除し、ユーザー入力の変更検知と同時にバックグラウンドでDBを更新。

---

## 📋 3. 機能一覧と簡易説明

| 機能名 | 説明 |
|---|---|
| **カレンダーナビゲーション** | 日付を視覚的に選択。日報が存在する日はマーカーでハイライト。 |
| **日報自動生成** | アプリ起動時、当日のデータが空ならデフォルトテンプレートを自動展開。 |
| **日報入力（件数あり/なし）** | 「項目＋件数」または「テキストのみの突発メモ」の2種類の形式をサポート。 |
| **自動引き継ぎToDo** | テンプレートとは独立した手動ToDoリスト。未完了タスクは翌日へ自動引き継ぎ。 |
| **横断検索** | 過去の日報から、項目名やメモのキーワードで該当データを一覧表示＆ジャンプ。 |
| **テンプレート管理** | 複数のテンプレートグループを作成し、業務に応じて必要な項目を呼び出し。 |

---

## 📂 4. ファイル構成

```text
104_ReportManagement/
├── ReportManagement.csproj        # プロジェクト設定ファイル (.NET 8)
├── App.xaml / App.xaml.cs         # アプリケーションエントリ、DB初期化
├── MainWindow.xaml / .xaml.cs     # メイン画面（カレンダー、日報、ToDo、検索）
├── docs/report_management/        # ドキュメントディレクトリ
│   ├── README.md
│   └── SPECIFICATION.md
├── Models/                        # データモデル層
│   ├── DailyEntry.cs              # 日報エントリー（行）モデル
│   ├── TemplateGroup.cs           # テンプレートグループ
│   ├── TemplateItem.cs            # テンプレート内の項目
│   └── TodoTask.cs                # ToDoタスクモデル
├── ViewModels/                    # プレゼンテーションロジック層 (MVVM)
│   ├── MainViewModel.cs           # メイン画面全体統括、検索処理
│   ├── DailyEntryViewModel.cs     # 日報1行のUI状態管理、自動保存処理
│   ├── CalendarViewModel.cs       # カレンダー月移動、セル状態管理
│   └── TodoViewModel.cs           # ToDoリスト全体の管理
├── Views/                         # サブ画面
│   └── TemplateManagerWindow.xaml # テンプレート管理画面
├── Services/                      # ビジネスロジック・データアクセス層
│   └── DatabaseService.cs         # SQLiteの全CRUD操作・マイグレーション（シングルトン）
├── Converters/                    # UIバインディング変換処理
│   └── ValueConverters.cs         # BoolToVisibility 等
└── Themes/                        # スタイルリソース
    └── DarkTheme.xaml             # VS2022準拠のダークテーマスタイル定義
```

---

## ⚙️ 5. 詳細な機能説明

### データベース設計 (SQLite)
4つの主要テーブルで構成されています。
1. **DailyEntries**:
   - `Id`, `EntryDate`, `ItemName`, `Count`, `IsCountable`, `Note`, `SortOrder`
   - 件数管理の有無を `IsCountable` でフラグ管理。集計時は `Count` を SUM します。
2. **TemplateGroups**:
   - `Id`, `GroupName`, `SortOrder`, `IsDefault`
   - `IsDefault=1` のグループが、起動時の自動生成対象となります。
3. **TemplateItems**:
   - `Id`, `GroupId`, `ItemName`, `DefaultCount`, `IsCountable`, `SortOrder`
   - 外部キーで `TemplateGroups` に紐づきます。
4. **TodoTasks**:
   - `Id`, `TaskDate`, `TaskName`, `IsCompleted`, `SortOrder`, `CreatedAt`, `StartTime`, `EndTime`, `Description`
   - 日報とは完全に独立しており、別枠で管理されます。
   - `StartTime` / `EndTime` / `Description` は後方互換性のために `ApplyTodoMigration` で動的に追加され、既存DBへのマイグレーションを自動実行します。

### リアルタイム保存機構
`DailyEntryViewModel` 内の各プロパティ（例：`Count` や `Note`）に `[ObservableProperty]` 属性を付与。
プロパティの `OnChange` フック内で `AutoSave()` メソッドを呼び出し、即座に `DatabaseService` を経由して UPDATE 文を発行します。これによりユーザーが「保存し忘れる」リスクがゼロになります。

---

## 🔀 6. 分岐による動作の違い（条件表）

| 条件・アクション | 動作結果 |
|---|---|
| **アプリ起動時（当日の日報が0件の場合）** | `IsDefault=1` のテンプレートグループから項目をコピーし、DBに新規作成して画面に表示する。 |
| **アプリ起動時（当日の日報が1件以上ある場合）** | テンプレートの自動展開は行わず、既存のDBデータのみを読み込んで表示する。 |
| **アプリ起動時のToDoリスト** | 前日のToDoリストのうち `IsCompleted=0` (未完了) のものを検索し、当日の日付でDBに新規作成（引き継ぎ）する。既に同名タスクがあれば重複作成しない。 |
| **日報行の IsCountable=True** | 項目名の横に ➖ 数値 ➕ の操作UIが表示され、数値の増減が可能。 |
| **日報行の IsCountable=False** | ➖ 数値 ➕ UIが非表示になり、代わりに補足メモ（テキストボックス）が広く表示される。（突発メモ用） |
| **カレンダー日付セルの HasEntries** | 指定した日付に1件でも `DailyEntries` が存在する場合、青い「●」マーカーが表示される。 |

---

## 🗑 7. マイグレーション・データクリーンアップ

`DatabaseService` 内には、スキーマ変更やデータ修正のためのマイグレーション用staticメソッドが存在します。

| メソッド | 役割 | 削除可能か |
|---|---|---|
| `ApplyTodoMigration` | `TodoTasks` テーブルに `StartTime` / `EndTime` / `Description` カラムが存在しない場合にALTER TABLEで追加する。 | 全ての環境でカラム追加が完了したら不要。 |
| `FixCorruptedStartTime` | 過去のバグで `StartTime` に日時形式（`2026-06-11 15:31:25` 等）が入ってしまったデータを空文字にクリアする。 | **削除可能**。初回適用以降は該当データが存在しないため、今後のリファクタリングで削除を検討してよい。 |

> **備考**: `FixCorruptedStartTime` のようなデータ修復は、`SanitizeTimeValue` が読み取り時に弾くため、画面上および翌日の引き継ぎ時には実害がありません。保険的な処置として残しています。
