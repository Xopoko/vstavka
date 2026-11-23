# Vstavka localization

The application includes **24 locales**, with additions chosen for the Windows desktop audience. This is a practical initial set, not a measured ranking by worldwide population or prospective sales. English is the source catalog, the default for new settings, and the fallback. The language picker starts with English; all other languages follow alphabetically by their English names. Russian is an ordinary entry in that order.

| Catalog | Language | Picker label | Direction |
| --- | --- | --- | --- |
| `en` | English | English | LTR |
| `ar` | Arabic | العربية | RTL |
| `bn` | Bengali | বাংলা | LTR |
| `zh-Hans` | Chinese, Simplified | 简体中文 | LTR |
| `zh-Hant` | Chinese, Traditional | 繁體中文 | LTR |
| `cs` | Czech | Čeština | LTR |
| `fr` | French | Français | LTR |
| `de` | German | Deutsch | LTR |
| `hi` | Hindi | हिन्दी | LTR |
| `hu` | Hungarian | Magyar | LTR |
| `id` | Indonesian | Bahasa Indonesia | LTR |
| `it` | Italian | Italiano | LTR |
| `ja` | Japanese | 日本語 | LTR |
| `ko` | Korean | 한국어 | LTR |
| `fa` | Persian | فارسی | RTL |
| `pl` | Polish | Polski | LTR |
| `pt-BR` | Portuguese, Brazil | Português (Brasil) | LTR |
| `ru` | Russian | Русский | LTR |
| `es` | Spanish | Español | LTR |
| `th` | Thai | ไทย | LTR |
| `tr` | Turkish | Türkçe | LTR |
| `uk` | Ukrainian | Українська | LTR |
| `ur` | Urdu | اردو | RTL |
| `vi` | Vietnamese | Tiếng Việt | LTR |

## Catalog and fallback contract

`src/Vstavka.Core/Localization/en.json` defines the UI strings. Each UTF-8 JSON catalog uses the same keys, formatting placeholders, and required line breaks. The catalogs are embedded in `Vstavka.Core.dll` and checked by `Vstavka.Localization.Tests`.

`Strings.AvailableLanguages` owns language codes, native labels, and ordering. An unknown saved language resolves to English. A missing catalog, key, or empty translated value falls back to the English catalog; an unknown key absent from English is returned as its key. All shipped catalogs must nevertheless pass full key and placeholder parity rather than relying on fallback to hide incomplete translations.

Language changes in Settings apply on Save. The first-run language picker updates the onboarding text immediately. Saving settings persists the selection locally. Choosing a language does not change Windows language preferences.

## Text direction and clipboard content

Arabic, Persian, and Urdu use right-to-left window layout. Language names and shortcut controls retain a stable left-to-right presentation; URLs, paths, and color codes keep their original order. Text-card direction follows its content independently of the selected interface language.

Only application text is translated. Stored clipboard text, links, file names, file paths, images, and source application names are not translated or rewritten when the language changes. Existing history remains the user's original content.

## Package preparation and review limits

The MSIX manifest declares the same 24 locales in the same order. Runtime catalog `en` maps to manifest/default PRI language `en-US`; other codes remain unchanged. Package display name and description use the English baseline **Vstavka**. All languages ship in the main package, with no separate resource-pack download.

Automated checks cover catalog structure and layout. Native-speaker review has not been recorded. The [Store listing](../store/listing.en-US.json) is maintained in English.
