using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using AudioCulprit.Core;
using Forms = System.Windows.Forms;
namespace AudioCulprit;

public partial class App : Application
{
    public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioCulprit");
    public HistoryRepository Repository { get; private set; } = null!;
    public AudioMonitorService Monitor { get; private set; } = null!;
    private HistoryWriter? writer;
    private readonly HistorySnapshot historySnapshot = new();
    private readonly object historyGate = new();
    internal AudioEvent[] ReadHistory() => historySnapshot.Read();
    internal IgnoreRule[] ReadRules() => historySnapshot.ReadRules();
    private HotkeyService? hotkey;
    private QuickWindow? quick;
    private string settingsPath = "";
    internal bool HotkeyEnabled { get; private set; } = true;
    internal string HotkeyStatus => !HotkeyEnabled ? "無効" : hotkey?.Registered == true ? hotkey.Label + " で「今の音？」を表示" : "登録できません。他のアプリで使われている可能性があります。";
    internal bool SetHotkey(bool enabled)
    {
        if (hotkey == null) return false;
        var success = hotkey.Enable(enabled);
        HotkeyEnabled = enabled;
        File.WriteAllText(settingsPath, System.Text.Json.JsonSerializer.Serialize(new { HotkeyEnabled }));
        return success;
    }
    internal void ShowQuick()
    {
        var active = Monitor.Active.ToArray();
        var candidate = historySnapshot.Latest(active);
        quick?.Close();
        quick = new QuickWindow(candidate, Monitor.Enabled, item => { ShowMain(); ((MainWindow)MainWindow).Details(item); }, item =>
        {
            Ignore(new IgnoreRule(item.ProcessName, item.ProcessPath));
            (MainWindow as MainWindow)?.ReloadRules();
        });
        quick.Closed += (_, _) => quick = null;
        quick.Show();
        quick.Activate();
    }
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? appIcon;
    private Mutex? mutex;
    private Forms.ToolStripMenuItem? monitorToggle;
    private readonly System.Windows.Threading.DispatcherTimer housekeeping = new() { Interval = TimeSpan.FromSeconds(30) };
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        mutex = new Mutex(true, "Local\\AudioCulprit.v02", out var first);
        if (!first)
        {
            Shutdown();
            return;
        }
        try
        {
            var verifyIndex = Array.IndexOf(e.Args, "--verify");
            var verifyFolder = verifyIndex >= 0 && e.Args.Length > verifyIndex + 1 ? Path.GetFullPath(e.Args[verifyIndex + 1]) : null;
            Repository = new HistoryRepository(Path.Combine(verifyFolder ?? DataFolder, "audio-culprit.db"));
            foreach (var item in Repository.Load()) historySnapshot.Add(item);
            historySnapshot.SetRules(Repository.Rules());
            settingsPath = Path.Combine(verifyFolder ?? DataFolder, "settings.json");
            if (File.Exists(settingsPath))
            {
                try { using var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settingsPath)); HotkeyEnabled = saved.RootElement.GetProperty("HotkeyEnabled").GetBoolean(); }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException) { HotkeyEnabled = true; }
            }
            writer = new HistoryWriter(Repository);
            writer.Failed += ex => Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowError("履歴を保存できません: " + ex.Message));
            Monitor = new AudioMonitorService();
            Monitor.Completed += Save;
            housekeeping.Tick += (_, _) => { try { foreach (var item in Monitor.Active) writer.Save(item with { EndTimeUtc = null }); writer.Prune(); historySnapshot.Prune(); } catch (Exception ex) { (MainWindow as MainWindow)?.ShowError(ex.Message); } };
            housekeeping.Start();
            hotkey = new HotkeyService(() => { try { ShowQuick(); } catch (Exception ex) { MessageBox.Show(ex.Message, "AudioCulprit"); } });
            hotkey.Enable(HotkeyEnabled);

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("開く", null, (_, _) => ShowMain());
            var quickMenu = menu.Items.Add("今の音？", null, (_, _) => ShowQuick());
            menu.Opening += (_, _) => quickMenu.Text = "今の音？" + (hotkey.Registered ? "（" + hotkey.Label + "）" : "");
            monitorToggle = new Forms.ToolStripMenuItem("監視を停止", null, (_, _) => SetMonitoring(!Monitor.Enabled));
            menu.Items.Add(monitorToggle);
            menu.Opening += (_, _) => UpdateTrayState();
            menu.Items.Add("設定", null, (_, _) => { ShowMain(); ((MainWindow)MainWindow).OpenSettings(); });
            menu.Items.Add("終了", null, (_, _) => Shutdown());
            using (var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/audio-culprit.ico")).Stream)
            using (var icon = new System.Drawing.Icon(iconStream))
                appIcon = (System.Drawing.Icon)icon.Clone();
            tray = new Forms.NotifyIcon { Text = "AudioCulprit · 音の犯人", Icon = appIcon, Visible = true, ContextMenuStrip = menu };
            tray.MouseClick += (_, args) => { if (args.Button == Forms.MouseButtons.Left) ShowMain(); };
            UpdateTrayState();
            if (!e.Args.Contains("--tray"))
                ShowMain();
            if (verifyFolder != null)
            {
                if (e.Args.Contains("--soak"))
                    Verification.StartSoak(this, verifyFolder, e.Args.Contains("--short-measure") ? 180 : 720);
                else if (e.Args.Contains("--tray") || e.Args.Contains("--measure"))
                    Verification.StartTray(this, verifyFolder, e.Args.Contains("--tray") ? "tray-runtime.json" : "window-runtime.json");
                else
                    Verification.Start(this, (MainWindow)MainWindow, verifyFolder);
            }
        }
        catch (Exception ex) { MessageBox.Show("起動できませんでした。\n" + ex.Message, "AudioCulprit"); Shutdown(1); }
    }
    private void Save(AudioEvent e)
    {
        lock (historyGate)
        {
            historySnapshot.Add(e);
            try
            {
                writer!.Save(e);
            }
            catch (Exception ex) { Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowError("履歴を保存できません: " + ex.Message)); }
        }
    }
    internal System.Threading.Tasks.Task ClearHistoryAsync()
    {
        lock (historyGate)
        {
            var previous = historySnapshot.ReadAll();
            return writer!.ClearAsync(() => historySnapshot.Remove(previous));
        }
    }
    internal void Ignore(IgnoreRule rule, bool remove = false)
    {
        Repository.Ignore(rule, remove);
        historySnapshot.SetRules(Repository.Rules());
    }
    private void ShowMain()
    {
        if (MainWindow is not AudioCulprit.MainWindow)
            MainWindow = new MainWindow(this);
        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
        ((MainWindow)MainWindow).RefreshMonitoringState();
    }
    internal void SetMonitoring(bool enabled)
    {
        Monitor.Enabled = enabled;
        UpdateTrayState();
        (MainWindow as MainWindow)?.RefreshMonitoringState();
    }
    private void UpdateTrayState()
    {
        if (tray != null)
            tray.Text = Monitor.Enabled ? "AudioCulprit · 監視中" : "AudioCulprit · 監視停止中";
        if (monitorToggle != null)
        {
            monitorToggle.Text = Monitor.Enabled ? "監視を停止（現在：監視中）" : "監視を開始（現在：停止中）";
            monitorToggle.Checked = Monitor.Enabled;
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        housekeeping.Stop();
        quick?.Close();
        hotkey?.Dispose();
        tray?.Dispose();
        appIcon?.Dispose();
        Monitor?.Dispose();
        writer?.Dispose();
        Repository?.Dispose();
        mutex?.Dispose();
        base.OnExit(e);
    }
}



