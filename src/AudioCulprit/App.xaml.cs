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
    private AppSettings settings = new();
    internal bool HotkeyEnabled => settings.HotkeyEnabled;
    internal string HotkeyStatus => !HotkeyEnabled ? UiText.Get("Disabled") : hotkey?.Registered == true ? UiText.Format("HotkeyStatus", hotkey.Label) : UiText.Get("HotkeyUnavailable");
    internal bool SetHotkey(bool enabled)
    {
        if (hotkey == null) return false;
        var success = hotkey.Enable(enabled);
        var changed = settings with { HotkeyEnabled = enabled };
        changed.Save(settingsPath);
        settings = changed;
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
        UiText.Initialize();
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
            settings = AppSettings.Load(settingsPath);
            writer = new HistoryWriter(Repository);
            writer.Failed += ex => Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowError(UiText.Get("HistorySaveFailed") + ex.Message));
            Monitor = new AudioMonitorService();
            Monitor.Completed += Save;
            housekeeping.Tick += (_, _) => { try { foreach (var item in Monitor.Active) writer.Save(item with { EndTimeUtc = null }); writer.Prune(); historySnapshot.Prune(); } catch (Exception ex) { (MainWindow as MainWindow)?.ShowError(ex.Message); } };
            housekeeping.Start();
            hotkey = new HotkeyService(() => { try { ShowQuick(); } catch (Exception ex) { MessageBox.Show(ex.Message, "AudioCulprit"); } });
            hotkey.Enable(HotkeyEnabled);

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(UiText.Get("Open"), null, (_, _) => ShowMain());
            var quickMenu = menu.Items.Add(UiText.Get("RecentSound"), null, (_, _) => ShowQuick());
            menu.Opening += (_, _) => quickMenu.Text = UiText.Get("RecentSound") + (hotkey.Registered ? " (" + hotkey.Label + ")" : "");
            monitorToggle = new Forms.ToolStripMenuItem(UiText.Get("StopMonitoring"), null, (_, _) => SetMonitoring(!Monitor.Enabled));
            menu.Items.Add(monitorToggle);
            menu.Opening += (_, _) => UpdateTrayState();
            menu.Items.Add(UiText.Get("Settings"), null, (_, _) => { ShowMain(); ((MainWindow)MainWindow).OpenSettings(); });
            updateMenu = new Forms.ToolStripMenuItem(UiText.Get("CheckUpdates"), null, async (_, _) =>
            {
                if (AvailableUpdate != null) OpenReleasePage();
                else MessageBox.Show(await CheckForUpdatesAsync(), UiText.Get("Updates"));
            });
            menu.Items.Add(updateMenu);
            menu.Items.Add(UiText.Get("Exit"), null, (_, _) => Shutdown());
            using (var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/audio-culprit.ico")).Stream)
            using (var icon = new System.Drawing.Icon(iconStream))
                appIcon = (System.Drawing.Icon)icon.Clone();
            tray = new Forms.NotifyIcon { Text = UiText.Get("TrayTitle"), Icon = appIcon, Visible = true, ContextMenuStrip = menu };
            tray.MouseClick += (_, args) => { if (args.Button == Forms.MouseButtons.Left) ShowMain(); };
            tray.BalloonTipClicked += (_, _) => OpenReleasePage();
            UpdateTrayState();
            if (!e.Args.Contains("--tray"))
                ShowMain();
            if (verifyFolder == null) StartUpdateChecks();
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
        catch (Exception ex) { MessageBox.Show(UiText.Get("StartupFailed") + ex.Message, "AudioCulprit"); Shutdown(1); }
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
            catch (Exception ex) { Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowError(UiText.Get("HistorySaveFailed") + ex.Message)); }
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
            tray.Text = Monitor.Enabled ? UiText.Get("TrayActive") : UiText.Get("TrayPaused");
        if (monitorToggle != null)
        {
            monitorToggle.Text = Monitor.Enabled ? UiText.Get("TrayStop") : UiText.Get("TrayStart");
            monitorToggle.Checked = Monitor.Enabled;
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        StopUpdateChecks();
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



