using System.Text.Json;
using System.Text.RegularExpressions;
using Vstavka.Core;
using Vstavka.Core.Localization;

var failures=new List<string>();
void Check(bool condition,string message) {if(!condition)failures.Add(message);}
var source=Strings.ReadCatalog("en");
var resources=typeof(Strings).Assembly.GetManifestResourceNames();
Check(Strings.AvailableLanguages.Select(x=>x.Code).Distinct().Count()==Strings.AvailableLanguages.Count,"Every language is unique.");
Check(resources.Where(x=>x.StartsWith("Vstavka.Core.Localization.")&&x.EndsWith(".json")).Order().SequenceEqual(Strings.AvailableLanguages.Select(x=>$"Vstavka.Core.Localization.{x.Code}.json").Order()),"The picker exposes every shipped catalog exactly once.");
Check(new[]{"zh-Hant","cs","hu","uk"}.All(code=>Strings.AvailableLanguages.Any(x=>x.Code==code)),"Desktop-audience language additions are present.");
Check(Strings.AvailableLanguages[0].Code=="en","English is first.");
Check(Strings.AvailableLanguages.Skip(1).Select(x=>x.EnglishName).SequenceEqual(Strings.AvailableLanguages.Skip(1).Select(x=>x.EnglishName).Order(StringComparer.Ordinal)),"Remaining languages are alphabetical, without Russian priority.");
Check(new AppSettings().Language=="en","English is the default for new and migrated settings.");
string[] Arguments(string text)=>Regex.Matches(text,@"\{\d+(?:[^}]*)?\}").Select(x=>x.Value).Order().ToArray();
foreach(var language in Strings.AvailableLanguages)
{
    var catalog=Strings.ReadCatalog(language.Code);
    Check(source.Keys.Order().SequenceEqual(catalog.Keys.Order()),$"Key parity: {language.Code}");
    foreach(var (key,value) in catalog)
    {
        Check(!string.IsNullOrWhiteSpace(value),$"Empty: {language.Code}.{key}");
        Check(!value.Contains('\uFFFD'),$"Broken Unicode: {language.Code}.{key}");
        if(!source.TryGetValue(key,out var english))continue;
        Check(Arguments(english).SequenceEqual(Arguments(value)),$"Placeholder mismatch: {language.Code}.{key}");
        Check(english.Count(c=>c=='\n')==value.Count(c=>c=='\n'),$"Newline mismatch: {language.Code}.{key}");
        try { string.Format(value,123,456); }catch(FormatException){failures.Add($"Invalid format: {language.Code}.{key}");}
    }
    Strings.SetLanguage(language.Code);
    Check(Strings.Format("WelcomeDescription", "<shortcut>").Contains("<shortcut>"), $"Welcome includes the configured shortcut: {language.Code}");
    Check(Strings.Language==language.Code,$"Language selection: {language.Code}");
    Check(Strings.IsRightToLeft==(language.Code is "ar" or "fa" or "ur"),$"Direction: {language.Code}");
    Check(Strings.Get("Settings")==catalog["Settings"],$"Selected locale lookup: {language.Code}");
    Console.WriteLine($"{language.Code}: {catalog.Count} keys checked");
}
Strings.SetLanguage("invalid-locale");
Check(Strings.Language=="en"&&Strings.Get("Settings")=="Settings","Unknown locale safely falls back to English.");
var legacy=JsonSerializer.Deserialize<AppSettings>("{\"Volume\":0.63,\"Hotkey\":\"Alt+V\"}")!;
Check(legacy.Language=="en" && legacy.Theme=="system" && legacy.Accent=="windows" && legacy.Volume==0.63,"Old settings keep existing values and gain defaults.");
var directory=Path.Combine(Path.GetTempPath(),"Vstavka.LocaleTests."+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var store=new HistoryStore(directory);store.Load();
    const string original="Notes: café, 日本語, العربية, русский";
    store.Add(new ClipItem {Text=original,Kind=ClipKind.Text,SourceApp="Synthetic test"});
    store.Settings.Language="ar";store.Settings.Theme="dark";store.Settings.Accent="teal";
    var save=store.SaveAsync();
    store.Settings.Language="en";store.Settings.Theme="light";store.Settings.Accent="amber";
    await save;
    var restored=new HistoryStore(directory);restored.Load();
    Check(restored.Settings.Language=="ar"&&restored.Settings.Theme=="dark"&&restored.Settings.Accent=="teal","Language/theme/accent captured in background save snapshot.");
    foreach(var lang in Strings.AvailableLanguages){Strings.SetLanguage(lang.Code);Check(restored.Items.Single().Text==original,"Locale change must never alter stored clipboard content.");}
    restored.Save();var reloaded=new HistoryStore(directory);reloaded.Load();
    Check(reloaded.Items.Single().Text==original&&reloaded.Settings.Language=="ar","Encrypted history and selected locale survive restart.");
}
finally {Directory.Delete(directory,true);Strings.SetLanguage("en");}
foreach(var failure in failures)Console.Error.WriteLine("FAIL "+failure);
Console.WriteLine($"Localization validation: {Strings.AvailableLanguages.Count*source.Count} strings; {failures.Count} failures. Synthetic history only.");
return failures.Count==0?0:1;
