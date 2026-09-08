# Microsoft Store 掲載原稿

価格: 無料。アプリ内購入・広告なし。カテゴリ候補: ユーティリティ & ツール。
対応: Windows 11 / x64。表示言語: 日本語、英語。
表示名は予約できた名称に合わせる。

## 日本語

短い説明: PCの謎の音、どのアプリが鳴らした？ 音の発生元候補を履歴から確認。

AudioCulpritは、PCで音を出したアプリの候補を探す無料のWindows用ツールです。
起動中に音声出力を監視し、時刻・アプリ名・再生時間・出力デバイスなどを記録します。
気になる音が鳴ったらCtrl + Alt + Aで最近の候補を表示。短い音や現在再生中のアプリも確認できます。

音声そのものを録音せず、マイクにもアクセスしません。履歴はPC内に保存し、7日間・最大10,000件保持。設定から削除できます。アカウント登録は不要です。

ブラウザのタブや通知内容、Windowsのシステム音の原因までは特定できない場合があります。起動前の音、ごく短い音や小さい音、一部の排他モードの音は検出できません。
ウィンドウを閉じるとトレイで監視を続けます。完全に終了するにはトレイメニューの「終了」を選んでください。
Store版の更新はMicrosoft Storeから配信されます。この版はWindowsログイン時の自動起動に対応していません。

## English

Short description: Find which app made that mysterious sound on your PC.

AudioCulprit is a free Windows utility that helps identify apps that may have made a sound. While running, it monitors playback sessions and keeps a local history of app names, times, durations and output devices. Press Ctrl + Alt + A to see recent candidates. Filter short sounds, view active playback, and mute an app's current audio sessions.

It does not record audio or access your microphone. History stays on your PC, is retained for up to 7 days and 10,000 entries, and can be deleted in settings. No account is required.

It cannot identify individual browser tabs or notification contents. The underlying cause of Windows system sounds may remain unknown. Sounds before launch, very short or quiet sounds, and some exclusive-mode audio may not be detected. Closing the window keeps monitoring in the system tray; choose Exit from the tray menu to quit.

Updates are delivered through Microsoft Store. Automatic launch at Windows login is not available in this edition.

## Certification notes

This is a full-trust .NET WPF desktop utility. The runFullTrust capability is required to enumerate Windows Core Audio playback sessions, resolve process information, register a global hotkey and provide a system tray icon. It does not require elevation, record audio, or access the microphone. No account, payment or external hardware is required. Launch the app, play audio in another application, then inspect the playback/history list. Closing the main window keeps the tray process running; use the tray Exit command to terminate it. This Store build disables GitHub update checks and registry-based startup registration.

サポートURL: https://github.com/mitarashi-dango/audio-culprit/issues
プライバシーURL: https://github.com/mitarashi-dango/audio-culprit/blob/master/docs/PRIVACY.md
スクリーンショット: Store版の実画面を1366×768以上のPNGで最低1枚。ユーザー名や実パスを含めない。
