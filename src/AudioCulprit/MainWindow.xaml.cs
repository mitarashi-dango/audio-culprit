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
    private readonly ComboBox historyPeriod = new() { Width = 120, SelectedIndex = 0, ItemsSource = new[] { UiText.Get("AllTime"), UiText.Get("Last30Seconds"), UiText.Get("LastMinute"), UiText.Get("Last5Minutes") }, Margin = new Thickness(8, 0, 8, 0) };
    private readonly CheckBox shortOnly = new() { Content = UiText.Get("ShortOnly"), VerticalAlignment = VerticalAlignment.Center };
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
        CulpritName.Text = latest?.DisplayName ?? (app.Monitor.Enabled ? UiText.Get("Waiting") : UiText.Get("MonitoringStopped"));
        Ago.Text = latest == null ? "" : Relative(latest.StartTimeUtc);
        CulpritInfo.Text = latest == null ? UiText.Get("SourcePlaceholder") : $"{latest.TimeLabel}   ·   {latest.DurationLabel}   ·   Peak {latest.PeakLabel}\n{latest.DeviceName}   ·   {latest.StateLabel}";
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
        UpdateRows(playingRows, active.Where(e => e.EndTimeUtc == null).Select(e => $"{e.DisplayName}  ·  {e.DurationLabel}  {e.StateLabel}").DefaultIfEmpty(app.Monitor.Enabled ? UiText.Get("NoPlayback") : UiText.Get("NoDetectionPaused")).ToArray());
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
        Status.Text = error ?? (enabled ? app.Monitor.Status : UiText.Get("PausedStatus"));
        if (displayedMonitoring == enabled) return;
        displayedMonitoring = enabled;
        var foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(enabled ? "#17634F" : "#8A4B10"));
        MonitorBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(enabled ? "#E5F5EE" : "#FFF1D9"));
        MonitorStateTitle.Foreground = foreground;
        MonitorStateHint.Foreground = foreground;
        MonitorStateTitle.Text = enabled ? UiText.Get("ActiveHeading") : UiText.Get("PausedHeading");
        MonitorStateHint.Text = enabled ? UiText.Get("ActiveHint") : UiText.Get("PausedHint");
        PauseButton.Content = enabled ? UiText.Get("StopButton") : UiText.Get("StartButton");
        PauseButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(enabled ? "#34465B" : "#17634F"));
        PauseButton.ToolTip = enabled ? UiText.Get("StopHint") : UiText.Get("StartHint");
        CulpritHeading.Text = enabled ? UiText.Get("RecentSound") : UiText.Get("PreviousSound");
        Status.Foreground = foreground;
        Title = enabled ? UiText.Get("ActiveTitle") : UiText.Get("PausedTitle");
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
        return s < 60 ? UiText.Format("SecondsAgo", s) : s < 3600 ? UiText.Format("MinutesAgo", s / 60) : s < 86400 ? UiText.Format("HoursAgo", s / 3600) : UiText.Format("DaysAgo", s / 86400);
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
        p.Children.Add(new TextBox { Text = UiText.Format("EventDetails", item.ProcessName, item.ProcessPath ?? UiText.Get("Unavailable"), item.ProcessId, item.SessionId,
            item.StartTimeUtc.ToLocalTime(), item.EndTimeUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") ?? UiText.Get("PlayingAtOpen"),
            item.DurationLabel, item.PeakLabel, item.SessionVolume, item.WasMuted, item.DeviceName, item.DeviceId, item.EventType),
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 16) });
        p.Children.Add(new TextBlock { Text = item.SourceLabel, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var folder = ActionButton(UiText.Get("OpenFolder"), () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.ProcessPath}\"") { UseShellExecute = true }));
        folder.IsEnabled = item.ProcessPath != null && File.Exists(item.ProcessPath);
        p.Children.Add(folder);
        p.Children.Add(ActionButton(UiText.Get("IgnoreApp"), () => { app.Ignore(new IgnoreRule(item.ProcessName, item.ProcessPath)); rules = app.ReadRules().ToList(); Refresh(); }));
        var muteStatus = new TextBlock { Text = UiText.Get("MuteHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        var mute = new Button { Content = UiText.Get("MuteApp"), HorizontalAlignment = HorizontalAlignment.Left };
        var unmute = new Button { Content = UiText.Get("Unmute"), HorizontalAlignment = HorizontalAlignment.Left };
        async void ChangeMute(bool value)
        {
            mute.IsEnabled = unmute.IsEnabled = false;
            try
            {
                var count = await app.Monitor.SetMuteAsync(item, value);
                muteStatus.Text = count == 0 ? UiText.Get("MuteNotFound") : UiText.Format(value ? "MutedCount" : "UnmutedCount", count);
            }
            catch (Exception ex) { muteStatus.Text = UiText.Get("MuteFailed") + ex.Message; }
            finally { mute.IsEnabled = unmute.IsEnabled = true; }
        }
        mute.Click += (_, _) => ChangeMute(true);
        unmute.Click += (_, _) => ChangeMute(false);
        p.Children.Add(mute);
        p.Children.Add(unmute);
        p.Children.Add(muteStatus);
        p.Children.Add(ActionButton(UiText.Get("OpenMixer"), () => Process.Start(new ProcessStartInfo("ms-settings:apps-volume") { UseShellExecute = true })));
        Dialog(UiText.Get("SoundDetails"), p).ShowDialog();
    }
    public void OpenSettings()
    {
        var p = new StackPanel { Margin = new Thickness(24) };
        p.Children.Add(new TextBlock { Text = UiText.Get("Settings"), FontSize = 26, FontWeight = FontWeights.Bold });
        var hotkey = new CheckBox { Content = UiText.Get("HotkeyOption"), IsChecked = app.HotkeyEnabled, Margin = new Thickness(4, 16, 0, 4) };
        var hotkeyStatus = new TextBlock { Text = app.HotkeyStatus, TextWrapping = TextWrapping.Wrap };
        hotkey.Click += (_, _) => { try { app.SetHotkey(hotkey.IsChecked == true); hotkeyStatus.Text = app.HotkeyStatus; } catch (Exception ex) { hotkeyStatus.Text = ex.Message; } };
        p.Children.Add(hotkey);
        p.Children.Add(hotkeyStatus);
#if !STORE_BUILD
        var startup = new CheckBox { Content = UiText.Get("StartupOption"), Margin = new Thickness(4, 20, 0, 16) };
        const string run = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        using (var key = Registry.CurrentUser.OpenSubKey(run))
            startup.IsChecked = key?.GetValue("AudioCulprit") != null;
        startup.Click += (_, _) => { try { using var key = Registry.CurrentUser.CreateSubKey(run); if (startup.IsChecked == true) key.SetValue("AudioCulprit", $"\"{Environment.ProcessPath}\" --tray"); else key.DeleteValue("AudioCulprit", false); } catch (Exception ex) { ShowError(ex.Message); startup.IsChecked = false; } };
        p.Children.Add(startup);
        p.Children.Add(new TextBlock { Text = UiText.Get("Updates"), FontWeight = FontWeights.Bold });
        var automaticUpdates = new CheckBox { Content = UiText.Get("AutomaticUpdates"), IsChecked = app.AutomaticUpdates, Margin = new Thickness(4, 8, 0, 4) };
        var updateStatus = new TextBlock { Text = app.AvailableUpdate == null ? UiText.Format("InstalledVersion", App.CurrentVersion.ToString(3)) : UiText.Format("UpdateAvailable", app.AvailableUpdate.Version), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        automaticUpdates.Click += (_, _) =>
        {
            try { app.SetAutomaticUpdates(automaticUpdates.IsChecked == true); }
            catch (Exception ex) { automaticUpdates.IsChecked = app.AutomaticUpdates; updateStatus.Text = ex.Message; }
        };
        p.Children.Add(automaticUpdates);
        p.Children.Add(new TextBlock { Text = UiText.Get("UpdatePrivacy"), TextWrapping = TextWrapping.Wrap, FontSize = 11 });
        var updateActions = new WrapPanel();
        var checkUpdates = new Button { Content = UiText.Get("CheckUpdates") };
        var releasePage = new Button { Content = UiText.Get("OpenRelease"), Visibility = app.AvailableUpdate == null ? Visibility.Collapsed : Visibility.Visible };
        releasePage.Click += (_, _) => app.OpenReleasePage();
        checkUpdates.Click += async (_, _) =>
        {
            checkUpdates.IsEnabled = false;
            updateStatus.Text = UiText.Get("CheckingUpdates");
            try { updateStatus.Text = await app.CheckForUpdatesAsync(); }
            finally { checkUpdates.IsEnabled = true; releasePage.Visibility = app.AvailableUpdate == null ? Visibility.Collapsed : Visibility.Visible; }
        };
        updateActions.Children.Add(checkUpdates);
        updateActions.Children.Add(releasePage);
        p.Children.Add(updateActions);
        p.Children.Add(updateStatus);
#else
        p.Children.Add(new TextBlock { Text = UiText.Get("StoreUpdates"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 12) });
#endif
        p.Children.Add(new TextBlock { Text = UiText.Get("IgnoredApps"), FontWeight = FontWeights.Bold });
        var list = new ListBox { ItemsSource = rules, Height = 140, Margin = new Thickness(0, 8, 0, 8) };
        p.Children.Add(list);
        p.Children.Add(ActionButton(UiText.Get("UnignoreApp"), () => { if (list.SelectedItem is IgnoreRule rule) { app.Ignore(rule, true); rules = app.ReadRules().ToList(); list.ItemsSource = rules; Refresh(); } }));
        p.Children.Add(new TextBlock { Text = UiText.Get("RetentionHint"), Margin = new Thickness(0, 16, 0, 12) });
        var clearHistory = new Button { Content = UiText.Get("ClearHistory"), HorizontalAlignment = HorizontalAlignment.Left };
        clearHistory.Click += async (_, _) =>
        {
            if (MessageBox.Show(UiText.Get("ClearConfirmation"), UiText.Get("ClearTitle"), MessageBoxButton.YesNo, MessageBoxImage.None, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            clearHistory.IsEnabled = false;
            try { await app.ClearHistoryAsync(); Refresh(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, UiText.Get("ClearFailed")); }
            finally { clearHistory.IsEnabled = true; }
        };
        p.Children.Add(clearHistory);
        p.Children.Add(new TextBlock { Text = UiText.Get("StorageLocation") + App.DataFolder, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 16, 0, 0) });
        p.Children.Add(ActionButton(UiText.Get("Licenses"), () =>
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
            Dialog(UiText.Get("Licenses"), panel, 760).ShowDialog();
        }));
        Dialog(UiText.Get("SettingsTitle"), p).ShowDialog();
    }
}

