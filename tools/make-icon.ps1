# Draws the Swatch Sheet icon (a fan of paint swatch cards) and saves it as a multi-size .ico.
# Run once with Windows PowerShell; the result is checked in at src\SwatchSheet\SwatchSheet.ico.
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot "..\src\SwatchSheet\SwatchSheet.ico"
$sizes = 16, 24, 32, 48, 64, 128, 256
$colors = "#FF5A5F", "#FFA53D", "#FFE05C", "#4CD787", "#4FA3FF", "#A46CFF"

function RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"
    $g.ScaleTransform($size / 256.0, $size / 256.0)

    $bg = RoundedRect 8 8 240 240 52
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml("#1A1E27"))), $bg)

    # Fan of cards pivoting near the bottom centre.
    $angles = -40, -24, -8, 8, 24, 40
    for ($i = 0; $i -lt $colors.Count; $i++) {
        $state = $g.Save()
        $g.TranslateTransform(128, 204)
        $g.RotateTransform($angles[$i])
        $card = RoundedRect -22 -150 44 154 15
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($colors[$i]))), $card)
        $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 0, 0, 0)), 3), $card)
        $g.Restore($state)
    }
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml("#F2F4F8"))), 112, 190, 32, 32)
    $g.Dispose()
    return $bmp
}

$pngs = foreach ($s in $sizes) {
    $b = Draw $s
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    , $ms.ToArray()
}

# ICO file: header, one directory entry per size, then the PNG data.
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
Write-Host "Saved $out"

