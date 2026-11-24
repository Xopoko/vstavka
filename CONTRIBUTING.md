# Contributing

Build as described in the [README](README.md#building-from-source). The
Microsoft Store uses the same app with a Windows packaging profile. See the
[MSIX instructions](packaging/README.md).

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
| `gnome-extension/tests/run-headless.sh` | The GNOME Shell extension in a headless GNOME Shell 46 session | Linux with Docker |

Use synthetic data in tests, screenshots and issues. Never paste your real
clipboard history, a `history.vstavka` file, or logs with personal paths.

- **User-visible text** lives in `src/Vstavka.Core/Localization`. Add every new
  key to all 24 catalogs with a real translation; `Vstavka.Localization.Tests`
  checks keys, placeholders and line breaks.
- **UI changes** follow [DESIGN.md](DESIGN.md): a view takes its colours,
  sizes, distances and radii from the roles described there and writes none
  of its own. Check the light, dark and right-to-left renders, plus keyboard
  focus and scaling. Run
  `dotnet run --project tests/Vstavka.Desktop.Tests -- --render <folder>`.
- **Motion** goes through `DesktopMotion`: one scale of durations, one curve
  and the few shared effects. Animate a change only when the movement explains
  it: where something came from, where it went, that an action was heard.
  Whatever repeats all day stays instant, and nothing the user asked for waits
  for an animation. Check the result with system animations turned off too.
- **Clipboard and paste changes:** say which systems and apps you tested with,
  and keep the privacy rules in [SECURITY.md](SECURITY.md) intact. Tests use
  recorded system boundaries instead of the real clipboard.
- **README images** in `docs/images` are rendered from the cross-platform app
  with `dotnet run --project tests/Vstavka.Desktop.Tests -- --readme docs/images`.
  The GIF needs `ffmpeg` on the `PATH` and system animations turned on. Run it
  on a Mac: the images show macOS, and the desktop picture uses SF Pro when
  the font is installed.

For a manual pass before a release, use the
[acceptance checklist](docs/acceptance.md).
