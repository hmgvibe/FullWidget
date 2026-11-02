# Draws the FullWidget logo (three cards making an F, a dial in the last corner) at every Windows
# icon size and writes app.ico. Same shapes as build\logo\logo.svg; the small sizes are snapped to
# whole pixels so they stay sharp in the tray and on the taskbar.
# Usage: powershell -File build\make-icon.ps1 [output.ico] [-Png folder]
param(
  [string]$Out = (Join-Path $PSScriptRoot '..\src\FullWidget\app.ico'),
  [string]$Png = (Join-Path $PSScriptRoot 'logo')
)
Add-Type -AssemblyName System.Drawing

$cyan = '#4cc9f0'; $violet = '#9b5de5'; $yellow = '#ffd166'; $green = '#06d6a0'

# Each shape: x, y, width, height, corner radius (a circle is a square with radius = half).
# The 64 grid is the master; 16, 20 and 24 are drawn by hand on their own pixel grid.
$grids = @{
  64 = @{ stem = 4, 4, 18, 56, 7;      top = 26, 4, 34, 18, 7;   mid = 26, 26, 20, 16, 7;   dot = 43, 43, 17, 17, 8.5 }
  24 = @{ stem = 1, 1, 7, 22, 2.75;    top = 10, 1, 13, 7, 2.75; mid = 10, 10, 8, 6, 2.5;   dot = 16, 16, 7, 7, 3.5 }
  20 = @{ stem = 1, 1, 6, 18, 2.25;    top = 8, 1, 11, 6, 2.25;  mid = 8, 8, 7, 5, 2;       dot = 13, 13, 6, 6, 3 }
  16 = @{ stem = 1, 1, 4, 14, 1.75;    top = 6, 1, 9, 4, 1.75;   mid = 6, 6, 5, 4, 1.5;     dot = 11, 11, 4, 4, 2 }
}

function Round-Path([double[]]$r, [double]$k) {
  $x = $r[0] * $k; $y = $r[1] * $k; $w = $r[2] * $k; $h = $r[3] * $k; $d = [Math]::Min($r[4] * $k * 2, [Math]::Min($w, $h))
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $p.AddArc([single]$x, [single]$y, [single]$d, [single]$d, 180, 90)
  $p.AddArc([single]($x + $w - $d), [single]$y, [single]$d, [single]$d, 270, 90)
  $p.AddArc([single]($x + $w - $d), [single]($y + $h - $d), [single]$d, [single]$d, 0, 90)
  $p.AddArc([single]$x, [single]($y + $h - $d), [single]$d, [single]$d, 90, 90)
  $p.CloseFigure()
  $p
}

function Draw([int]$size) {
  $grid = if ($grids.ContainsKey($size)) { $size } else { 64 }
  $g0 = $grids[$grid]; $k = $size / $grid
  $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
  $g.Clear([System.Drawing.Color]::Transparent)
  foreach ($s in @(@('stem', $cyan), @('top', $violet), @('mid', $yellow), @('dot', $green))) {
    $path = Round-Path $g0[$s[0]] $k
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($s[1]))
    $g.FillPath($brush, $path); $brush.Dispose(); $path.Dispose()
  }
  $g.Dispose()
  $bmp
}

# Small frames as classic 32-bit DIBs (every Windows API reads them), big ones as PNG.
function Dib-Bytes($bmp) {
  $n = $bmp.Width
  $ms = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $ms
  $w.Write([uint32]40); $w.Write([int32]$n); $w.Write([int32]($n * 2)); $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]0); $w.Write([uint32]0); $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
  for ($y = $n - 1; $y -ge 0; $y--) {
    for ($x = 0; $x -lt $n; $x++) { $c = $bmp.GetPixel($x, $y); $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A) }
  }
  $maskRow = [int]([Math]::Ceiling($n / 32) * 4)
  $w.Write((New-Object byte[] ($maskRow * $n)))   # AND mask: all zero, the alpha channel decides
  $w.Flush(); $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($size in $sizes) {
  $bmp = Draw $size
  if ($size -ge 64) { $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $data = $ms.ToArray() }
  else { $data = Dib-Bytes $bmp }
  $bmp.Dispose()
  ,@($size, $data)
}

# ICO file: header, one 16-byte entry per frame, then each frame's data.
$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
  $s = $f[0]; $data = $f[1]
  $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
  $offset += $data.Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f[1]) }
$w.Close()
"wrote $Out ($($frames.Count) sizes)"

# PNGs for documents and the market site.
if ($Png) {
  New-Item -ItemType Directory -Force $Png | Out-Null
  foreach ($size in 16, 32, 256, 512) { $bmp = Draw $size; $bmp.Save((Join-Path $Png "logo-$size.png"), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose() }
  "wrote logo-16/32/256/512.png to $Png"
}
