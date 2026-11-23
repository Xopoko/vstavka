# MSIX preparation

This folder prepares a Microsoft Store hosted MSIX for the native WPF app. Packaging is local: the script does not sign, create certificates, change trust, install, or upload anything.

Pass a `-PackageVersion` higher than the version currently in the Store. Store package versions use a nonzero first component and reserve the fourth component. [Microsoft package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)

## Build input

Use the application publishing workflow to produce a Release, self-contained `win-x64` directory. Then invoke from the repository root:

```powershell
pwsh -File ./scripts/Package-Store.ps1 -PublishDirectory ./artifacts/preview -LocalValidation
```

The validation package uses `Vstavka.LocalPreview` / `CN=VstavkaLocalPreview`. These are clearly local placeholders, **not a Microsoft Store reservation**. The unsigned file cannot be treated as an ordinary trusted installable release.

For a Store submission, the same script accepts the three identity fields as arguments: `-IdentityName`, `-Publisher`, `-PublisherDisplayName`. Pass the exact case-sensitive values from **Partner Center → Product identity**. The script preserves them using XML APIs. It does not claim that provided values have been verified against Partner Center. Never substitute an inferred publisher or name. [Identity and packaging](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion)

Every invocation uses a fresh directory under `artifacts/packages`, retains the package payload and MakeAppx log for inspection, reads back the packaged manifest, and records the package hash and per-entry hashes. It does not overwrite earlier runs or publish/build the app itself. Symbols (`.pdb`) and the two portable-only helpers (`Install-Preview.ps1`, `Uninstall-Preview.ps1`) are left outside the customer package. Set `-OutputDirectory` or `-WindowsSdkVersion` explicitly when necessary.

The packager compares the published product version and embedded JSON catalogs with the project files. The receipt records the binary version, catalog hashes, and packaged language declarations.

## Languages

The package display name is **Vstavka**. English is the baseline for package descriptions and MakePri (`en-US`). The packager generates the language declarations from `Strings.AvailableLanguages` in the same order and maps `en` to `en-US`. It reads the declarations back from the completed MSIX.

The JSON catalogs are embedded in `Vstavka.Core.dll`; they do not depend on Windows selecting a separate language pack. Every customer receives all 24 locales and can change the app language in Settings. Store listing translations are a separate preparation step and are not implied by the application catalogs. See `docs/localization.md` for the language list and translation review limits.

## Package assets

The six required PNGs belong in `packaging/Assets`:

| Filename | Exact size | Use |
| --- | --- | --- |
| `StoreLogo.png` | 50 × 50 | Package property logo |
| `Square44x44Logo.png` | 44 × 44 | Application icon |
| `Square150x150Logo.png` | 150 × 150 | Application tile |
| `StoreLogo.scale-200.png` | 100 × 100 | Package logo at 200% |
| `Square44x44Logo.scale-200.png` | 88 × 88 | Application icon at 200% |
| `Square150x150Logo.scale-200.png` | 300 × 300 | Application tile at 200% |

Use original app artwork. The packager validates their exact dimensions, includes all six assets and generates `resources.pri` using the SDK MakePri tool and `priconfig.xml`. Scale variants remain in the same package. [Manual MSIX assets and PRI](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion)

## Runtime contract

One visible app: `Vstavka`, executable `Vstavka.exe`; target Windows 11 Desktop x64 (`MinVersion=10.0.22000.0`). One restricted capability: `runFullTrust`, required by the desktop process. No elevation, services, installer custom actions, execution aliases, or custom non-user-configurable startup capability are declared. Full trust means a normal desktop process, so absence of network capabilities is **not** network isolation; the app's implementation must keep the no-network promise. [Capability semantics](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)

`VstavkaStartup` starts with `Enabled=false`. The application requests enablement only after the first-run startup option is accepted. Startup activation passes `--startup` so the app can remain in the tray. Windows can disable startup; app code must respect `DisabledByUser` and must not override that setting. [Startup extension](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions), [extension parameters](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop-extension)
