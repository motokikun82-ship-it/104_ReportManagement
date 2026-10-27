# タスクリスト: 集計画面の全項目選択機能の追加およびドキュメント最新化

- [/] 集計画面（AggregationWindow）の全選択/全解除ボタン改修 <!-- id: 0 -->
  - [x] AggregationViewModel に全項目を対象とする `SelectAllItemsCommand` / `DeselectAllItemsCommand` を実装 <!-- id: 1 -->
  - [x] AggregationWindow.xaml に「全項目を選択」「全項目を解除」ボタンを配置し、グループ内ボタンと区別 <!-- id: 2 -->
- [ ] ビルド検証 <!-- id: 3 -->
  - [ ] `dotnet build ReportManagement.csproj` のビルド確認 <!-- id: 4 -->
- [ ] ドキュメント更新 <!-- id: 5 -->
  - [ ] `docs/SPECIFICATION.md` の更新（新機能・画面・テーブル・集計選択仕様の追記） <!-- id: 6 -->
  - [ ] `docs/README.md` の更新（最新の全機能と使い方ガイドの追記） <!-- id: 7 -->
- [ ] 完了確認・ウォークスルー作成 <!-- id: 8 -->
  - [ ] `walkthrough.md` の作成・アーティファクト化 <!-- id: 9 -->
