using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioCulprit.Core;
using Microsoft.Win32;
namespace AudioCulprit;

public partial class MainWindow : Window
{
    private readonly App app;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ObservableCollection<AudioEvent> historyRows = new();
    private readonly ObservableCollection<Row> suspiciousRows = new();
    private readonly ObservableCollection<string> playingRows = new();
    private bool? displayedMonitoring;
    private readonly ComboBox historyPeriod = new() { Width = 120, SelectedIndex = 0, ItemsSource = new[] { "全期間", "過去30秒", "過去1分", "過去5分" }, Margin = new Thickness(8, 0, 8, 0) };
    private readonly CheckBox shortOnly = new() { Content = "短い音だけ", VerticalAlignment = VerticalAlignment.Center };
    private List<IgnoreRule> rules;
    private AudioEvent? latest;
    private string? iconPath;
    private string? error;
    private int ticks;
    private readonly Dictionary<string, ImageSource?> icons = new();
    private sealed record Row(AudioEvent Event)
    {
        public override string ToString() => $"{Event.TimeLabel}   {Event.DisplayName}   ·   {Event.DurationLabel}";
    }
    public MainWindow(App app)
    {
        InitializeComponent();
        this.app = app;
        History.ItemsSource = historyRows;
        System.Windows.Data.CollectionViewSource.GetDefaultView(historyRows).SortDescriptions.Add(new SortDescription(nameof(AudioEvent.StartTimeUtc), ListSortDirection.Descending));
        History.Columns[0].SortDirection = ListSortDirection.Descending;
        Suspicious.ItemsSource = suspiciousRows;
        Playing.ItemsSource = playingRows;
        HistoryFilters.Children.Add(historyPeriod);
        HistoryFilters.Children.Add(shortOnly);
        rules = app.ReadRules().ToList();
        historyPeriod.SelectionChanged += (_, _) => Refresh();
        shortOnly.Click += (_, _) => Refresh();
        timer.Tick += (_, _) => Refresh();
        timer.Start();
        Refresh();
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
    public void ShowError(string message)
    {
        error = message;
        Status.Text = message;
    }
    private void Refresh()
    {
        ++ticks;
        if (!IsVisible && ticks > 1)
            return;
        var active = app.Monitor.Enabled ? app.Monitor.Active.ToArray() : Array.Empty<AudioEvent>();
        var stored = app.ReadHistory();
        var events = active.Concat(stored.Where(e => !active.Any(a => a.Id == e.Id))).OrderByDescending(e => e.StartTimeUtc).ToArray();
        latest = SuspicionEvaluator.Latest(events, rules);
        CulpritName.Text = latest?.DisplayName ?? (app.Monitor.Enabled ? "次の音を待っています" : "監視を停止しています");
        Ago.Text = latest == null ? "" : Relative(latest.StartTimeUtc);
        CulpritInfo.Text = latest == null ? "再生音の発生元が、ここに表示されます。" : $"{latest.TimeLabel}   ·   {latest.DurationLabel}   ·   Peak {latest.PeakLabel}\n{latest.DeviceName}   ·   {latest.StateLabel}";
        if (iconPath != latest?.ProcessPath)
        {
            iconPath = latest?.ProcessPath;
            CulpritIcon.Source = LoadIcon(iconPath);
        }
        var selected = (History.SelectedItem as AudioEvent)?.Id;
        var seconds = historyPeriod.SelectedIndex switch { 1 => 30, 2 => 60, 3 => 300, _ => 0 };
        var cutoff = DateTime.UtcNow.AddSeconds(-seconds);
        var visible = events.Where(e => (seconds == 0 || (e.EndTimeUtc ?? e.StartTimeUtc.AddMilliseconds(e.DurationMs)) >= cutoff) && (shortOnly.IsChecked != true || e.DurationMs < 10000)).ToArray();
        var byId = visible.ToDictionary(e => e.Id);
        {
            for (var i = historyRows.Count - 1; i >= 0; i--)
                if (!byId.ContainsKey(historyRows[i].Id)) historyRows.RemoveAt(i);
                else if (historyRows[i] != byId[historyRows[i].Id]) historyRows[i] = byId[historyRows[i].Id];
            var existing = historyRows.Select(e => e.Id).ToHashSet();
            foreach (var item in visible.Reverse())
                if (!existing.Contains(item.Id)) historyRows.Insert(0, item);
        }
        if (selected != null)
            History.SelectedItem = events.FirstOrDefault(e => e.Id == selected);
        UpdateRows(suspiciousRows, events.Where(e => e.DurationMs < 10000 && !rules.Any(r => r.Matches(e))).Take(12).Select(e => new Row(e)).ToArray());
        UpdateRows(playingRows, active.Where(e => e.EndTimeUtc == null).Select(e => $"{e.DisplayName}  ·  {e.DurationLabel}  {e.StateLabel}").DefaultIfEmpty(app.Monitor.Enabled ? "現在、再生音は検出されていません" : "監視停止中のため検出していません").ToArray());
        UpdateMonitoringAppearance();
    }
    private static void UpdateRows<T>(ObservableCollection<T> rows, T[] values)
    {
        for (var i = rows.Count - 1; i >= values.Length; i--) rows.RemoveAt(i);
        for (var i = 0; i < values.Length; i++)
            if (i >= rows.Count) rows.Add(values[i]);
            else if (!EqualityComparer<T>.Default.Equals(rows[i], values[i])) rows[i] = values[i];
    }
    internal void RefreshMonitoringState()
    {
        UpdateMonitoringAppearance();
        Refresh();
    }
    private void UpdateMonitoringAppearance()
    {
        var enabled = app.Monitor.Enabled;
        Status.Text = error ?? (enabled ? app.Monitor.Status : "監視停止中 · 新しい音は記録されません");
        if (displayedMonitoring == enabled) return;
        displayedMonitoring = enabled;
        var foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(enabled ? "#17634F" : "#8A4B10"));
        MonitorBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(enabled ? "#E5F5EE" : "#FFF1D9"));
        MonitorStateTitle.Foreground = foreground;
        MonitorStateHint.Foreground = foreground;
        MonitorStateTitle.Text = enabled ? "● 監視中" : "■ 監視停止中";
        MonitorStateHint.Text = enabled ? "再生音を検出して、発生元を記録しています。" : "新しい音は記録されません。下には保存済みの履歴を表示しています。";
        PauseButton.Content = enabled ? "■ 監視を停止" : "▶ 監視を開始";
        PauseButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(enabled ? "#34465B" : "#17634F"));
        PauseButton.ToolTip = enabled ? "音の検出と新しい記録を停止します" : "音の検出と記録を開始します";
        CulpritHeading.Text = enabled ? "今の音？" : "停止前の音（履歴）";
        Status.Foreground = foreground;
        Title = enabled ? "AudioCulprit — 監視中" : "AudioCulprit — 監視停止中";
    }
    private ImageSource? LoadIcon(string? path)
    {
        if (path == null)
            return null;
        if (icons.TryGetValue(path, out var cached))
            return cached;
        ImageSource? result = null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon != null)
            {
                result = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                result.Freeze();
            }
        }
        catch { }
        if (icons.Count >= 128)
            icons.Clear();
        icons[path] = result;
        return result;
    }
    private static string Relative(DateTime utc)
    {
        var s = Math.Max(0, (DateTime.UtcNow - utc).TotalSeconds);
        return s < 60 ? $"{s:0}秒前" : s < 3600 ? $"{s / 60:0}分前" : s < 86400 ? $"{s / 3600:0}時間前" : $"{s / 86400:0}日前";
    }
    private void PauseClick(object sender, RoutedEventArgs e)
    {
        app.SetMonitoring(!app.Monitor.Enabled);
    }
    private void SettingsClick(object sender, RoutedEventArgs e) => OpenSettings();
    internal void ReloadRules() { rules = app.ReadRules().ToList(); Refresh(); }
    private void LatestDetails(object sender, RoutedEventArgs e)
    {
        if (latest != null)
            Details(latest);
    }
    private void HistoryDetails(object sender, RoutedEventArgs e)
    {
        if (History.SelectedItem is AudioEvent item)
            Details(item);
    }
    private void HistoryDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => HistoryDetails(sender, e);
    private void SuspiciousDetails(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Suspicious.SelectedItem is Row row)
            Details(row.Event);
    }
    private Window Dialog(string title, StackPanel panel, int width = 620) => new Window { Title = title, Owner = this, Width = width, SizeToContent = SizeToContent.Height, MaxHeight = 760, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
    private static Button ActionButton(string text, Action action)
    {
        var b = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Left };
        b.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "AudioCulprit"); } };
        return b;
    }
    internal void Details(AudioEvent item)
    {
        var p = new StackPanel { Margin = new Thickness(24) };
        p.Children.Add(new TextBlock { Text = item.DisplayName, FontSize = 26, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 14) });
        p.Children.Add(new TextBox { Text = $"Process Name: {item.ProcessName}\nProcess Path: {item.ProcessPath ?? "取得できません"}\nPID: {item.ProcessId}\nSession ID: {item.SessionId}\n開始: {item.StartTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}\n終了: {(item.EndTimeUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "再生中（表示時点）")}\n再生時間: {item.DurationLabel}\n最大 Peak: {item.PeakLabel}\nSession Volume: {item.SessionVolume:P0}\nMute: {item.WasMuted}\n出力: {item.DeviceName}\nDevice ID: {item.DeviceId}\nEvent Type: {item.EventType}", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 16) });
        p.Children.Add(new TextBlock { Text = item.SourceLabel, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var folder = ActionButton("ファイルの場所を開く", () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.ProcessPath}\"") { UseShellExecute = true }));
        folder.IsEnabled = item.ProcessPath != null && File.Exists(item.ProcessPath);
        p.Children.Add(folder);
        p.Children.Add(ActionButton("このアプリを無視", () => { app.Ignore(new IgnoreRule(item.ProcessName, item.ProcessPath)); rules = app.ReadRules().ToList(); Refresh(); }));
        var muteStatus = new TextBlock { Text = "同じ実行ファイルの、現在存在する音声出力に適用します。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        var mute = new Button { Content = "このアプリをミュート", HorizontalAlignment = HorizontalAlignment.Left };
        var unmute = new Button { Content = "ミュート解除", HorizontalAlignment = HorizontalAlignment.Left };
        async void ChangeMute(bool value)
        {
            mute.IsEnabled = unmute.IsEnabled = false;
            try
            {
                var count = await app.Monitor.SetMuteAsync(item, value);
                muteStatus.Text = count == 0 ? "対象の音声出力が見つかりません。アプリを再生してからお試しください。" : $"{count} 件の音声出力を" + (value ? "ミュートしました。" : "ミュート解除しました。");
            }
            catch (Exception ex) { muteStatus.Text = "変更できませんでした（一部だけ変更された可能性があります）: " + ex.Message; }
            finally { mute.IsEnabled = unmute.IsEnabled = true; }
        }
        mute.Click += (_, _) => ChangeMute(true);
        unmute.Click += (_, _) => ChangeMute(false);
        p.Children.Add(mute);
        p.Children.Add(unmute);
        p.Children.Add(muteStatus);
        p.Children.Add(ActionButton("Windows音量ミキサーを開く", () => Process.Start(new ProcessStartInfo("ms-settings:apps-volume") { UseShellExecute = true })));
        Dialog("音の詳細", p).ShowDialog();
    }
    public void OpenSettings()
    {
        var p = new StackPanel { Margin = new Thickness(24) };
        p.Children.Add(new TextBlock { Text = "設定", FontSize = 26, FontWeight = FontWeights.Bold });
        var hotkey = new CheckBox { Content = "ホットキーで「今の音？」を表示（Aが使用中ならF10）", IsChecked = app.HotkeyEnabled, Margin = new Thickness(4, 16, 0, 4) };
        var hotkeyStatus = new TextBlock { Text = app.HotkeyStatus, TextWrapping = TextWrapping.Wrap };
        hotkey.Click += (_, _) => { try { app.SetHotkey(hotkey.IsChecked == true); hotkeyStatus.Text = app.HotkeyStatus; } catch (Exception ex) { hotkeyStatus.Text = ex.Message; } };
        p.Children.Add(hotkey);
        p.Children.Add(hotkeyStatus);
        var startup = new CheckBox { Content = "Windowsログイン時に起動（トレイに常駐）", Margin = new Thickness(4, 20, 0, 16) };
        const string run = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        using (var key = Registry.CurrentUser.OpenSubKey(run))
            startup.IsChecked = key?.GetValue("AudioCulprit") != null;
        startup.Click += (_, _) => { try { using var key = Registry.CurrentUser.CreateSubKey(run); if (startup.IsChecked == true) key.SetValue("AudioCulprit", $"\"{Environment.ProcessPath}\" --tray"); else key.DeleteValue("AudioCulprit", false); } catch (Exception ex) { ShowError(ex.Message); startup.IsChecked = false; } };
        p.Children.Add(startup);
        p.Children.Add(new TextBlock { Text = "無視するアプリ（履歴には残ります）", FontWeight = FontWeights.Bold });
        var list = new ListBox { ItemsSource = rules, Height = 140, Margin = new Thickness(0, 8, 0, 8) };
        p.Children.Add(list);
        p.Children.Add(ActionButton("選択したアプリの無視を解除", () => { if (list.SelectedItem is IgnoreRule rule) { app.Ignore(rule, true); rules = app.ReadRules().ToList(); list.ItemsSource = rules; Refresh(); } }));
        p.Children.Add(new TextBlock { Text = "履歴は7日間・最大10,000件を保持します。\n音声自体は保存せず、マイクにもアクセスしません。\n監視間隔 50ms / 短音の統合間隔 500ms", Margin = new Thickness(0, 16, 0, 12) });
        var clearHistory = new Button { Content = "すべての履歴を削除", HorizontalAlignment = HorizontalAlignment.Left };
        clearHistory.Click += async (_, _) =>
        {
            if (MessageBox.Show("保存済みのすべての音声履歴を削除します。元に戻せません。\n再生中の音は終了後に新たに記録されます。", "履歴の削除", MessageBoxButton.YesNo, MessageBoxImage.None, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            clearHistory.IsEnabled = false;
            try { await app.ClearHistoryAsync(); Refresh(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "履歴を削除できませんでした"); }
            finally { clearHistory.IsEnabled = true; }
        };
        p.Children.Add(clearHistory);
        p.Children.Add(new TextBlock { Text = "保存先: " + App.DataFolder, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 16, 0, 0) });
        p.Children.Add(ActionButton("ライセンス", () =>
        {
            var assembly = typeof(App).Assembly;
            var text = new System.Text.StringBuilder();
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.Contains("Licenses")).OrderBy(n => n))
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                text.AppendLine(name).AppendLine(reader.ReadToEnd()).AppendLine();
            }
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBox { Text = text.ToString(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap });
            Dialog("ライセンス", panel, 760).ShowDialog();
        }));
        Dialog("AudioCulprit の設定", p).ShowDialog();
    }
}

