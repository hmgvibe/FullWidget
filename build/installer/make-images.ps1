# Draws the installer's header picture (the logo on white) at every size Inno Setup picks from
# for the screen's scaling, from build\logo\logo-512.png.
# Usage: powershell -File build\installer\make-images.ps1
Add-Type -AssemblyName System.Drawing
$logo = [System.Drawing.Image]::FromFile((Join-Path $PSScriptRoot '..\logo\logo-512.png'))
foreach ($size in 55, 64, 69, 83, 92, 110, 119, 138) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear([System.Drawing.Color]::White)
  $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
  $pad = [int][Math]::Round($size * 0.1)
  $g.DrawImage($logo, $pad, $pad, $size - 2 * $pad, $size - 2 * $pad)
  $g.Dispose()
  $bmp.Save((Join-Path $PSScriptRoot "header-$size.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp)
  $bmp.Dispose()
}
$logo.Dispose()
"wrote header-55..138.bmp"
