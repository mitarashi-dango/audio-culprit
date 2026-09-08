using System.Text.Json;

namespace AudioCulprit.Core;

public sealed record AppSettings
{
    public bool HotkeyEnabled { get; init; } = true;
    public bool CheckForUpdates { get; init; } = true;
    public DateTime? LastUpdateCheckUtc { get; init; }
    public string? LastNotifiedVersion { get; init; }

    public bool ShouldNotify(Version version) => CheckForUpdates && LastNotifiedVersion != version.ToString();

    public bool UpdateCheckDue(DateTime now) => CheckForUpdates &&
        (LastUpdateCheckUtc == null || now < LastUpdateCheckUtc || now - LastUpdateCheckUtc >= TimeSpan.FromDays(1));

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public void Save(string path)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, path, true);
    }
}
