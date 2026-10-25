# Builds Capri.exe next to Capri.ps1 with the ps2exe module.
# Run:  powershell -ExecutionPolicy Bypass -File tools\Build-Exe.ps1
# The .exe still needs the rest of the folder (lib\, images, voices, json files) beside it.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName PresentationCore, WindowsBase

if(-not (Get-Module -ListAvailable -Name ps2exe)){
 Write-Host 'Installing ps2exe for your user account (one time)...'
 if(-not (Get-PackageProvider -ListAvailable -Name NuGet -ErrorAction SilentlyContinue)){Install-PackageProvider -Name NuGet -Scope CurrentUser -Force | Out-Null}
 Install-Module -Name ps2exe -Scope CurrentUser -Force
}
Import-Module ps2exe

# Icon: capri.ico if you made one, otherwise her hover pose (poses-faces-clean.png, first cell) as a 256 px icon.
$icon = Join-Path $root 'capri.ico'
if(-not (Test-Path -LiteralPath $icon)){
 Write-Host 'Making capri.ico from her hover pose...'
 $sheet = [Windows.Media.Imaging.BitmapImage]::new([Uri](Join-Path $root 'poses-faces-clean.png'))
 $cell = [Windows.Media.Imaging.CroppedBitmap]::new($sheet, [Windows.Int32Rect]::new(0, 0, [int]($sheet.PixelWidth/2), [int]($sheet.PixelHeight/2)))
 $size = 256.0
 $ratio = [Math]::Min($size/$cell.PixelWidth, $size/$cell.PixelHeight)
 $dw = $cell.PixelWidth*$ratio; $dh = $cell.PixelHeight*$ratio
 $visual = [Windows.Media.DrawingVisual]::new(); $context = $visual.RenderOpen()
 $context.DrawImage($cell, [Windows.Rect]::new(($size-$dw)/2.0, ($size-$dh)/2.0, $dw, $dh)); $context.Close()
 $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(256, 256, 96, 96, [Windows.Media.PixelFormats]::Pbgra32); $bitmap.Render($visual)
 $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new(); $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
 $png = [IO.MemoryStream]::new(); $encoder.Save($png); $bytes = $png.ToArray()
 # A one-image .ico holding a PNG (supported since Windows Vista).
 $file = [IO.File]::Create($icon); $out = [IO.BinaryWriter]::new($file)
 $out.Write([uint16]0); $out.Write([uint16]1); $out.Write([uint16]1)
 $out.Write([byte]0); $out.Write([byte]0); $out.Write([byte]0); $out.Write([byte]0)
 $out.Write([uint16]1); $out.Write([uint16]32); $out.Write([uint32]$bytes.Length); $out.Write([uint32]22)
 $out.Write($bytes); $out.Dispose()
}

Invoke-ps2exe -inputFile (Join-Path $root 'Capri.ps1') -outputFile (Join-Path $root 'Capri.exe') -noConsole -STA `
 -iconFile $icon -title 'Capri' -product 'Capri' -description 'Capri desktop companion' -version '2.0.0.0'
Write-Host "Built $(Join-Path $root 'Capri.exe')"
