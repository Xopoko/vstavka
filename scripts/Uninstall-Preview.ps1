[CmdletBinding(SupportsShouldProcess)]
param()
$ErrorActionPreference='Stop'
$programsRoot=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs'))
$target=[IO.Path]::GetFullPath((Join-Path $programsRoot 'Vstavka'))
if(-not $target.StartsWith($programsRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw 'Unsafe uninstall target.'}
if(Test-Path -LiteralPath $target) {
    if((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Install directory is a reparse point; stop for manual inspection.'}
    if(Get-ChildItem -LiteralPath $target -Recurse -Force -Attributes ReparsePoint){throw 'Install directory contains a reparse point; stop for manual inspection.'}
}
$appPath=Join-Path $target 'Vstavka.exe'
$running=Get-Process -Name Vstavka -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $appPath}
if($running){throw 'Exit Vstavka using its tray menu, then uninstall again.'}
if($PSCmdlet.ShouldProcess($target,'Remove Vstavka preview, its startup entry and shortcut; preserve encrypted history')) {
    $runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $runValue=(Get-ItemProperty -LiteralPath $runKey -Name Vstavka -ErrorAction SilentlyContinue).Vstavka
    if($runValue -eq ('"{0}" --background' -f $appPath)) {Remove-ItemProperty -LiteralPath $runKey -Name Vstavka}
    $shell=New-Object -ComObject WScript.Shell
    foreach($shortcutName in @('Vstavka.lnk','Вставка.lnk')) {
        $shortcut=Join-Path ([Environment]::GetFolderPath('Programs')) $shortcutName
        if((Test-Path -LiteralPath $shortcut) -and $shell.CreateShortcut($shortcut).TargetPath -eq $appPath) {Remove-Item -LiteralPath $shortcut}
    }
    $uninstallKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\VstavkaPreview'
    if(Test-Path -LiteralPath $uninstallKey){Remove-Item -LiteralPath $uninstallKey}
    if(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target -Recurse -Force}
    Write-Output 'Vstavka preview removed. Encrypted history retained in %LOCALAPPDATA%\Vstavka.'
}
