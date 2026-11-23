using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace Vstavka.Core.Localization;

public sealed record LanguageOption(string Code, string Name, string EnglishName);

/// <summary>English is the source catalog and fallback. Stored clipboard data is never translated.</summary>
public sealed class Strings : INotifyPropertyChanged
{
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs = new(StringComparer.OrdinalIgnoreCase);
    private static IReadOnlyDictionary<string, string> _active = ReadCatalog("en");
    public static Strings Current { get; } = new();
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");
    public static string Language { get; private set; } = "en";
    public static bool IsRightToLeft => Culture.TextInfo.IsRightToLeft;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string this[string key] => Get(key);
    public static IReadOnlyList<LanguageOption> AvailableLanguages { get; } = new[]
    {
        new LanguageOption("en", "English", "English"),
        new LanguageOption("ar", "العربية", "Arabic"),
        new LanguageOption("bn", "বাংলা", "Bengali"),
        new LanguageOption("zh-Hans", "简体中文", "Chinese (Simplified)"),
        new LanguageOption("zh-Hant", "繁體中文", "Chinese (Traditional)"),
        new LanguageOption("cs", "Čeština", "Czech"),
        new LanguageOption("fr", "Français", "French"),
        new LanguageOption("de", "Deutsch", "German"),
        new LanguageOption("hi", "हिन्दी", "Hindi"),
        new LanguageOption("hu", "Magyar", "Hungarian"),
        new LanguageOption("id", "Bahasa Indonesia", "Indonesian"),
        new LanguageOption("it", "Italiano", "Italian"),
        new LanguageOption("ja", "日本語", "Japanese"),
        new LanguageOption("ko", "한국어", "Korean"),
        new LanguageOption("fa", "فارسی", "Persian"),
        new LanguageOption("pl", "Polski", "Polish"),
        new LanguageOption("pt-BR", "Português (Brasil)", "Portuguese"),
        new LanguageOption("ru", "Русский", "Russian"),
        new LanguageOption("es", "Español", "Spanish"),
        new LanguageOption("th", "ไทย", "Thai"),
        new LanguageOption("tr", "Türkçe", "Turkish"),
        new LanguageOption("uk", "Українська", "Ukrainian"),
        new LanguageOption("ur", "اردو", "Urdu"),
        new LanguageOption("vi", "Tiếng Việt", "Vietnamese")
    };
    internal static string NormalizeLanguage(string? language) =>
        AvailableLanguages.FirstOrDefault(x => string.Equals(x.Code, language, StringComparison.OrdinalIgnoreCase))?.Code ?? "en";

    public static void SetLanguage(string? language)
    {
        var code=NormalizeLanguage(language);
        _active=ReadCatalog(code); Language=code; Culture=CultureInfo.GetCultureInfo(code);
        // UI formatting follows the selected language; persistence always uses JSON/invariant values.
        CultureInfo.CurrentUICulture=Culture;
        Current.PropertyChanged?.Invoke(Current,new PropertyChangedEventArgs("Item[]"));
    }
    public static string Get(string key)
    {
        if(_active.TryGetValue(key,out var value) && !string.IsNullOrWhiteSpace(value))return value;
        var english=ReadCatalog("en");return english.TryGetValue(key,out value)?value:key;
    }
    public static string Format(string key,params object[] args) => string.Format(Culture,Get(key),args);
    public static IReadOnlyDictionary<string,string> ReadCatalog(string code)
    {
        lock(Catalogs)
        {
            if(Catalogs.TryGetValue(code,out var cached))return cached;
            var assembly=typeof(Strings).Assembly;
            using var stream=assembly.GetManifestResourceStream($"Vstavka.Core.Localization.{code}.json");
            if(stream==null)
            {
                if(code=="en")throw new InvalidOperationException("The English source catalog is missing.");
                return ReadCatalog("en");
            }
            var catalog = JsonSerializer.Deserialize(stream, VstavkaJsonContext.Default.LocalizationCatalog)
                ?? throw new InvalidDataException("Invalid localization catalog.");
            Catalogs[code]=catalog;return catalog;
        }
    }
}
