# Store screenshot scenes

This development-only harness hosts the real WPF `RibbonWindow.Content` and
`SettingsWindow.Content` from the current `src/Vstavka` build. It does not call
`Vstavka.App.OnStartup`, create clipboard/history services, install keyboard
hooks, alter startup settings, inspect the desktop, or read user data. The
in-memory history, collections, file names, and landscape are synthetic. Scenes use the
English UI and the fixed blue accent, so the result does not depend on the
Windows accent of the machine that renders them.

| Scene | File | Content |
| --- | --- | --- |
| 1 | `01-history-light.png` | Light history with "Prompts" and "Work" collection chips |
| 2 | `02-history-dark.png` | The same history and collections in the dark theme |
| 3 | `03-prompts-collection.png` | "Prompts" selected, with three prompt cards |
| 4 | `04-settings.png` | Light settings, centered |

Build and render the Store screenshots from the repository root:

```powershell
dotnet build tests/Vstavka.StoreScenes/Vstavka.StoreScenes.csproj -c Release
& tests/Vstavka.StoreScenes/bin/Release/net10.0-windows10.0.19041.0/Vstavka.StoreScenes.exe --render store/screenshots --output store/screenshots/capture-receipt.json
```

`--render` lays out each 1560 × 900 DIP scene offscreen and saves it at two
device pixels per DIP (3120 × 1800 PNG). No window is shown. The record lists
the app version, size, and SHA-256 of every file. Timestamps on the cards use
today's date, so hashes change from day to day.

`--verify --output <json>` checks layout and card counts only and refreshes
[verification.json](verification.json).

Without these flags the harness opens a borderless window titled
**Vstavka Store capture** for manual inspection: F1–F4 switch scenes, Escape exits.
No synthetic content is ever placed on the real clipboard, and the view buttons
have no application service subscribers.
