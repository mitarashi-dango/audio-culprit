using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AudioCulprit.Core;

public sealed record AvailableUpdate(Version Version, Uri ReleasePage);

public sealed class UpdateChecker(HttpClient client)
{
    public const string ReleasesPage = "https://github.com/mitarashi-dango/audio-culprit/releases";
    public const string Endpoint = "https://api.github.com/repos/mitarashi-dango/audio-culprit/releases/latest";

    public async Task<AvailableUpdate?> CheckAsync(Version current, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.UserAgent.ParseAdd($"AudioCulprit/{current.ToString(3)}");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Parse(json, current);
    }

    public static AvailableUpdate? Parse(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var release = document.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var tag = release.GetProperty("tag_name").GetString();
        if (tag == null || !Regex.IsMatch(tag, @"^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)) return null;
        if (!Version.TryParse(tag.TrimStart('v'), out var version) || version <= new Version(current.Major, current.Minor, Math.Max(0, current.Build))) return null;
        // Build the destination from the known repository, never from a response-provided URL.
        return new AvailableUpdate(version, new Uri(ReleasesPage + "/tag/" + Uri.EscapeDataString(tag)));
    }
}
