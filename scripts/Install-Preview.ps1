[CmdletBinding(SupportsShouldProcess)]
param([string]$SourceDirectory)
$ErrorActionPreference='Stop'
if(-not $SourceDirectory) {
    if(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Vstavka.exe')) {$SourceDirectory=$PSScriptRoot}
    else {$SourceDirectory=Join-Path $PSScriptRoot '..\artifacts\preview'}
}
$source=[IO.Path]::GetFullPath($SourceDirectory)
$appFiles=@('Vstavka.exe','Vstavka.dll','Vstavka.Core.dll')
foreach($name in $appFiles) {
    if(-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) {throw "Missing $name. Build the preview first."}
}
$target=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\Vstavka'))
if($source.Equals($target,[StringComparison]::OrdinalIgnoreCase)){throw 'Source is already the installed directory.'}
if(Test-Path -LiteralPath $target) {if((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Install directory is a reparse point; stop for manual inspection.'}}
$running=Get-Process -Name Vstavka -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq (Join-Path $target 'Vstavka.exe')}
if($running){throw 'Exit Vstavka using its tray menu, then install again.'}
if($PSCmdlet.ShouldProcess($target,'Install Vstavka preview for current user and add Start menu shortcut')) {
    [IO.Directory]::CreateDirectory($target) | Out-Null
    Get-ChildItem -LiteralPath $source | Copy-Item -Destination $target -Recurse -Force
    foreach($name in $appFiles) {
        if((Get-FileHash -LiteralPath (Join-Path $target $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $source $name)).Hash) {
            throw "Installed file differs from build: $name"
        }
    }
    $uninstaller=Join-Path $target 'Uninstall-Preview.ps1'
    if(-not (Test-Path -LiteralPath $uninstaller)) {Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall-Preview.ps1') -Destination $uninstaller}
    # Windows PowerShell 5.1 needs a BOM to preserve the Russian shortcut name.
    [IO.File]::WriteAllText($uninstaller,[IO.File]::ReadAllText($uninstaller),[Text.UTF8Encoding]::new($true))
    $shortcutPath=Join-Path ([Environment]::GetFolderPath('Programs')) 'Vstavka.lnk'
    $shell=New-Object -ComObject WScript.Shell
    $oldShortcutPath=Join-Path ([Environment]::GetFolderPath('Programs')) 'Вставка.lnk'
    if(Test-Path -LiteralPath $oldShortcutPath) {
        $oldShortcut=$shell.CreateShortcut($oldShortcutPath)
        if($oldShortcut.TargetPath -eq (Join-Path $target 'Vstavka.exe')) {Remove-Item -LiteralPath $oldShortcutPath}
    }
    $shortcut=$shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath=Join-Path $target 'Vstavka.exe';$shortcut.WorkingDirectory=$target;$shortcut.Description='Vstavka clipboard history';$shortcut.Save()
    $uninstallKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\VstavkaPreview'
    New-Item -Path $uninstallKey -Force | Out-Null
    $version=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $target 'Vstavka.dll')).ProductVersion.Split('+')[0]
    $properties=@{DisplayName='Vstavka (Preview)';DisplayVersion=$version;InstallLocation=$target;DisplayIcon=(Join-Path $target 'Vstavka.exe');UninstallString=('powershell.exe -NoProfile -File "'+$uninstaller+'"')}
    foreach($entry in $properties.GetEnumerator()) {New-ItemProperty -LiteralPath $uninstallKey -Name $entry.Key -Value $entry.Value -PropertyType String -Force | Out-Null}
    New-ItemProperty -LiteralPath $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -LiteralPath $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
    Write-Output ('Installed: '+(Join-Path $target 'Vstavka.exe'))
}
