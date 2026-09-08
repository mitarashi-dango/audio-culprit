# Microsoft Store 無料公開

## 現在の準備

MSIX用マニフェスト、Store専用ビルド、掲載原稿、プライバシーポリシーを用意。
Izu（PC Mode Switcherと同じ開発者アカウント）でAudioCulpritを2026-09-08に審査提出済み。Partner Centerで `In certification`、Submission完了／Pre-processing進行中を確認。審査合格後に自動公開する設定。Store IDは `9PJN1M9DRVBR`。正式な識別情報は `packaging/store/identity.json` に保存済み。公開完了はまだ確認していない。
Windows 11 x64を対象とし、.NETランタイムを同梱する。
Store版ではGitHub更新確認と従来のRunレジストリによる自動起動設定を除外する。自動起動は初回Store版では未対応。

2026-09-08: Store専用ビルド、正式識別情報によるMSIX生成とMakeAppx検証に成功。PCModeSwitcherの `.packages/microsoft.windows.sdk.buildtools` にある公式SDKツールを使用。自動テスト74件成功。専用ビルドの日本語・英語で起動、監視停止・再開を確認。署名後のMSIXインストールとWindows App Certification Kitは未実施。

プライバシーポリシー公開先: https://github.com/mitarashi-dango/audio-culprit/blob/master/docs/PRIVACY.md
Partner Center: https://partner.microsoft.com/en-us/dashboard/products/9PJN1M9DRVBR/overview
提出下書きID: `1152921505701837023`

## 登録から提出まで

1. PCModeSwitcherで利用している既存の開発者アカウントでPartner Centerにサインインする。AudioCulprit用に別アカウントを登録する必要はない。既存アカウントに未完了の確認がある場合のみ完了する。
2. Partner Centerで新しいMSIXアプリを作成し、名前を予約する。
3. 「製品 ID」から Package/Identity/Name、Package/Identity/Publisher、Package/Properties/PublisherDisplayName を取得する。
4. Windows SDKのMSIX Packaging Toolsを用意して、以下を実行する。値はPartner Centerの表示をそのまま使う。

```powershell
./publish-store.ps1 # identity.jsonの正式な識別情報を使用
# このPCの既存SDKツールを使用する場合
./publish-store.ps1 -MakeAppxPath 'E:/AI_Playground/PMS/PCModeSwitcher/.packages/microsoft.windows.sdk.buildtools/10.0.26100.8249/bin/10.0.26100.0/x64/makeappx.exe'
# SDKなしで専用ビルドとpayloadのみ確認
./publish-store.ps1 -PrepareOnly
```

5. 生成MSIXをテスト環境で開発用署名してインストール検証する（テスト証明書の信頼追加はこのスクリプトでは行わない）。音声検出、履歴、ミュート、ホットキー、トレイ終了、再起動、更新・アンインストール、ZIP版との併用を確認する。Windows App Certification Kitも実行する。
6. docs/PRIVACY.mdを公開し、アクセス可能なURLを取得する。docs/STORE-LISTING.mdの原稿とStore版の実スクリーンショットを用意する。
7. Partner Centerで価格を「無料」、アプリ内購入なしに設定する。配信地域、年齢区分質問票、製品プロパティを実態に合わせて入力する。
8. MSIXと掲載情報を登録し、runFullTrustの必要理由を審査メモに記載する。内容を確認して審査へ提出する。

Store提出用MSIXの有料証明書は不要。Storeが署名する。未署名MSIXは通常のダブルクリックではインストールできない。
バージョンはcsprojの3桁に `.0` を付与する（末尾はStore用に0）。更新時はプロジェクトのバージョンを上げる。
MSIXではローカルデータにリダイレクトが適用される場合がある。ZIP版の履歴引き継ぎやアンインストール後の保持は保証せず、実機で確認する。

## 公式資料

- https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account
- https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion
- https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/create-app-submission
- https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images
- https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview
