#Requires -Version 7.0
[CmdletBinding(DefaultParameterSetName = 'LocalValidation')]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $PublishDirectory,

    [Parameter(ParameterSetName = 'LocalValidation')]
    [switch] $LocalValidation,

    [Parameter(Mandatory, ParameterSetName = 'StoreIdentity')]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]{2,49}$')]
    [string] $IdentityName,

    [Parameter(Mandatory, ParameterSetName = 'StoreIdentity')]
    [ValidateNotNullOrEmpty()]
    [string] $Publisher,

    [Parameter(Mandatory, ParameterSetName = 'StoreIdentity')]
    [ValidateNotNullOrEmpty()]
    [string] $PublisherDisplayName,

    [ValidatePattern('^\d+\.\d+\.\d+\.0$')]
    [string] $PackageVersion = '1.1.4.0',

    [ValidateSet('Wpf', 'Avalonia')]
    [string] $Application = 'Wpf',

    [string] $OutputDirectory,

    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string] $WindowsSdkVersion = '10.0.26100.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Invoke-HiddenNative {
    param([string] $FilePath, [string[]] $Arguments)
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void]$startInfo.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult() }
    } finally { $process.Dispose() }
}

function Test-WindowsStoreBuild {
    param([string] $AssemblyPath)
    $stream = [IO.File]::OpenRead($AssemblyPath)
    $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        foreach ($handle in $metadata.GetAssemblyDefinition().GetCustomAttributes()) {
            $attribute = $metadata.GetCustomAttribute($handle)
            if ($attribute.Constructor.Kind -ne [Reflection.Metadata.HandleKind]::MemberReference) { continue }
            $row = [Reflection.Metadata.Ecma335.MetadataTokens]::GetRowNumber($attribute.Constructor)
            $constructor = $metadata.GetMemberReference([Reflection.Metadata.Ecma335.MetadataTokens]::MemberReferenceHandle($row))
            if ($constructor.Parent.Kind -ne [Reflection.Metadata.HandleKind]::TypeReference) { continue }
            $parentRow = [Reflection.Metadata.Ecma335.MetadataTokens]::GetRowNumber($constructor.Parent)
            $type = $metadata.GetTypeReference([Reflection.Metadata.Ecma335.MetadataTokens]::TypeReferenceHandle($parentRow))
            if ($metadata.GetString($type.Namespace) -cne 'System.Reflection' -or
                $metadata.GetString($type.Name) -cne 'AssemblyMetadataAttribute') { continue }
            $reader = $metadata.GetBlobReader($attribute.Value)
            if ($reader.ReadUInt16() -eq 1 -and $reader.ReadSerializedString() -ceq 'VstavkaDistribution' -and
                $reader.ReadSerializedString() -ceq 'WindowsStore') { return $true }
        }
        return $false
    } finally { $pe.Dispose(); $stream.Dispose() }
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
if (-not (Test-Path -LiteralPath $publishRoot -PathType Container)) {
    throw 'PublishDirectory must be a self-contained win-x64 publish directory.'
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'artifacts/packages' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$publishPrefix = $publishRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($outputRoot.Equals($publishRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $outputRoot.StartsWith($publishPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be outside PublishDirectory.'
}
$version = [Version]::Parse($PackageVersion)
if ($version.Major -eq 0 -or $version.Major -gt 65535 -or $version.Minor -gt 65535 -or $version.Build -gt 65535) {
    throw 'Store package version needs a nonzero major, components at most 65535, and final component 0.'
}

$identityScope = 'local_validation_only'
if ($PSCmdlet.ParameterSetName -eq 'LocalValidation') {
    $IdentityName = 'Vstavka.LocalPreview'
    $Publisher = 'CN=VstavkaLocalPreview'
    $PublisherDisplayName = 'Local preview'
} else {
    if ($IdentityName -eq 'Vstavka.LocalPreview' -or $Publisher -eq 'CN=VstavkaLocalPreview') {
        throw 'The local validation identity cannot be used as a Store identity.'
    }
    $identityScope = 'provided_partner_center_identity_not_verified'
}

$makeAppx = Join-Path ${env:ProgramFiles(x86)} "Windows Kits/10/bin/$WindowsSdkVersion/x64/makeappx.exe"
$makePri = Join-Path ${env:ProgramFiles(x86)} "Windows Kits/10/bin/$WindowsSdkVersion/x64/makepri.exe"
if (-not (Test-Path -LiteralPath $makeAppx -PathType Leaf)) {
    throw "Windows SDK MakeAppx was not found at the configured version: $WindowsSdkVersion. No tools were installed."
}
if (-not (Test-Path -LiteralPath $makePri -PathType Leaf)) {
    throw "Windows SDK MakePri was not found at the configured version: $WindowsSdkVersion. No tools were installed."
}
$entryName = 'Vstavka'
$uiLibraries = @('PresentationFramework.dll')
if ($Application -eq 'Avalonia') {
    $entryName = 'Vstavka.Desktop'
    $uiLibraries = @('Avalonia.Controls.dll', 'Vstavka.Platform.Windows.dll', 'libSkiaSharp.dll')
}
$requiredPayload = @("$entryName.exe", "$entryName.dll", 'Vstavka.Core.dll',
    "$entryName.runtimeconfig.json", "$entryName.deps.json", 'coreclr.dll', 'hostfxr.dll',
    'hostpolicy.dll', 'System.Private.CoreLib.dll') + $uiLibraries
foreach ($name in $requiredPayload) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishRoot $name) -PathType Leaf)) {
        throw "Missing self-contained $Application payload: $name. Publish Release with -r win-x64 --self-contained true."
    }
}

# Bind the candidate's product version and embedded translations to the current source.
# PE metadata is inspected without loading or executing the application assembly.
[xml] $appProject = Get-Content -LiteralPath (Join-Path $projectRoot "src/$entryName/$entryName.csproj") -Raw
$expectedProductVersion = $appProject.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$appAssembly = Join-Path $publishRoot "$entryName.dll"
if ($Application -eq 'Avalonia' -and -not (Test-WindowsStoreBuild -AssemblyPath $appAssembly)) {
    throw 'This Avalonia build is missing the Windows Store configuration. Publish with -p:VstavkaWindowsStore=true.'
}
$productVersion = (Get-Item -LiteralPath $appAssembly).VersionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($productVersion) -or $productVersion.Split('+')[0] -cne $expectedProductVersion) {
    throw "The published product version does not match source $expectedProductVersion. Rebuild the preview before packaging."
}
$localizationRoot = Join-Path $projectRoot 'src/Vstavka.Core/Localization'
$stringsSource = Get-Content -LiteralPath (Join-Path $localizationRoot 'Strings.cs') -Raw
$languageCodes = @([regex]::Matches($stringsSource, 'new\s+LanguageOption\s*\(\s*"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
if ($languageCodes.Count -eq 0 -or $languageCodes[0] -cne 'en' -or @($languageCodes | Select-Object -Unique).Count -ne $languageCodes.Count) {
    throw 'Expected distinct runtime languages with English first. Review the package language contract.'
}
$expectedManifestLanguages = @($languageCodes | ForEach-Object { if ($_ -eq 'en') { 'en-US' } else { $_ } })
$coreStream = [IO.File]::OpenRead((Join-Path $publishRoot 'Vstavka.Core.dll'))
$coreReader = [Reflection.PortableExecutable.PEReader]::new($coreStream)
try {
    $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($coreReader)
    $resourceSection = $coreReader.GetSectionData($coreReader.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress)
    [byte[]] $resourceBytes = $resourceSection.GetContent()
    $embeddedResources = @{}
    foreach ($handle in $metadata.ManifestResources) {
        $resource = $metadata.GetManifestResource($handle)
        $resourceName = $metadata.GetString($resource.Name)
        if ($resourceName -notlike 'Vstavka.Core.Localization.*.json') { continue }
        if (-not $resource.Implementation.IsNil) { throw "Localization must be embedded: $resourceName." }
        $offset = [int]$resource.Offset
        if ($offset -lt 0 -or $offset -gt $resourceBytes.Length - 4) { throw "Invalid resource offset: $resourceName." }
        $length = [BitConverter]::ToInt32($resourceBytes, $offset)
        if ($length -lt 1 -or $length -gt $resourceBytes.Length - $offset - 4) { throw "Invalid resource length: $resourceName." }
        $catalogBytes = [byte[]]::new($length)
        [Array]::Copy($resourceBytes, $offset + 4, $catalogBytes, 0, $length)
        $embeddedResources[$resourceName] = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($catalogBytes)).ToLowerInvariant()
    }
    if ($embeddedResources.Count -ne $languageCodes.Count) { throw 'Published localization count does not match the runtime list. Rebuild the preview.' }
    $localizationCatalogs = @($languageCodes | ForEach-Object {
        $catalogPath = Join-Path $localizationRoot "$_.json"
        $resourceName = "Vstavka.Core.Localization.$_.json"
        $catalogHash = (Get-FileHash -LiteralPath $catalogPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not $embeddedResources.ContainsKey($resourceName) -or $embeddedResources[$resourceName] -cne $catalogHash) {
            throw "Published localization differs from source: $_. Rebuild the preview."
        }
        [ordered]@{ language = $_; resource = $resourceName; sha256 = $catalogHash }
    })
} finally { $coreReader.Dispose(); $coreStream.Dispose() }

foreach ($name in @("$entryName.exe", 'coreclr.dll')) {
    $peReader = [IO.BinaryReader]::new([IO.File]::OpenRead((Join-Path $publishRoot $name)))
    try {
        if ($peReader.ReadUInt16() -ne 0x5A4D) { throw "Invalid Windows executable header: $name." }
        $peReader.BaseStream.Position = 0x3C
        $peOffset = $peReader.ReadInt32()
        if ($peOffset -lt 0 -or $peOffset -gt ($peReader.BaseStream.Length - 6)) { throw "Invalid PE offset: $name." }
        $peReader.BaseStream.Position = $peOffset
        if ($peReader.ReadUInt32() -ne 0x4550 -or $peReader.ReadUInt16() -ne 0x8664) {
            throw "The package is x64 but $name is not an x64 PE file. Publish with -r win-x64."
        }
    } finally { $peReader.Dispose() }
}
$templatePath = Join-Path $projectRoot 'packaging/AppxManifest.template.xml'
$assetsRoot = Join-Path $projectRoot 'packaging/Assets'
$packageAssets = @{
    'StoreLogo.png' = 50; 'Square44x44Logo.png' = 44; 'Square150x150Logo.png' = 150
    'StoreLogo.scale-200.png' = 100; 'Square44x44Logo.scale-200.png' = 88; 'Square150x150Logo.scale-200.png' = 300
}
foreach ($asset in $packageAssets.GetEnumerator()) {
    $name = $asset.Key
    $assetPath = Join-Path $assetsRoot $name
    if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
        throw "Missing package asset: $name. See packaging/README.md for dimensions."
    }
    $pngStream = [IO.File]::OpenRead($assetPath)
    try {
        $header = [byte[]]::new(24)
        if ($pngStream.Read($header, 0, 24) -ne 24 -or [Convert]::ToHexString($header[0..7]) -ne '89504E470D0A1A0A') {
            throw "Invalid PNG header: $name."
        }
        $width = [uint32]$header[16] * 16777216 + [uint32]$header[17] * 65536 + [uint32]$header[18] * 256 + $header[19]
        $height = [uint32]$header[20] * 16777216 + [uint32]$header[21] * 65536 + [uint32]$header[22] * 256 + $header[23]
        if ($width -ne $asset.Value -or $height -ne $asset.Value) {
            throw "Incorrect $name size: ${width}x${height}; expected $($asset.Value)x$($asset.Value)."
        }
    } finally { $pngStream.Dispose() }
}
$publishItems = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -Force)
if (@($publishItems | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0 -or
    ((Get-Item -LiteralPath $publishRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'PublishDirectory must not contain symbolic links or junctions.'
}

# Every run owns a fresh directory. No previous artifacts, installed apps, trust stores or Windows settings are changed.
$runName = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $outputRoot $runName
$stageRoot = Join-Path $runRoot 'payload'
[void][IO.Directory]::CreateDirectory($stageRoot)
foreach ($item in $publishItems | Where-Object {
    -not $_.PSIsContainer -and $_.Extension -ne '.pdb' -and $_.Name -notin @('Install-Preview.ps1', 'Uninstall-Preview.ps1')
}) {
    $relativePath = [IO.Path]::GetRelativePath($publishRoot, $item.FullName)
    if ($relativePath -in @('AppxManifest.xml', 'resources.pri') -or $relativePath -like 'Assets\*' -or
        ($Application -eq 'Avalonia' -and $relativePath -in @('Vstavka.exe', 'License.txt', 'ThirdPartyNotices.txt'))) {
        throw 'PublishDirectory contains packaging-owned files. Use a clean application publish directory.'
    }
    # Keep the installed Start menu and startup target stable across the WPF upgrade.
    if ($relativePath -ceq "$entryName.exe") { $relativePath = 'Vstavka.exe' }
    $targetPath = Join-Path $stageRoot $relativePath
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $targetPath))
    Copy-Item -LiteralPath $item.FullName -Destination $targetPath
}
if ($Application -eq 'Avalonia') {
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $stageRoot 'License.txt')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging/WindowsThirdPartyNotices.txt') `
        -Destination (Join-Path $stageRoot 'ThirdPartyNotices.txt')
}
$stageAssets = Join-Path $stageRoot 'Assets'
[void][IO.Directory]::CreateDirectory($stageAssets)
foreach ($name in $packageAssets.Keys) {
    Copy-Item -LiteralPath (Join-Path $assetsRoot $name) -Destination (Join-Path $stageAssets $name)
}

[xml] $manifest = Get-Content -LiteralPath $templatePath -Raw
$ns = [Xml.XmlNamespaceManager]::new($manifest.NameTable)
$ns.AddNamespace('m', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$resources = $manifest.SelectSingleNode('/m:Package/m:Resources', $ns)
foreach ($language in $expectedManifestLanguages) {
    $resource = $manifest.CreateElement('Resource', $resources.NamespaceURI)
    $resource.SetAttribute('Language', $language)
    [void]$resources.AppendChild($resource)
}
$identity = $manifest.SelectSingleNode('/m:Package/m:Identity', $ns)
$identity.SetAttribute('Name', $IdentityName)
$identity.SetAttribute('Publisher', $Publisher)
$identity.SetAttribute('Version', $PackageVersion)
$manifest.SelectSingleNode('/m:Package/m:Properties/m:PublisherDisplayName', $ns).InnerText = $PublisherDisplayName
$manifestPath = Join-Path $stageRoot 'AppxManifest.xml'
$xmlSettings = [Xml.XmlWriterSettings]::new()
$xmlSettings.Encoding = [Text.UTF8Encoding]::new($false)
$xmlSettings.Indent = $true
$xmlWriter = [Xml.XmlWriter]::Create($manifestPath, $xmlSettings)
try { $manifest.Save($xmlWriter) } finally { $xmlWriter.Dispose() }

$priConfigPath = Join-Path $projectRoot 'packaging/priconfig.xml'
[xml] $priConfig = Get-Content -LiteralPath $priConfigPath -Raw
$defaultLanguage = @($priConfig.resources.index.default.qualifier | Where-Object name -eq 'Language')[0].value
if ($defaultLanguage -cne 'en-US') { throw 'MakePri must use the English en-US default language.' }
# Only Windows shell logos use MRT. WPF/.NET satellite assemblies and the app's
# embedded JSON catalogs use their own loaders and must not become MRT resources.
# Keep Assets beneath this isolated root so Files/Assets/... identities survive.
$priInputRoot = Join-Path $runRoot 'pri-input'
$priInputAssets = Join-Path $priInputRoot 'Assets'
[void][IO.Directory]::CreateDirectory($priInputAssets)
foreach ($name in $packageAssets.Keys) {
    Copy-Item -LiteralPath (Join-Path $stageAssets $name) -Destination (Join-Path $priInputAssets $name)
}
$priPath = Join-Path $stageRoot 'resources.pri'
$makePriLog = Join-Path $runRoot 'makepri.log'
$priResult = Invoke-HiddenNative -FilePath $makePri -Arguments @('new', '/pr', $priInputRoot, '/cf', $priConfigPath, '/mn', $manifestPath, '/of', $priPath, '/o')
$priOutput = $priResult.Output -split '\r?\n'
$makePriExit = $priResult.ExitCode
$priLines = @($priOutput | ForEach-Object { "$_" -replace '\x00', '' })
$priLines | Set-Content -LiteralPath $makePriLog -Encoding utf8
if ($makePriExit -ne 0 -or -not (Test-Path -LiteralPath $priPath -PathType Leaf) -or
    @($priLines | Where-Object { $_ -match '(?i)\b(?:warning|error)\b' }).Count -gt 0) {
    throw "MakePri indexing failed or reported a warning: exit $makePriExit. Inspect $makePriLog"
}
$priDumpPath = Join-Path $runRoot 'resources.pri.xml'
$priDumpResult = Invoke-HiddenNative -FilePath $makePri -Arguments @('dump', '/if', $priPath, '/of', $priDumpPath, '/dt', 'detailed', '/o')
$priDumpOutput = $priDumpResult.Output -split '\r?\n'
$priDumpExit = $priDumpResult.ExitCode
$priDumpOutput | ForEach-Object { "$_" -replace '\x00', '' } | Set-Content -LiteralPath (Join-Path $runRoot 'makepri-dump.log') -Encoding utf8
if ($priDumpExit -ne 0 -or -not (Test-Path -LiteralPath $priDumpPath -PathType Leaf)) {
    throw "MakePri readback failed: exit $priDumpExit. Partial output retained in $runRoot"
}
[xml] $priDump = Get-Content -LiteralPath $priDumpPath -Raw
$priResources = @($priDump.SelectNodes('//NamedResource'))
if ($priDump.PriInfo.ResourceMap.name -cne $IdentityName -or $priResources.Count -ne 3) {
    throw 'PRI resource identity or shell logo resource count differs from the package contract.'
}
foreach ($name in @('StoreLogo.png', 'Square44x44Logo.png', 'Square150x150Logo.png')) {
    $resource = $priDump.SelectSingleNode("//NamedResource[@uri='ms-resource://$IdentityName/Files/Assets/$name']")
    if ($null -eq $resource) { throw "PRI lost the Assets path for $name." }
    $candidates = @($resource.SelectNodes('Candidate'))
    $scaleName = $name.Replace('.png', '.scale-200.png')
    $neutral = @($candidates | Where-Object { $_.Value -ceq "Assets\$name" -and $_.QualifierSet.index -eq '0' })
    $scaled = @($candidates | Where-Object { $_.Value -ceq "Assets\$scaleName" -and
        $_.SelectSingleNode("QualifierSet/Qualifier[@name='Scale' and @value='200']") })
    if ($candidates.Count -ne 2 -or $neutral.Count -ne 1 -or $scaled.Count -ne 1) {
        throw "PRI must preserve neutral and 200 percent shell logo candidates for $name."
    }
}
Write-Host "MakePri indexing and shell logo readback passed without warnings. Log: $makePriLog"

$scopeSuffix = if ($identityScope -eq 'local_validation_only') { '-local-only' } else { '-identity-provided' }
$packageName = "Vstavka-$PackageVersion-x64$scopeSuffix-unsigned.msix"
$packagePath = Join-Path $runRoot $packageName
$makeAppxLog = Join-Path $runRoot 'makeappx.log'
$makeAppxResult = Invoke-HiddenNative -FilePath $makeAppx -Arguments @('pack', '/d', $stageRoot, '/p', $packagePath, '/h', 'SHA256', '/o')
$makeAppxResult.Output | Set-Content -LiteralPath $makeAppxLog -Encoding utf8
if ($makeAppxResult.ExitCode -ne 0) {
    Get-Content -LiteralPath $makeAppxLog -Tail 25 | Write-Output
    throw "MakeAppx validation/pack failed: exit $($makeAppxResult.ExitCode). Partial output retained in $runRoot"
}
Write-Host "MakeAppx package creation and validation passed. Log: $makeAppxLog"

# Inspect the actual package, without installing or registering it.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $entry = $archive.GetEntry('AppxManifest.xml')
    if (-not $entry) { throw 'Packaged manifest is missing.' }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { [xml] $packedManifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $packedNs = [Xml.XmlNamespaceManager]::new($packedManifest.NameTable)
    $packedNs.AddNamespace('m', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $packedNs.AddNamespace('desktop', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10')
    $packedNs.AddNamespace('rescap', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities')
    $packedIdentity = $packedManifest.SelectSingleNode('/m:Package/m:Identity', $packedNs)
    foreach ($pair in @{ Name = $IdentityName; Publisher = $Publisher; Version = $PackageVersion; ProcessorArchitecture = 'x64' }.GetEnumerator()) {
        if ($packedIdentity.GetAttribute($pair.Key) -cne $pair.Value) { throw "Packaged identity mismatch: $($pair.Key)." }
    }
    $packedLanguages = @($packedManifest.SelectNodes('/m:Package/m:Resources/m:Resource', $packedNs) | ForEach-Object { $_.GetAttribute('Language') })
    if (($packedLanguages -join '|') -cne ($expectedManifestLanguages -join '|')) {
        throw 'Packaged language declarations differ from the runtime language list.'
    }
    if ($packedManifest.SelectSingleNode('/m:Package/m:Properties/m:DisplayName', $packedNs).InnerText -cne 'Vstavka') {
        throw 'Packaged display name must use the English product baseline Vstavka.'
    }
    $applications = $packedManifest.SelectNodes('/m:Package/m:Applications/m:Application', $packedNs)
    if ($applications.Count -ne 1 -or $applications[0].GetAttribute('Id') -cne 'Vstavka' -or
        $applications[0].GetAttribute('Executable') -cne 'Vstavka.exe' -or $null -eq $archive.GetEntry('Vstavka.exe')) {
        throw 'The package must expose exactly one Vstavka application.'
    }
    $startup = $packedManifest.SelectSingleNode('//desktop:StartupTask', $packedNs)
    if ($null -eq $startup -or $startup.GetAttribute('TaskId') -cne 'VstavkaStartup' -or $startup.GetAttribute('Enabled') -cne 'false') {
        throw 'Packaged startup contract differs from VstavkaStartup, initially disabled.'
    }
    $capabilityNodes = $packedManifest.SelectNodes('/m:Package/m:Capabilities/*', $packedNs)
    $capabilities = @($capabilityNodes | ForEach-Object { $_.GetAttribute('Name') })
    if ($capabilities.Count -ne 1 -or $capabilities[0] -cne 'runFullTrust') {
        throw 'Unexpected packaged capability set.'
    }
    $payloadInventory = @($archive.Entries | Where-Object { $_.Length -gt 0 } | ForEach-Object {
        $entryStream = $_.Open()
        try { $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($entryStream)).ToLowerInvariant() }
        finally { $entryStream.Dispose() }
        [ordered]@{ name = $_.FullName; bytes = $_.Length; sha256 = $digest }
    })
} finally { $archive.Dispose() }

$receipt = [ordered]@{
    schema = 'vstavka.package-preparation.v1'
    preparedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    phase = 'prepared'
    requestedEffect = 'local_preparation_only'
    application = $Application
    productVersion = $productVersion
    identityScope = $identityScope
    identityName = $IdentityName
    publisher = $Publisher
    packageVersion = $PackageVersion
    architecture = 'x64'
    artifact = $packageName
    bytes = (Get-Item -LiteralPath $packagePath).Length
    sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    makeAppxVersion = (Get-Item -LiteralPath $makeAppx).VersionInfo.FileVersion
    makePriVersion = (Get-Item -LiteralPath $makePri).VersionInfo.FileVersion
    resourceIndex = 'shell_assets_only_verified_neutral_and_200_percent'
    resourceIndexWarnings = 0
    resourceIndexReadback = 'passed_identity_paths_and_six_candidates'
    defaultLanguage = $defaultLanguage
    languages = $packedLanguages
    localizationCatalogs = $localizationCatalogs
    manifestValidation = 'passed_makeappx_and_packaged_readback'
    capabilities = $capabilities
    startupTask = 'VstavkaStartup'
    startupInitiallyEnabled = $false
    signature = 'unsigned'
    installed = $false
    wack = 'not_run'
    partnerCenterState = 'not_accessed'
    userAcceptance = 'pending'
    payload = $payloadInventory
}
$receiptPath = Join-Path $runRoot 'package-receipt.json'
$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptPath -Encoding utf8
[pscustomobject]@{
    Phase = 'prepared'
    Package = $packagePath
    Receipt = $receiptPath
    IdentityScope = $identityScope
    SHA256 = $receipt.sha256
    Wack = 'not_run'
    StoreDelivery = 'stopped_before_user_review'
}
