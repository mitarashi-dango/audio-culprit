using System.Net;
using AudioCulprit.Core;

internal static class UpdateTests
{
    public static void Run(Action<bool, string> check)
    {
        var current = new Version(0, 2, 0, 0);
        string Release(string tag, bool draft = false, bool prerelease = false) => System.Text.Json.JsonSerializer.Serialize(new { tag_name = tag, draft, prerelease, html_url = "https://example.invalid/untrusted" });
        check(UpdateChecker.Parse(Release("v0.2.1"), current)?.Version == new Version(0, 2, 1), "new patch release detected");
        check(UpdateChecker.Parse(Release("0.10.0"), current)?.Version == new Version(0, 10, 0), "release versions compare numerically");
        check(new[] { "v0.2.0", "v0.1.9", "v0.3.0-beta.1", "v01.2.3", "not-a-version", "v9999999999999.0.0" }.All(tag => UpdateChecker.Parse(Release(tag), current) == null), "equal, older, prerelease and invalid tags ignored");
        check(UpdateChecker.Parse(Release("v0.3.0", draft: true), current) == null && UpdateChecker.Parse(Release("v0.3.0", prerelease: true), current) == null, "draft and prerelease flags excluded");
        check(UpdateChecker.Parse(Release("v0.3.0"), current)!.ReleasePage.AbsoluteUri == UpdateChecker.ReleasesPage + "/tag/v0.3.0", "release destination stays in the official repository");
        using var client = new HttpClient(new FakeHandler(request =>
        {
            check(request.RequestUri!.AbsoluteUri == UpdateChecker.Endpoint && request.Headers.UserAgent.ToString() == "AudioCulprit/0.2.0" && request.Content == null && request.Headers.Authorization == null, "update request has no history, body or credentials");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Release("v0.3.0")) };
        }));
        check(new UpdateChecker(client).CheckAsync(current).GetAwaiter().GetResult()?.Version == new Version(0, 3, 0), "GitHub response handled asynchronously");
        using var missing = new HttpClient(new FakeHandler(_ => new(HttpStatusCode.NotFound)));
        check(new UpdateChecker(missing).CheckAsync(current).GetAwaiter().GetResult() == null, "repository with no releases handled");
        using var limited = new HttpClient(new FakeHandler(_ => new(HttpStatusCode.Forbidden)));
        try { new UpdateChecker(limited).CheckAsync(current).GetAwaiter().GetResult(); check(false, "rate limit must report failure"); }
        catch (HttpRequestException) { check(true, "rate limit is not reported as up to date"); }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { new UpdateChecker(client).CheckAsync(current, cancellation.Token).GetAwaiter().GetResult(); check(false, "cancellation must propagate"); }
        catch (OperationCanceledException) { check(true, "update check can be canceled on shutdown"); }
        var now = DateTime.UtcNow;
        check(new AppSettings().UpdateCheckDue(now) && !(new AppSettings { CheckForUpdates = false }).UpdateCheckDue(now), "automatic update default and opt-out");
        var settings = new AppSettings { LastUpdateCheckUtc = now, LastNotifiedVersion = "0.3.0" };
        check(!settings.ShouldNotify(new Version(0, 3, 0)) && settings.ShouldNotify(new Version(0, 4, 0)) && !(settings with { CheckForUpdates = false }).ShouldNotify(new Version(0, 4, 0)), "one notification per version and opt-out respected");
        check(!settings.UpdateCheckDue(now.AddHours(23)) && settings.UpdateCheckDue(now.AddDays(1)) && settings.UpdateCheckDue(now.AddHours(-1)), "daily checks and clock rollback");
        var directory = Path.Combine(Path.GetTempPath(), "AudioCulprit-update-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "{\"HotkeyEnabled\":false}");
            check(!AppSettings.Load(path).HotkeyEnabled && AppSettings.Load(path).CheckForUpdates, "old hotkey settings migrate with updates enabled");
            (settings with { CheckForUpdates = false, HotkeyEnabled = false }).Save(path);
            var loaded = AppSettings.Load(path);
            check(!loaded.CheckForUpdates && !loaded.HotkeyEnabled && loaded.LastNotifiedVersion == "0.3.0" && loaded.LastUpdateCheckUtc == now, "update preferences and notification history survive restart");
            File.WriteAllText(path, "broken json");
            check(AppSettings.Load(path) == new AppSettings(), "corrupt settings fall back to defaults");
        }
        finally { Directory.Delete(directory, true); }
    }
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
