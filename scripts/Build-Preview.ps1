[CmdletBinding()]
param([switch]$SkipTests)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $projectRoot
try {
    if(-not $SkipTests) {
        foreach($suite in @('Core','Localization','Audio','Clipboard')) {
            & dotnet run --project "tests/Vstavka.$suite.Tests/Vstavka.$suite.Tests.csproj" -c Release
            if($LASTEXITCODE -ne 0){throw "$suite tests failed."}
        }
    }
    & dotnet publish src/Vstavka/Vstavka.csproj -c Release -r win-x64 --self-contained true -o artifacts/preview -p:DebugType=None -p:DebugSymbols=false
    if($LASTEXITCODE -ne 0){throw 'Publish failed.'}
    $exe=Join-Path $projectRoot 'artifacts\preview\Vstavka.exe'
    $checkOutput=New-TemporaryFile
    try {
        $test=Start-Process -FilePath $exe -ArgumentList @('--self-test','--output',('"{0}"' -f $checkOutput.FullName)) -WindowStyle Hidden -PassThru -Wait
        $checks=Get-Content -LiteralPath $checkOutput.FullName -Raw | ConvertFrom-Json
        if(-not $checks.checks) {throw "App checks produced no report (exit $($test.ExitCode))."}
        $checkReport=Join-Path $projectRoot 'artifacts\app-checks.json'
        Copy-Item -LiteralPath $checkOutput.FullName -Destination $checkReport -Force
        if($test.ExitCode -ne 0 -or $checks.passed -ne $true) {
            throw "App checks failed (exit $($test.ExitCode)). See $checkReport."
        }
    } finally {
        Remove-Item -LiteralPath $checkOutput.FullName
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-Preview.ps1') -Destination (Join-Path $projectRoot 'artifacts\preview\Install-Preview.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall-Preview.ps1') -Destination (Join-Path $projectRoot 'artifacts\preview\Uninstall-Preview.ps1') -Force
    $version=([xml](Get-Content -LiteralPath 'src/Vstavka/Vstavka.csproj' -Raw)).Project.PropertyGroup.Version
    $receipt=[ordered]@{product='Vstavka';version=($version+'-preview');architecture='x64';framework='self-contained .NET 10';exeSha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash;appSha256=(Get-FileHash -LiteralPath 'artifacts/preview/Vstavka.dll' -Algorithm SHA256).Hash;coreSha256=(Get-FileHash -LiteralPath 'artifacts/preview/Vstavka.Core.dll' -Algorithm SHA256).Hash;testSuitesRun=(-not $SkipTests);phase='prepared';storeUploaded=$false;builtAt=[DateTimeOffset]::UtcNow.ToString('o')}
    $receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts\preview-receipt.json') -Encoding utf8
    Write-Output ('Preview ready: '+$exe)
} finally {Pop-Location}
