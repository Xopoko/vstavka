# MSIX preparation

`Package-Store.ps1` creates an unsigned MSIX for validation or submission to
Microsoft Store. Partner Center signs the package during publication.

## Build input

Publish the Avalonia app as a self-contained `win-x64` build with the Windows Store settings. Run these commands from the repository root:

```powershell
dotnet publish src/Vstavka.Desktop -c Release -r win-x64 --self-contained true -p:VstavkaWindowsStore=true -o artifacts/windows-store
pwsh -File ./scripts/Package-Store.ps1 -PublishDirectory ./artifacts/windows-store -LocalValidation
```

Use the existing Store identity to upgrade the WPF release. The package retains
the application ID, executable name and startup task. It opens the existing
DPAPI-encrypted history and keeps the saved shortcut and clipboard consent.

Local validation uses the placeholder identity `Vstavka.LocalPreview` and
publisher `CN=VstavkaLocalPreview`.

For a Store submission, pass `-IdentityName`, `-Publisher` and
`-PublisherDisplayName` exactly as shown under **Partner Center → Product
identity**. Pass a `-PackageVersion` higher than the Store package version, with
four parts and a zero in the fourth part. [Microsoft package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)

Each invocation writes the MSIX, payload, MakeAppx log and package and file
hashes to a fresh directory under `artifacts/packages`. Set `-OutputDirectory`
or `-WindowsSdkVersion` to use a different artifact directory or SDK.

The packager compares the published product version and embedded JSON catalogs with the project files. The receipt records the binary version, catalog hashes, and packaged language declarations.

## Languages

The package display name is **Vstavka**. English is the baseline for package descriptions and MakePri (`en-US`). The packager generates the language declarations from `Strings.AvailableLanguages` in the same order and maps `en` to `en-US`. It reads the declarations back from the completed MSIX.

All 24 locales ship in `Vstavka.Core.dll` and are available in Settings. See the
[localization notes](../docs/localization.md) for the catalog list.

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

## Manifest and startup

The manifest exposes `Vstavka.exe` on Windows 11 x64 and declares
`runFullTrust` for the desktop process. [Capability semantics](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)

`VstavkaStartup` is disabled by default. The user can enable it from the
first-run guide or Settings. Startup activation passes `--startup` and leaves
the app in the tray. Vstavka respects startup disabled in Windows Settings.
[Startup extension](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions)
