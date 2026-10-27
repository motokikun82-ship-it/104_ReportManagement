# 実装計画: 集計画面の全項目選択機能の追加およびドキュメント最新化

集計ウィンドウにおいて、全グループを対象とした「全項目を選択」「全項目を解除」ボタンを追加し、グループ単位の選択ボタンと併用できるようにします。また、追加された機能群および今回の改修内容を仕様書・READMEに反映してドキュメントを最新化します。

---

## ユーザー要求の概要
1. **集計ウィンドウの選択改善**:
   - パターンBを採用：
     - 「全項目を選択」「全項目を解除」（全グループ・未分類含む全項目が対象）
     - 「グループ内全選択」「グループ内全解除」（現在選択中グループの項目が対象）
2. **Markdownドキュメントの最新化**:
   - `docs/README.md` および `docs/SPECIFICATION.md` に、集計機能、お気に入り項目、祝日設定、メモ帳機能、スケジュールテンプレ、テーマ設定機能などの最新仕様を反映。

---

## 変更対象ファイル
1. [AggregationViewModel.cs](file:///c:/Users/BEST/Desktop/morinaga/Script/104_ReportManagement/ViewModels/AggregationViewModel.cs)
   - `SelectAllItems()` / `DeselectAllItems()` コマンドの追加（全グループの項目を対象にチェック状態を変更し、キャッシュから再描画）
2. [AggregationWindow.xaml](file:///c:/Users/BEST/Desktop/morinaga/Script/104_ReportManagement/Views/AggregationWindow.xaml)
   - グループフィルタ領域またはコントロールバーに「全項目を選択」「全項目を解除」ボタンを配置し、既存のボタンは「グループ内全選択」「グループ内全解除」として明示
3. [SPECIFICATION.md](file:///c:/Users/BEST/Desktop/morinaga/Script/104_ReportManagement/docs/SPECIFICATION.md)
   - 全機能一覧、画面構成、DB構造、最新の仕様を網羅して更新
4. [README.md](file:///c:/Users/BEST/Desktop/morinaga/Script/104_ReportManagement/docs/README.md)
   - 目的・特徴、起動方法、各画面・機能の詳細操作手順を網羅して更新

---

## 検証計画
- `dotnet build ReportManagement.csproj` によるビルド成功の確認
- UIレイアウトの整合性、コマンド動作ロジックの確認
- ドキュメントの記載内容の確認
