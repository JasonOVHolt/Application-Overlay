# Generates app.ico (16-256 px, PNG-compressed entries) for Window Overlay.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$outDir = Split-Path $MyInvocation.MyCommand.Path

function RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}

function Render([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # Background: rounded square, blue gradient
    $bg = RoundRect (8*$s) (8*$s) (240*$s) (240*$s) (52*$s)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 0)), ([System.Drawing.PointF]::new($size, $size)), ([System.Drawing.Color]::FromArgb(104, 160, 255)), ([System.Drawing.Color]::FromArgb(48, 92, 220))
    $g.FillPath($grad, $bg)

    # Back window (the app being mirrored): translucent outline
    $back = RoundRect (36*$s) (48*$s) (150*$s) (110*$s) (14*$s)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 255, 255, 255))), $back)
    if ($size -ge 32) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(200, 255, 255, 255)), ([Math]::Max(1.5, 7*$s))
        $g.DrawPath($pen, $back)
    }

    # Front window (the overlay): solid dark with a play triangle
    $front = RoundRect (92*$s) (104*$s) (128*$s) (100*$s) (14*$s)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(24, 24, 32))), $front)
    $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1, 7*$s))), $front)
    $cx = 156*$s; $cy = 154*$s; $t = 26*$s
    $tri = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($cx - $t*0.7, $cy - $t),
        [System.Drawing.PointF]::new($cx - $t*0.7, $cy + $t),
        [System.Drawing.PointF]::new($cx + $t, $cy))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)), $tri)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($size -ge 256) { $bmp.Save("$outDir\app-$size.png") }  # app-256 preview; app-1024 for macOS/Linux
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($sz in $sizes) { ,(Render $sz) }

# ICO container: header, directory entries, then PNG data
$fs = [System.IO.File]::Create("$outDir\app.ico")
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Close()
"Wrote $outDir\app.ico"

# Large PNG for the macOS app icon (built into .icns by macos/build-app.sh) and Linux.
$null = Render 1024
"Wrote $outDir\app-1024.png"
