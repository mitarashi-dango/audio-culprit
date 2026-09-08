# AudioCulprit v0.2 実装仕様書

**対象:** Windows 10 / Windows 11  
**開発言語:** C#  
**フレームワーク:** .NET 10 / WPF  
**アプリ名:** AudioCulprit  
**日本語コンセプト:** 音の犯人  
**想定リポジトリ名:** `audio-culprit`

---

# 1. アプリ概要

AudioCulpritは、

**「今の音、誰が鳴らした？」**

を後から即座に確認するためのWindows常駐型ユーティリティである。

単なる音声履歴ビューアではない。

目的は、

**謎の通知音・突然の効果音・バックグラウンドアプリの音について、犯人となるアプリを素早く特定すること**

である。

ユーザーが知りたい最重要情報は、

> さっき音を鳴らしたのは Discord.exe  
> 8秒前

という答えである。

---

# 2. 解決する問題

Windowsでは突然、

- ピコン
- ポン
- ブッ
- エラー音
- 通知音
- USB接続音
- アプリ独自の効果音

などが鳴っても、その瞬間に音量ミキサーを見ていなければ発生元を特定しづらい。

AudioCulpritは普段からWindowsの音声活動を監視し、

- どのアプリが
- いつ
- どれくらいの長さ
- どれくらいの強さで
- どの出力デバイスから

音を出したか記録する。

ただし音声そのものは録音しない。

---

# 3. 製品コンセプト

AudioCulpritの立ち位置は、

**Audio History**

ではない。

**Mystery Sound Investigator**

とする。

つまり、

「過去の音を一覧する」

よりも、

**「今の音の犯人は誰？」**

を最優先にする。

---

# 4. MVP完成目標

以下を満たした時点でMVP完成とする。

1. タスクトレイへ常駐できる
2. Windowsのオーディオセッションを監視できる
3. 実際に音を出したアプリを検出できる
4. 音声開始時刻を記録できる
5. 音声終了時刻を記録できる
6. 再生時間を計算できる
7. 最大ピーク値を記録できる
8. アプリ名を取得できる
9. 実行ファイルパスを可能な範囲で取得できる
10. 出力デバイス名を保存できる
11. 履歴をSQLiteへ保存できる
12. 最後に音を出したアプリを大きく表示できる
13. 長時間再生中のアプリと新規通知音を区別できる
14. 特定アプリを「無視」できる
15. 履歴を削除できる
16. 音声そのものを保存しない
17. 外部通信なしで動作する
18. 管理者権限なしで通常利用できる

---

# 5. 最重要機能

AudioCulprit起動後、メイン画面最上部に、

## 今の音？

を表示する。

その下に、

```text
Discord

8秒前

17:42:13

再生時間 0.38秒
```

のように表示する。

取得可能ならアプリのアイコンも表示する。

ユーザーが一覧表を探さなくても、

**開いた瞬間に答えが見える**

ことを最優先とする。

---

# 6. メイン画面

メイン画面は3領域とする。

## A. 今の音？

最後に「新しく音を出した」と判定されたアプリ。

## B. 怪しい音

最近発生した短時間の音声イベント。

## C. 音声履歴

すべての記録。

---

# 7. 「今の音？」カード

表示項目：

- アプリアイコン
- アプリ名
- 何秒前か
- 発生時刻
- 再生時間
- 最大ピーク
- 出力デバイス

例：

```text
今の音？

Discord.exe

3秒前

17:52:31
0.32秒
Peak 34%
Speakers
```

---

# 8. Mystery Sound Mode

AudioCulprit独自の重要機能とする。

Spotify、YouTube、ゲームなど、

**長時間ずっと音を出しているアプリ**

と、

Discord通知など、

**突然新しく音を出したアプリ**

を区別する。

例：

```text
Spotify
42分間継続中

Discord
0.31秒
3秒前
```

この場合、

Discordを、

**怪しい音**

として強調表示する。

---

# 9. 怪しい音判定

初期ルール：

以下を満たす音声イベントを優先表示する。

- 新規に音声を開始した
- 再生時間が10秒未満
- 直前まで無音だった
- 除外アプリではない

優先度を内部的に、

```text
SuspicionScore
```

として保持してもよい。

MVPでは高度なスコアリングは不要。

---

# 10. 長時間再生判定

30秒以上連続して音声を出しているアプリは、

**継続再生**

扱いとする。

例：

- Spotify
- VLC
- YouTube
- ゲーム
- Discord通話

これらは履歴には残すが、

「今の音？」

候補としては優先度を下げる。

---

# 11. 除外アプリ

ユーザーはアプリ単位で、

**今後、謎の音候補から除外**

できる。

例：

Spotifyを除外した場合、

Spotifyの音声活動は履歴に残してもよいが、

「今の音？」

には表示しない。

設定例：

```text
除外アプリ

Spotify.exe
foobar2000.exe
vlc.exe
```

---

# 12. 除外方法

履歴または詳細画面に、

**このアプリを無視**

ボタンを設置する。

押した場合、

ProcessNameまたはProcessPathを除外リストへ追加する。

可能ならProcessPathを優先する。

---

# 13. 音声履歴一覧

例：

| 時刻 | アプリ | 長さ | Peak | 出力 |
|---|---|---:|---:|---|
| 17:53:02 | Chrome | 4.2秒 | 18% | Speakers |
| 17:48:51 | System Sounds | 0.2秒 | 71% | Speakers |
| 17:42:13 | Discord | 0.4秒 | 32% | Speakers |
| 17:31:09 | Spotify | 18分 | 46% | Headphones |

新しいものを上に表示する。

---

# 14. 詳細画面

履歴クリック時に表示する。

項目：

- アプリ名
- Process Name
- Process Path
- PID
- Session ID
- 開始時刻
- 終了時刻
- 再生時間
- 最大Peak
- Session Volume
- Mute状態
- 出力デバイス
- Event Type

---

# 15. 詳細画面の操作

以下のボタンを設置する。

- ファイルの場所を開く
- このアプリを無視
- このアプリをミュート
- Windows音量ミキサーを開く

ただし、

**アプリ単位ミュートが安定して実装できない場合はMVP後回しでもよい。**

---

# 16. グローバルホットキー

将来的に重要機能とする。

例：

```text
Ctrl + Alt + ?
```

または設定可能なホットキー。

押した瞬間、

小型ポップアップを表示する。

例：

```text
今の音？

Discord.exe

2.4秒前
0.31秒

[詳細]
[無視]
```

MVPでは後回しでもよい。

---

# 17. 音声監視方式

Windows Core Audio APIを使用する。

主に：

```text
MMDeviceEnumerator
Audio Session Manager
IAudioSessionControl
IAudioSessionControl2
IAudioMeterInformation
```

C#では原則、

```text
NAudio
```

を利用してよい。

---

# 18. セッション発見

アプリ起動時に、

既存オーディオセッションを列挙する。

その後、

新規オーディオセッションも監視する。

取得可能な情報：

```text
SessionId
ProcessId
ProcessName
ProcessPath
DisplayName
DeviceId
DeviceName
CurrentVolume
Mute
Peak
```

---

# 19. プロセス情報のキャッシュ

音を出した直後にプロセスが終了する可能性がある。

そのため、

セッション発見時または初回音声検出時に、

```text
PID
ProcessName
ProcessPath
Icon
```

をメモリへキャッシュする。

---

# 20. 音声開始判定

オーディオセッションが存在するだけでは、

「音を出した」

とは判定しない。

ピーク値を利用する。

初期値：

```text
StartThreshold = 0.01
```

Peakがこれ以上になった場合、

音声開始候補とする。

---

# 21. 音声終了判定

初期値：

```text
EndThreshold = 0.005
SilenceHoldMs = 250
```

PeakがEndThreshold未満になり、

250ms以上続いた場合に、

音声終了とする。

---

# 22. 監視周期

初期値：

```text
PollIntervalMs = 50
```

50msごとにPeakを取得する。

ただしPeak値を毎回DBへ保存してはいけない。

メモリ上で処理する。

---

# 23. イベント統合

同じアプリが、

```text
ピッ
100ms
ピッ
```

のように鳴った場合、

細かすぎる履歴にならないよう統合する。

初期値：

```text
MergeGapMs = 500
```

500ms以内の同一セッション音声は、

同一イベントとして結合する。

---

# 24. 同時再生

複数アプリが同時に音を出した場合、

すべて記録する。

例：

```text
17:42:13.100 Spotify.exe
17:42:13.240 Discord.exe
```

Discordだけに絞ってはいけない。

Mystery Sound Mode上で優先度を変えるだけとする。

---

# 25. 長時間再生

Spotify等について、

50msごとに履歴を生成してはいけない。

開始から終了まで、

1イベントとして扱う。

例：

```text
Spotify.exe

Start
18:00:00

End
18:35:42

Duration
35m42s
```

---

# 26. System Sounds

通常のアプリへ紐付けられないWindowsシステム音は、

```text
System Sounds
```

として記録する。

具体的なプロセスが分からない場合、

無理に推測してはいけない。

---

# 27. Unknown

プロセス情報を取得できなかった場合は、

```text
Unknown
```

として残す。

履歴を破棄しない。

可能なら、

- PID
- Session ID
- Device
- 時刻
- Peak

を保存する。

---

# 28. 出力デバイス

以下のような複数出力デバイスへ対応可能な構造にする。

```text
Speakers
Headphones
USB DAC
HDMI
Bluetooth Audio
```

MVP初期実装では既定デバイスのみでもよい。

ただしアーキテクチャ上は、

**複数デバイス監視可能**

な構造とする。

---

# 29. デバイス切替

ユーザーが、

```text
Speakers
↓
Headphones
```

へ変更した場合も、

AudioCulprit再起動なしで追従する。

デフォルトデバイス変更通知を受け、

監視対象を再構築する。

---

# 30. タスクトレイ

AudioCulprit起動後、

タスクトレイへ常駐する。

左クリック：

```text
AudioCulpritを開く
```

右クリック：

```text
開く
監視ON/OFF
設定
終了
```

---

# 31. 通知音禁止

AudioCulprit自身は、

通常のWindows通知音を鳴らさない。

理由：

AudioCulprit自身が、

**謎の音の犯人になる**

のを防ぐため。

---

# 32. 自動起動

設定：

```text
Windowsログイン時にAudioCulpritを起動
```

初期値：

```text
OFF
```

ユーザーが明示的にONにする。

---

# 33. データ保存

SQLiteを使用する。

推奨：

```text
Microsoft.Data.Sqlite
```

保存先：

```text
%LocalAppData%\AudioCulprit\audio-culprit.db
```

---

# 34. AudioEvent

保存項目：

```text
Id
SessionId
ProcessId
ProcessName
ProcessPath
DisplayName
DeviceId
DeviceName
StartTimeUtc
EndTimeUtc
DurationMs
MaxPeak
SessionVolume
WasMuted
EventType
SuspicionScore
```

---

# 35. EventType

```text
Application
SystemSounds
Unknown
```

必要なら将来的に追加可能。

---

# 36. IgnoreRule

除外アプリ保存用。

```text
Id
ProcessName
ProcessPath
CreatedAtUtc
```

ProcessPath取得可能な場合は、

Pathを優先する。

---

# 37. 履歴保持

初期値：

```text
7日間
```

または、

```text
最大10,000件
```

古いものから自動削除する。

---

# 38. 履歴削除

設定または履歴画面に、

```text
すべての履歴を削除
```

を用意する。

確認ダイアログ必須。

---

# 39. プライバシー

以下は絶対に保存しない。

```text
音声データ
PCM
録音
マイク音声
会話内容
音楽
動画内容
ブラウザ内容
```

保存するのは、

**音声活動のメタデータのみ**

とする。

---

# 40. マイク

マイク入力は監視しない。

AudioCulpritは、

**Windowsの再生音**

だけを扱う。

---

# 41. インターネット通信

MVPでは一切不要。

```text
クラウドなし
広告なし
AIなし
APIキーなし
テレメトリなし
ユーザー登録なし
```

完全ローカル動作。

---

# 42. 管理者権限

通常利用ではAdministrator権限を要求しない。

取得できないプロセス情報については、

Unknownまたは取得済み情報のみ表示する。

---

# 43. UI方針

Process Monitorのような、

技術者向けの大量ログ画面にはしない。

優先順位は、

```text
1. 誰が鳴らした？
2. 何秒前？
3. どれくらい鳴った？
4. どこから鳴った？
```

とする。

PIDやSession IDは詳細画面に隠す。

---

# 44. UI例

```text
AudioCulprit

今の音？

[Discord Icon]

Discord

3秒前
17:52:31

0.31秒
Peak 34%
Speakers


怪しい音

17:52 Discord       0.31秒
17:48 System Sounds 0.18秒
17:41 Chrome        1.20秒


再生中

Spotify
42分12秒
```

---

# 45. 再生中一覧

現在継続して音声を出しているアプリを、

履歴とは別に表示してもよい。

例：

```text
現在再生中

Spotify
42分

Chrome
3分
```

Mystery Sound候補とは視覚的に分離する。

---

# 46. アーキテクチャ

```text
AudioCulprit
│
├─ Audio
│   ├─ AudioMonitorService
│   ├─ AudioDeviceMonitor
│   ├─ AudioSessionMonitor
│   ├─ AudioActivityDetector
│   └─ SuspicionEvaluator
│
├─ Processes
│   └─ ProcessMetadataResolver
│
├─ History
│   ├─ AudioEvent
│   ├─ IgnoreRule
│   └─ HistoryRepository
│
├─ Services
│   ├─ SettingsService
│   ├─ StartupService
│   └─ HotkeyService
│
├─ UI
│   ├─ MainWindow
│   ├─ EventDetailsWindow
│   ├─ SettingsWindow
│   └─ TrayService
│
└─ App.xaml
```

過度なClean Architecture化は禁止。

個人開発として、

**読みやすく直しやすい構造**

を優先する。

---

# 47. 推奨ライブラリ

```text
NAudio
Microsoft.Data.Sqlite
```

その他は必要になった時点で追加。

不必要に大量のNuGetパッケージを導入しない。

---

# 48. パフォーマンス目標

常駐アプリなので軽量性を重視する。

目標：

```text
アイドルCPU平均 1%未満
メモリ 100MB未満
```

50ms監視だからといって、

高頻度のDB書き込みをしてはいけない。

---

# 49. テスト1：Discord

Discord通知音を鳴らす。

期待：

```text
Discord.exe

0.x秒

Mystery Sound候補
```

---

# 50. テスト2：Spotify再生中＋Discord

Spotifyを音楽再生中に、

Discord通知音を鳴らす。

期待：

```text
Spotify
継続再生

Discord
新規短時間音
```

Discordを、

「今の音？」

として表示する。

このテストは非常に重要。

---

# 51. テスト3：Chrome

YouTubeを再生。

期待：

```text
chrome.exe
```

として取得される。

---

# 52. テスト4：System Sounds

Windows標準システム音。

取得可能なら、

```text
System Sounds
```

と表示。

---

# 53. テスト5：同時再生

SpotifyとChromeとDiscord。

すべて記録。

Mystery Sound判定は、

新しく鳴ったアプリを優先。

---

# 54. テスト6：除外

Spotifyを、

```text
このアプリを無視
```

へ登録。

以後、

Mystery Sound候補へ表示されない。

---

# 55. テスト7：短い音

0.2秒程度の音。

履歴へ残ること。

---

# 56. テスト8：長時間再生

10分以上再生。

細切れイベントにならないこと。

---

# 57. テスト9：プロセス終了

短い音を出して即終了するアプリ。

終了後も、

ProcessName等が履歴に残る。

---

# 58. テスト10：デバイス変更

SpeakersからHeadphonesへ変更。

再起動せず追従。

---

# 59. 技術的限界

AudioCulpritが特定するのは、

基本的に、

**音を出したWindowsオーディオセッション / プロセス**

である。

例：

```text
Discord.exe
```

までは特定できても、

```text
DiscordのDM通知音
```

まで必ず特定できるとは限らない。

UI上でも誤解させない。

---

# 60. 「犯人」の意味

AudioCulpritでいう犯人とは、

**音声を出力していた可能性が高いプロセス**

を意味する。

OS内部の事情で完全に特定できない場合、

断定せず、

```text
Unknown
System Sounds
```

として扱う。

---

# 61. 将来機能

MVP完成後に検討。

## グローバルホットキー

音が鳴った直後、

ショートカット一発で犯人表示。

## アプリミュート

犯人特定後、

その場でMute。

## Notification Sound Only

短い音だけ表示。

## 時間帯フィルター

例：

```text
過去30秒
過去1分
過去5分
```

## プロセス起動履歴

音の直前に起動したアプリを表示。

## USBイベント

USB接続音などの状況証拠。

## ウィンドウイベント

突然表示されたウィンドウと音を関連付ける。

## What Just Happened

最終的に、

**直前30秒にWindowsで起きた出来事を記録するPCドラレコ**

へ拡張可能。

---

# 62. MVPでやらないこと

以下は初期版では実装しない。

```text
音声録音
AI音声解析
音源ファイル特定
クラウド同期
アカウント
広告
スマホ版
Mac版
Linux版
USB診断
フォーカス履歴
画面点灯原因診断
完全なWindowsイベント解析
```

---

# 63. Codex実装順

## Phase 1

最重要技術検証。

簡易WPFまたはコンソールで、

```text
時刻
PID
ProcessName
Peak
```

をリアルタイム表示する。

ここで、

**本当に音を出したアプリを取得できるか**

確認する。

このPhaseが通るまでは、

本格UIを作らない。

---

## Phase 2

音声開始・終了判定。

例：

```text
Discord.exe

Start
17:42:13.120

End
17:42:13.490

Duration
370ms

Peak
0.34
```

---

## Phase 3

複数アプリ同時監視。

Spotify再生中にDiscord通知が来ても、

両方取得する。

---

## Phase 4

Mystery Sound判定。

長時間音と、

新しく鳴った短時間音を区別する。

---

## Phase 5

SQLite履歴保存。

---

## Phase 6

メイン画面。

最上部に、

```text
今の音？
```

を実装。

---

## Phase 7

除外アプリ。

---

## Phase 8

タスクトレイ。

---

## Phase 9

デバイス切替。

---

## Phase 10

設定・自動起動・履歴削除。

---

# 64. 最重要テストシナリオ

AudioCulprit最大の存在価値を確認するテスト。

1. Spotifyで音楽を流す
2. 5分ほどそのまま再生する
3. Discord通知音を鳴らす
4. AudioCulpritを開く

期待結果：

```text
今の音？

Discord

2秒前

0.32秒
```

Spotifyは、

```text
現在再生中
```

として別扱い。

この動作が実現できれば、

AudioCulpritの基本コンセプトは成立している。

---

# 65. 完成条件

以下をすべて満たしたらMVP完成。

- 音を出したアプリを取得できる
- 短い通知音を取得できる
- 長時間音声を取得できる
- 複数アプリを区別できる
- 新規音声を強調できる
- 長時間音を背景扱いできる
- 最後に音を出したアプリを大きく表示できる
- 除外アプリが使える
- 履歴保存できる
- 再起動後も履歴が残る
- 出力デバイスを記録できる
- System Soundsを扱える
- Unknownを捨てない
- タスクトレイ常駐できる
- 音声を録音しない
- ネット通信不要
- 管理者権限不要
- 異常なCPU負荷がない
- 終了後にプロセスが残らない

---

# 66. 最重要方針

AudioCulpritは、

高度なオーディオ解析ソフトではない。

音声ミキサーでもない。

ログビューアでもない。

目的は一つ。

**「今の音、誰が鳴らした？」**

に対して、

**「Discord。3秒前。」**

と即答すること。

迷った場合、

新機能を増やすより、

この答えを速く・正確に出せる方を優先する。