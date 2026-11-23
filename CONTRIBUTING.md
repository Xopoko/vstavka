# Contributing

Build as described in the [README](README.md#building-from-source). The
Microsoft Store app (`src/Vstavka`) builds only on Windows; the cross-platform
app (`src/Vstavka.Desktop`) and its tests build on Windows, macOS and Linux.

Each test suite is a console app: `dotnet run --project tests/<suite>`. Run the
suites your change touches; CI runs all of them on every push and pull request.

| Suite | What it checks | Where it runs |
| --- | --- | --- |
| `Vstavka.Core.Tests` | History, search, storage, settings and shortcuts | Any system |
| `Vstavka.Localization.Tests` | Keys, placeholders and line breaks in all 24 catalogs | Any system |
| `Vstavka.Desktop.Tests` | Cross-platform panel, first-run guide and settings, headless; add `-- --render <folder>` to save PNGs | Any system |
| `Vstavka.Platform.MacOS.Tests` | macOS adapter logic with fakes | Any system |
| `Vstavka.Platform.Linux.Tests` | Linux adapter and GNOME helper setup with fakes | Any system |
| `Vstavka.Platform.Windows.Tests` | Windows adapter | Windows |
| `Vstavka.Clipboard.Tests`, `Vstavka.Audio.Tests` | Store app clipboard capture and sounds | Windows |
| `gnome-extension/tests/run-headless.sh` | The GNOME Shell extension in a headless GNOME Shell 46 session | Linux with Docker |

For the Store app, `scripts/Build-Preview.ps1` runs its suites and the app's
self-test; CI also checks the Store screenshot harness.

Use synthetic data in tests, screenshots and issues. Never paste your real
clipboard history, a `history.vstavka` file, or logs with personal paths.

- **User-visible text** lives in `src/Vstavka.Core/Localization`. Add every new
  key to all 24 catalogs with a real translation; `Vstavka.Localization.Tests`
  checks keys, placeholders and line breaks.
- **UI changes:** look at the light, dark and right-to-left renders. For the
  cross-platform app, run
  `dotnet run --project tests/Vstavka.Desktop.Tests -- --render <folder>`; for
  the Store app, run `Vstavka.exe --render-preview --output <folder>\preview.png`
  and check high contrast too. Check keyboard focus and 125% scaling as well.
- **Clipboard and paste changes:** say which systems and apps you tested with,
  and keep the privacy rules in [SECURITY.md](SECURITY.md) intact. Tests use
  recorded system boundaries instead of the real clipboard.
- **README images** in `docs/images` are rendered from the cross-platform app
  with `dotnet run --project tests/Vstavka.Desktop.Tests -- --readme docs/images`.
  The GIF needs `ffmpeg` on the `PATH` and system animations turned on.
- **Store screenshots** are rendered by `tests/Vstavka.StoreScenes`; see its
  [README](tests/Vstavka.StoreScenes/README.md).

For a manual pass before a release, use the
[acceptance checklist](docs/acceptance.md).
