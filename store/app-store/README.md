# Mac App Store listing

Edit the listing here before copying the changes to App Store Connect.

## Layout

`metadata/<locale>/` holds one folder per App Store localization:

| File | Field | Limit |
| --- | --- | --- |
| `name.txt` | Name | 30 |
| `subtitle.txt` | Subtitle | 30 |
| `keywords.txt` | Keywords separated by commas | 100 |
| `promotional_text.txt` | Promotional text | 170 |
| `description.txt` | Description | 4000 |

`screenshots/en-US/` holds the screenshots in page order, 2880 × 1800. Locales
without their own screenshots show these.

## Rules for the texts

- The Mac App Store build puts the chosen item on the clipboard for manual
  paste with Command–V. Clipboard sounds are a Windows feature.
- No other platforms, prices, competitor names or claims the app cannot back.
- Name: `Vstavka: <local word for clipboard manager>`. The subtitle is a phrase
  with search words that are not in the name. Keywords repeat neither.
- Use the wording of the interface catalog in
  `src/Vstavka.Core/Localization/<language>.json` for buttons and settings.

## Storefront languages

The English (U.K.) keywords include Russian terms for storefronts such as
Kazakhstan and Belarus. Locales also available in the U.S. storefront include
some complementary English keywords. [Apple's storefront language table](https://developer.apple.com/help/app-store-connect/reference/app-information/app-store-localizations)

## When a change reaches customers

Promotional text can be updated without a new submission. Other changes depend
on the field and version status. [Editable fields in App Store Connect](https://developer.apple.com/help/app-store-connect/reference/app-information/required-localizable-and-editable-properties)

## Search API baseline

These texts were prepared on 2026-11-23 before the first release. Two weeks
after release, look for app ID `6838294627` in the first 100 Mac app results to
record a baseline for comparison. [U.S. search API example](https://itunes.apple.com/search?term=clipboard+manager&country=us&entity=macSoftware&limit=100)

Terms to follow: `clipboard manager`, `clipboard history`, `copy paste`,
`pasteboard` (us, gb); `буфер обмена`, `менеджер буфера обмена` (ru, kz);
`zwischenablage` (de); `presse-papiers` (fr); `portapapeles` (es, mx);
`área de transferência` (br); `クリップボード` (jp); `剪贴板` (cn); `클립보드` (kr).

The translations were not reviewed by native speakers.
