# Contributing

Build and test as described in the [README](README.md#building-from-source).
`scripts/Build-Preview.ps1` runs every test suite and the app's self-test; a
change is ready when that script passes.

Use synthetic data in tests, screenshots and issues. Never paste your real
clipboard history, a `history.vstavka` file, or logs with personal paths.

- **User-visible text** lives in `src/Vstavka.Core/Localization`. Add every new
  key to all 24 catalogs with a real translation; `Vstavka.Localization.Tests`
  checks keys, placeholders and line breaks.
- **UI changes:** run `Vstavka.exe --render-preview --output <folder>\preview.png`
  and look at the light, dark, high-contrast and right-to-left renders. Check
  keyboard focus and 125% scaling as well.
- **Clipboard and paste changes:** say which apps you tested with, and keep the
  privacy rules in [SECURITY.md](SECURITY.md) intact. Tests use recorded system
  boundaries instead of the real clipboard.
- **Store screenshots** are rendered by `tests/Vstavka.StoreScenes`; see its
  [README](tests/Vstavka.StoreScenes/README.md).

For a manual pass before a release, use the
[acceptance checklist](docs/acceptance.md).
