# AudioCulprit v0.2 全体レビュー

## 修正状況（2026-09-08）

以下の5件は修正済みです。下のレビュー本文は修正前の記録です（行番号も修正前）。

- 保存を専用の HistoryWriter スレッドへ移し、終了時にキューを処理しきってからDBを閉じます。一時的な書き込み失敗は最大3回試行し、失敗を画面へ通知します。履歴削除は先行する書き込みの後に実行します。
- デバイスを通知購読成功後に登録し、初期化失敗時は破棄して次回探索で再試行します。セッションのCOMエラーでもデバイスを作り直し、古いデバイスから届いた通知は採用しません。物理機器の障害・抜き差し試験は未実施です。
- 最後に音があった時刻から30秒以内の候補を対象にし、その中で継続中の長い再生を後回しにします。終了済みの長い音は古い短音より優先されます。
- 小さいウィンドウでは画面全体を縦スクロールして履歴を操作できます。
- 履歴を差分更新して並べ替えを保持します。時刻・長さ・Peakは表示文字列ではなく実値でソートします。

検証: 22チェック成功。実音の34秒連続再生、停止中0件・再開後1件、保存をブロック中の短音2回が両方保存されることを確認しました。UI検証では候補の新しさ、ソート保持、新規イベント・Peak更新後のソート、760×650での履歴表示を確認。更新した単体EXEでも起動・停止・再開・終了を確認しました。

結果: `artifacts/review/tests-fixed-results.txt`、`monitor-fixed-results.txt`、`ui-fixed-results.txt`。Windows 10と物理デバイス切替は引き続き未検証です。

---

実施日: 2026-09-08。Windows 11 / x64。対象は本体、監視コア、保存処理、既存テスト、配布スクリプト、および現在の単体 `artifacts/AudioCulprit.exe`。

基本経路は動作していますが、修正すべき問題が5件あります。4件は追加検証で再現し、1件はコードと依存ライブラリの実装から確認しました。本レビューでは製品ソースと配布EXEを変更していません。検証用コード・結果は `artifacts/review` に分離しました。

P2 は次の修正対象を表します。日常の利用条件により検出漏れや操作不能を引き起こす問題であり、スタイルの好みや将来機能は含めていません。

## 1. [P2] 履歴の保存待ちで音の監視自体が止まる

該当: [App.xaml.cs:68](E:/AI_Playground/audio-culprit/src/AudioCulprit/App.xaml.cs:68)、[AudioMonitorService.cs:159](E:/AI_Playground/audio-culprit/src/AudioCulprit.Core/AudioMonitorService.cs:159)。

監視スレッドが `Completed` を同期呼び出しし、購読先の `Save` がその場でSQLiteへ書き込みます。履歴削除・整理とのロック競合やディスクI/Oが遅れたとき、同じスレッドの50ms周期のピーク取得も止まります。その間に終了する通知音は、あとから取り戻せません。

追加検証では、専用DBの保存ロックを保持して最初の音の保存待ちを作り、その間に別の0.25秒の音を鳴らしました。十分な無音間隔を空けた2回の音に対し、記録は1件でした。これは保存遅延を意図的に模擬した結果で、通常時に毎回発生するという意味ではありません。

修正方針: 監視スレッドからは完了イベントをキューへ渡し、専用の保存処理で書き込む。終了時にはキューを保存し終えてからDBを閉じる。失敗時の再試行と、保存できていない状態の表示も同じ経路で扱う。

## 2. [P2] デバイスの初期化失敗後に新規セッション通知を購読し直せない

該当: [AudioMonitorService.cs:75](E:/AI_Playground/audio-culprit/src/AudioCulprit.Core/AudioMonitorService.cs:75)。

`devices.Add(id, d)` を実行した後に `endpoint.AudioSessionManager.OnSessionCreated` を購読しています。このプロパティ取得が一時的に失敗し、同じデバイスIDがActiveのまま復帰すると、次回の列挙では登録済み扱いになるため購読の行へ戻りません。結果として、初期化後に起動したアプリの音を見落とす可能性があります。

2秒周期の処理も、同じ `AudioSessionManager.Sessions` を読み直しているだけです。NAudio 2.2.1ではこのプロパティは保持中の列挙オブジェクトを返し、再生成は `RefreshSessions()` が担当します。[NAudioの実装](https://raw.githubusercontent.com/naudio/NAudio/v2.2.1/NAudio.Wasapi/CoreAudioApi/AudioSessionManager.cs)

Windowsも、列挙オブジェクトだけでは新規セッションが欠ける場合があると説明しています。したがって、定期列挙だけで通知購読の欠落を補えるとは判断できません。[Microsoftの仕様](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionmanager2-getsessionenumerator)

修正方針: 初期化と通知購読が成功してから辞書へ登録する。失敗時は途中の状態を破棄し、次回にデバイス管理オブジェクトを作り直す。デバイスごとに例外を処理して、1台の失敗で他のデバイスの監視まで飛ばさないようにする。

これはコード経路の指摘です。物理デバイスの抜き差しやWindows Audioサービスの変更による実機再現は行っていません。

## 3. [P2] 昨日の短音が、たった今の音より優先される

該当: [AudioEvent.cs:65](E:/AI_Playground/audio-culprit/src/AudioCulprit.Core/AudioEvent.cs:65)。

候補選択は「30秒未満か」を最優先し、その後に開始時刻を比較しています。全保存履歴を入力するため、昨日の0.2秒の音と、たった今終了した40秒の音がある場合、「今の音？」には昨日のアプリが出ます。追加検証でも `yesterday.exe` が選ばれました。新しい音が30秒に達した瞬間に、古い履歴へ表示が戻ることもあります。

継続中の音楽より直近の通知を優先する意図は妥当ですが、古い短音を無期限に優先すると「最後に鳴った発生元」を示せません。

修正方針: 直近の時間範囲内で短音を優先し、該当がなければ最新の音へフォールバックする。継続再生と、すでに終了した長めの音も区別する。

## 4. [P2] 許可されている最小サイズで履歴一覧が消える

該当: [MainWindow.xaml:1](E:/AI_Playground/audio-culprit/src/AudioCulprit/MainWindow.xaml:1)、同ファイルのGrid行構成。

最小ウィンドウサイズは760×650ですが、上部のカードと固定高の一覧だけで利用可能な高さを使い切ります。実際のWPFウィンドウをこのサイズに変更したところ、履歴DataGridの `ActualHeight` は **0** でした。外側にもスクロールがなく、履歴を選択する操作ができません。

証拠: [最小サイズの描画](E:/AI_Playground/audio-culprit/artifacts/review/minimum-window.png)。

修正方針: 小さい画面では上部の領域を縮めるか全体をスクロール可能にし、履歴の表示領域を確保する。最小高さだけを引き上げると小さい画面で収まらなくなるため、レイアウト側で対応する。

## 5. [P2] 履歴の並べ替えが毎秒解除される

該当: [MainWindow.xaml.cs:80](E:/AI_Playground/audio-culprit/src/AudioCulprit/MainWindow.xaml.cs:80)。

1秒ごとの更新で `History.ItemsSource` を新しい配列へ差し替えています。このため、列見出しで指定した並べ替えが更新のたびに消えます。追加検証ではDataGridの実際のソート処理を呼び、`SortDescriptions.Count` が更新前の1から更新後の0へ変化することを確認しました。Peak順やアプリ順で調べ続けることができません。

修正方針: コレクションとビューを維持して差分更新するか、差し替え時にソート条件を復元する。選択行だけでなく、閲覧位置も保つ。

## 実施した確認

- Releaseのソリューションビルド: 警告0、エラー0。
- 既存の実音を含む結合テスト: 17項目すべて成功。
- 実際の34秒の連続再生: 32秒時点で約31.8秒の「継続再生」、途中の完了イベント0件、終了後に1件。
- 停止中に音を鳴らす: 追加記録0件。再開後に音を鳴らす: 追加記録1件。
- 保存待ちの模擬: 2回の短音に対して記録1件となり、取りこぼしを再現。
- 古い候補の選択、最小サイズの履歴消失、ソート解除: 本番のクラスを使って再現。
- 現在の単体EXE: 専用DBで起動し、監視開始・停止・再開の画面を生成、終了コード0。
- 単体EXEのトレイ起動を20秒計測: Working Set約72.6MB、マシン全体に対する平均CPU約0.25%、終了コード0。長時間常駐の保証ではありません。

画面検証のプロセスはPNG生成を含むため、そこでのCPU・メモリ値を通常のアイドル負荷としては扱っていません。

## 今回の検証範囲外

Windows 10、物理デバイスの抜き差しとBluetooth復旧、実ログインでの自動起動、実際のトレイクリック、SpotifyとDiscord固有の組合せ、数日単位の常駐負荷は未検証です。既存の短時間テスト成功だけで、これらを確認済みとはしていません。

検証コード: [Program.cs](E:/AI_Playground/audio-culprit/artifacts/review/Program.cs)。音声の検証結果: [monitor-results.txt](E:/AI_Playground/audio-culprit/artifacts/review/monitor-results.txt)。配布EXEの計測: [runtime.json](E:/AI_Playground/audio-culprit/artifacts/review/packaged/runtime.json)、[tray-runtime.json](E:/AI_Playground/audio-culprit/artifacts/review/tray/tray-runtime.json)。
