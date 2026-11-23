# Mac App Store listing

The texts and screenshots of the Mac App Store page. App Store Connect is filled
from these files, so edit them here first.

## Layout

`metadata/<locale>/` holds one folder per App Store localization:

| File | Field | Limit |
| --- | --- | --- |
| `name.txt` | Name | 30 |
| `subtitle.txt` | Subtitle | 30 |
| `keywords.txt` | Keywords, comma-separated without spaces | 100 |
| `promotional_text.txt` | Promotional text | 170 |
| `description.txt` | Description | 4000 |

`screenshots/en-US/` holds the screenshots in page order, 2880 × 1800. Locales
without their own screenshots show these.

## Rules for the texts

- Describe the Mac App Store build. It puts the chosen item on the clipboard and
  the user pastes with Command–V; it does not paste by itself. Collections and
  sounds are not in this app.
- No other platforms, prices, competitor names or claims the app cannot back.
- Name: `Vstavka: <local word for clipboard manager>`. The subtitle is a phrase
  with search words that are not in the name. Keywords repeat neither.
- Use the wording of the interface catalog in
  `src/Vstavka.Core/Localization/<language>.json` for buttons and settings.
- Search reads the name, subtitle and keywords of one localization at a time, so
  the words of a phrase must sit in the same localization.

## Which store reads which localization

- English (U.K.) is the page for the United Kingdom and for most countries
  without their own language here, including Kazakhstan, Belarus and their
  neighbors. Its keywords therefore carry Russian search words.
- Russia and Ukraine read Russian, Ukrainian and English (U.K.).
- The United States also reads Arabic, both Chinese, French, Korean, Portuguese
  (Brazil), Russian, Spanish (Mexico) and Vietnamese. Their keywords end with
  English words that are not in the English (U.S.) keywords.

Apple's current table:
<https://developer.apple.com/help/app-store-connect/reference/app-information/app-store-localizations>

## When a change reaches customers

- Promotional text: at once, on the live version too.
- Everything else: with the next version, after App Review.
- While a version waits for review, App Store Connect accepts text changes in
  localizations that already exist, but refuses new localizations and new
  screenshots.

## Search positions

These texts were written on 2026-11-23, before the first release, so the
starting point is "not listed". Two weeks after a release, look for app ID
`6838294627` in the first 100 results of the public search API, for example
<https://itunes.apple.com/search?term=clipboard+manager&country=us&entity=macSoftware&limit=100>.

Terms to follow: `clipboard manager`, `clipboard history`, `copy paste`,
`pasteboard` (us, gb); `буфер обмена`, `менеджер буфера обмена` (ru, kz);
`zwischenablage` (de); `presse-papiers` (fr); `portapapeles` (es, mx);
`área de transferência` (br); `クリップボード` (jp); `剪贴板` (cn); `클립보드` (kr).

The translations were not reviewed by native speakers.
