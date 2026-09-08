using System.Globalization;
using System.Resources;
using System.Text.Json;
using AudioCulprit.Core;

internal static class LocalizationTests
{
    public static void Run(Action<bool, string> check)
    {
        var originalUi = CultureInfo.CurrentUICulture;
        var originalDefault = CultureInfo.DefaultThreadCurrentUICulture;
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var resources = new ResourceManager("AudioCulprit.Core.Strings", typeof(UiText).Assembly);
            var english = resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
            var japanese = resources.GetResourceSet(CultureInfo.GetCultureInfo("ja"), true, false)!;
            var keys = english.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToArray();
            check(keys.Length > 90 && keys.All(k => japanese.GetString(k) != null) && english.Cast<object>().Count() == japanese.Cast<object>().Count(), "all UI resources have English and Japanese translations");
            check(keys.All(key =>
            {
                var en = System.Text.CompositeFormat.Parse(english.GetString(key)!);
                var ja = System.Text.CompositeFormat.Parse(japanese.GetString(key)!);
                return en.MinimumArgumentCount == ja.MinimumArgumentCount;
            }), "all localized format arguments match");
            foreach (var name in new[] { "ja-JP", "ja", "en-US", "en-GB", "de-DE", "fr-FR", "zh-CN", "ar-SA", "" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
                var isJapanese = name.StartsWith("ja", StringComparison.Ordinal);
                UiText.Initialize();
                check(UiText.Get("Settings") == (isJapanese ? "設定" : "Settings"), "display language selection: " + name);
                check(Task.Run(() => UiText.Get("Settings")).Result == (isJapanese ? "設定" : "Settings"), "worker language: " + name);
            }
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
            var item = new AudioEvent { EventType = "SystemSounds", DurationMs = 65000 };
            var saved = JsonSerializer.Serialize(item);
            check(item.StateLabel == "継続再生" && item.DurationLabel == "1分05秒", "Japanese event labels");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            var loaded = JsonSerializer.Deserialize<AudioEvent>(saved)!;
            check(loaded.StateLabel == "Continuous" && loaded.DurationLabel == "1 min 05 sec" && loaded.SourceLabel.StartsWith("Windows system sound"), "saved Japanese history renders in English without migration");
            check((loaded with { EndTimeUtc = DateTime.UtcNow }).StateLabel == "Ended", "ended state differs from Exit command");
            check(UiText.Format("EventDetails", "app", "path", 1, "session", DateTime.UtcNow, "end", "1 sec", "10%", .5, false, "speaker", "device", "Application").Contains("Started:"), "English detail format renders all fields");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefault;
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
