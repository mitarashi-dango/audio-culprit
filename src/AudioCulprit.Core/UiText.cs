using System.Globalization;
using System.Resources;

namespace AudioCulprit.Core;

/// <summary>Japanese UI for Japanese display cultures; English for every other culture.</summary>
public static class UiText
{
    private static readonly ResourceManager Resources = new("AudioCulprit.Core.Strings", typeof(UiText).Assembly);
    public static CultureInfo LanguageFor(CultureInfo culture) =>
        CultureInfo.GetCultureInfo(culture.TwoLetterISOLanguageName == "ja" ? "ja" : "en");

    public static void Initialize()
    {
        var language = LanguageFor(CultureInfo.CurrentUICulture);
        CultureInfo.DefaultThreadCurrentUICulture = language;
        CultureInfo.CurrentUICulture = language;
    }

    public static string Get(string key) => Resources.GetString(key, LanguageFor(CultureInfo.CurrentUICulture))
        ?? throw new MissingManifestResourceException($"Missing UI string: {key}");

    public static string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
