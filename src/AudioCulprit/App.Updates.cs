using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AudioCulprit.Core;
using Forms = System.Windows.Forms;

namespace AudioCulprit;

public partial class App
{
    internal static Version CurrentVersion => typeof(App).Assembly.GetName().Version!;
    internal bool AutomaticUpdates => settings.CheckForUpdates;
    internal AvailableUpdate? AvailableUpdate { get; private set; }
    private Forms.ToolStripMenuItem? updateMenu = null;
    private readonly HttpClient updateClient = new() { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 1024 * 1024 };
    private readonly CancellationTokenSource updateShutdown = new();
    private readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromSeconds(15) };
#if !STORE_BUILD
    private Task<string>? updateTask;
#endif

    private void StartUpdateChecks()
    {
#if !STORE_BUILD
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Interval = TimeSpan.FromHours(1);
            if (settings.UpdateCheckDue(DateTime.UtcNow)) await CheckForUpdatesAsync();
        };
        updateTimer.Start();
#endif
    }

    internal void SetAutomaticUpdates(bool enabled)
    {
        var changed = settings with { CheckForUpdates = enabled };
        changed.Save(settingsPath);
        settings = changed;
        if (enabled) updateTimer.Interval = TimeSpan.FromSeconds(15);
    }

    internal Task<string> CheckForUpdatesAsync()
    {
#if STORE_BUILD
        return Task.FromResult(UiText.Get("StoreUpdates"));
#else
        if (updateTask is { IsCompleted: false }) return updateTask;
        return updateTask = CheckForUpdatesCoreAsync();
#endif
    }

    private async Task<string> CheckForUpdatesCoreAsync()
    {
        settings = settings with { LastUpdateCheckUtc = DateTime.UtcNow };
        try
        {
            AvailableUpdate = await new UpdateChecker(updateClient).CheckAsync(CurrentVersion, updateShutdown.Token);
            if (updateShutdown.IsCancellationRequested) return "";
            if (updateMenu != null) updateMenu.Text = AvailableUpdate == null ? UiText.Get("CheckUpdates") : UiText.Format("UpdateAvailable", AvailableUpdate.Version);
            if (AvailableUpdate != null && tray != null && settings.ShouldNotify(AvailableUpdate.Version))
            {
                tray.ShowBalloonTip(10000, UiText.Get("Updates"), UiText.Format("UpdateNotification", AvailableUpdate.Version), Forms.ToolTipIcon.Info);
                settings = settings with { LastNotifiedVersion = AvailableUpdate.Version.ToString() };
            }
            settings.Save(settingsPath);
            return AvailableUpdate == null ? UiText.Format("NoUpdates", CurrentVersion.ToString(3)) : UiText.Format("UpdateAvailable", AvailableUpdate.Version);
        }
        catch (OperationCanceledException) when (updateShutdown.IsCancellationRequested) { return ""; }
        catch (Exception)
        {
            // Offline, rate-limited, malformed responses and settings I/O must not interrupt monitoring.
            try { settings.Save(settingsPath); } catch { }
            return UiText.Get("UpdateCheckFailed");
        }
    }

    internal void OpenReleasePage()
    {
        if (AvailableUpdate == null) return;
        try { Process.Start(new ProcessStartInfo(AvailableUpdate.ReleasePage.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, UiText.Get("Updates")); }
    }

    private void StopUpdateChecks()
    {
        updateTimer.Stop();
        updateShutdown.Cancel();
        updateClient.Dispose();
    }
}
