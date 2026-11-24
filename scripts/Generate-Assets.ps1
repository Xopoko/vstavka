[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assets=Join-Path $projectRoot 'packaging\Assets'
$appAssets=Join-Path $projectRoot 'assets'
[IO.Directory]::CreateDirectory($assets) | Out-Null
[IO.Directory]::CreateDirectory($appAssets) | Out-Null
function New-IconPng([int]$Size,[string]$Path) {
    $bitmap=[Drawing.Bitmap]::new($Size,$Size)
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.ScaleTransform($Size/256.0,$Size/256.0)
    $background=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(119,82,223))
    $white=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,255,255))
    $muted=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(192,172,247))
    $round=[Drawing.Drawing2D.GraphicsPath]::new()
    $round.AddArc(8,8,88,88,180,90);$round.AddArc(160,8,88,88,270,90)
    $round.AddArc(160,160,88,88,0,90);$round.AddArc(8,160,88,88,90,90);$round.CloseFigure()
    $graphics.FillPath($background,$round)
    $graphics.FillRectangle($muted,60,65,104,121)
    $graphics.FillRectangle($white,84,84,110,120)
    $graphics.FillRectangle($background,107,112,64,10)
    $graphics.FillRectangle($background,107,137,50,10)
    $graphics.FillRectangle($background,107,162,34,10)
    $bitmap.Save($Path,[Drawing.Imaging.ImageFormat]::Png)
    $round.Dispose();$background.Dispose();$white.Dispose();$muted.Dispose();$graphics.Dispose();$bitmap.Dispose()
}
foreach($asset in @(@('StoreLogo',50),@('Square44x44Logo',44),@('Square150x150Logo',150))) {
    New-IconPng -Size $asset[1] -Path (Join-Path $assets ($asset[0]+'.png'))
    New-IconPng -Size ($asset[1]*2) -Path (Join-Path $assets ($asset[0]+'.scale-200.png'))
}
$pngPath=Join-Path $appAssets 'Vstavka.png'
New-IconPng -Size 256 -Path $pngPath
$png=[IO.File]::ReadAllBytes($pngPath)
$stream=[IO.File]::Create((Join-Path $appAssets 'Vstavka.ico'))
$writer=[IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]1)
$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([byte]0)
$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$png.Length);$writer.Write([uint32]22);$writer.Write($png)
$writer.Dispose();$stream.Dispose()
Write-Output 'Generated original Vstavka icon and 6 MSIX logo assets.'
