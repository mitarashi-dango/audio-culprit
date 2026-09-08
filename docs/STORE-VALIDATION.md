# Store 0.2.0.0 検証記録

実施日: 2026-09-08

提出結果: Submission 1 (`1152921505701837023`) の審査提出が受理され、Partner Centerの `In certification` を確認。全必須項目Complete、MSIX Validated。審査合格後に自動公開。公開済みではない。

- 製品: AudioCulprit / Izu / Store ID `9PJN1M9DRVBR`
- パッケージ: `Izu.AudioCulprit` / x64 / Windows 11以降
- 提出物: `artifacts/store/9ec4c71aeb434f5aa55f29702e45fae4/AudioCulprit-0.2.0-x64.msix`
- SHA256: `6D1EFBED348EB3774006D8D6A9131C990074183C5E12F6F7E77E20880FCCF8CB`
- MakeAppx: 10.0.26100.8249、スキーマ検証を有効にして生成成功
- Partner Center: アップロード後の解析でValidatedを確認
- 無料（JPY 0、全240市場）、Utilities + tools、Windows Desktopのみで保存。OneDrive自動バックアップは無効。プライバシー情報はローカルの実行ファイルパス等を扱うため「個人情報を使用」に設定して公開ポリシーURLを登録
- 年齢区分: IARC 3+ / ESRB Everyoneなど。発行者の連絡先共有・規約同意・成年確認について所有者の承認を得て保存
- 自動テスト: 74件成功
- 実音声の結合テスト込み: 82件成功（プロセス識別、停止、ミュート／解除など）
- Store専用ビルド: 日英の起動・監視停止／再開・終了成功。各21秒の検証
- 掲載画像: `artifacts/store/screenshots/ja/main-window.png`、`artifacts/store/screenshots/en/main-window.png`。1366×900、実アプリのWPF描画。音声履歴が空の画面を使用し、個人のプロセス情報を含めない

Store専用ビルドの直接実行で検証した。署名済みMSIXのインストール・更新・アンインストール、Windows App Certification Kitは未実施。Partner CenterのValidatedは審査合格を意味しない。

通常実行中のZIP版は停止・置換していない。`--verify` は専用mutexと指定先のデータフォルダを使用する。
