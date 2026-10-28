# Draws the Microsoft Store artwork in Package\StoreArt: 9:16 poster art and 1:1 box art (the app logo's slide and ink,
# large, on the logo's orange, with the product name). Partner Center: Store listing → Store logos.
Add-Type -AssemblyName System.Drawing
$dir = Join-Path $PSScriptRoot 'StoreArt'
New-Item -ItemType Directory -Force $dir | Out-Null

function Rgb([int]$r, [int]$g, [int]$b, [int]$a = 255) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }
function Rounded([System.Drawing.RectangleF]$rect, [double]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [float]($r * 2)
    $p.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $p.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $p.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $p.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure(); $p
}

function Draw([string]$name, [int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $u = $w / 100.0 # one unit = 1% of the width, so both sizes of a shape look the same

    # background: the logo's orange, lighter at the top, with two soft circles
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point 0, $h), (Rgb 0xE0 0x5A 0x2E), (Rgb 0xA8 0x32 0x16)
    $g.FillRectangle($bg, 0, 0, $w, $h)
    $soft = New-Object System.Drawing.SolidBrush (Rgb 255 255 255 18)
    $g.FillEllipse($soft, [float](-30 * $u), [float](-25 * $u), [float](90 * $u), [float](90 * $u))
    $g.FillEllipse($soft, [float](55 * $u), [float]($h - 60 * $u), [float](80 * $u), [float](80 * $u))

    # the slide: a white 16:9 card with a shadow, a title bar and text lines
    $cw = $(if ($h -gt $w) { 64 } else { 60 }) * $u; $ch = $cw * 9 / 16 # narrower on the square, so the pen stays inside
    $cx = ($w - $cw) / 2; $cy = $h * 0.43 - $ch / 2
    $card = New-Object System.Drawing.RectangleF ([float]$cx), ([float]$cy), ([float]$cw), ([float]$ch)
    $shadow = New-Object System.Drawing.RectangleF ([float]$cx), ([float]($cy + 1.6 * $u)), ([float]$cw), ([float]$ch)
    $g.FillPath((New-Object System.Drawing.SolidBrush (Rgb 0x5A 0x18 0x08 70)), (Rounded $shadow (3 * $u)))
    $g.FillPath([System.Drawing.Brushes]::White, (Rounded $card (3 * $u)))
    function At([double]$x, [double]$y) { New-Object System.Drawing.PointF ([float]($cx + $x * $cw)), ([float]($cy + $y * $ch)) }
    $orange = New-Object System.Drawing.SolidBrush (Rgb 0xC4 0x3E 0x1C)
    $grey = New-Object System.Drawing.SolidBrush (Rgb 0xD5 0xD8 0xDC)
    $g.FillRectangle($orange, [float]($cx + 0.08 * $cw), [float]($cy + 0.12 * $ch), [float](0.46 * $cw), [float](0.1 * $ch))
    foreach ($row in @(@(0.36, 0.5), @(0.5, 0.62), @(0.64, 0.42), @(0.78, 0.55))) {
        $g.FillRectangle($grey, [float]($cx + 0.08 * $cw), [float]($cy + $row[0] * $ch), [float]($row[1] * $cw), [float](0.055 * $ch))
    }

    # ink over the slide: a yellow highlight, a teal circle and a red check
    $hi = New-Object System.Drawing.Pen (Rgb 0xFF 0xD6 0x00 140), ([float](3.6 * $u))
    $g.DrawLine($hi, (At 0.07 0.53), (At 0.72 0.53))
    function Ink($color, [double]$width, $pts, [bool]$closed = $false) {
        $p = New-Object System.Drawing.Pen $color, ([float]($width * $u))
        $p.StartCap = 'Round'; $p.EndCap = 'Round'; $p.LineJoin = 'Round'
        $arr = [System.Drawing.PointF[]]($pts | ForEach-Object { At $_[0] $_[1] })
        if ($closed) { $g.DrawClosedCurve($p, $arr, [float]0.6, [System.Drawing.Drawing2D.FillMode]::Alternate) } else { $g.DrawCurve($p, $arr, [float]0.5) }
    }
    Ink (Rgb 0x1F 0x9E 0x8C) 1.3 @(@(0.70, 0.10), @(0.85, 0.07), @(0.95, 0.20), @(0.90, 0.40), @(0.75, 0.45), @(0.62, 0.34), @(0.65, 0.15), @(0.78, 0.06))
    Ink (Rgb 0xE5 0x1C 0x23) 1.5 @(@(0.73, 0.8), @(0.79, 0.88), @(0.92, 0.66))

    # a pen resting at the card's lower right corner
    $state = $g.Save()
    $g.TranslateTransform([float]($cx + $cw * 0.97), [float]($cy + $ch * 1.0))
    $g.RotateTransform(-45)
    $pl = $cw * 0.36; $pw = $cw * 0.047
    $g.FillPath((New-Object System.Drawing.SolidBrush (Rgb 0x22 0x2B 0x33)), (Rounded (New-Object System.Drawing.RectangleF ([float](0.12 * $pl)), ([float](-$pw / 2)), ([float](0.88 * $pl)), ([float]$pw)) ($pw / 2)))
    $g.FillRectangle((New-Object System.Drawing.SolidBrush (Rgb 0x1F 0x9E 0x8C)), [float](0.8 * $pl), [float](-$pw / 2), [float](0.06 * $pl), [float]$pw)
    $tip = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF ([float](0.13 * $pl)), ([float](-$pw / 2))), (New-Object System.Drawing.PointF ([float](0.13 * $pl)), ([float]($pw / 2))))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush (Rgb 0xF2 0xE6 0xD8)), $tip)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (Rgb 0x22 0x2B 0x33)), [float](-0.6 * $u), [float](-0.6 * $u), [float](1.2 * $u), [float](1.2 * $u))
    $g.Restore($state)

    # the product name under the slide
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $font = New-Object System.Drawing.Font 'Segoe UI Semibold', ([float](12 * $u)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $ty = $cy + $ch + ($h - $cy - $ch) * 0.45
    $g.DrawString('PPTSlider', $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, ([float]($ty - 10 * $u)), ([float]$w), ([float](20 * $u))), $fmt)

    $g.Dispose()
    $bmp.Save((Join-Path $dir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

Draw 'PosterArt-720x1080.png' 720 1080
Draw 'PosterArt-1440x2160.png' 1440 2160
Draw 'BoxArt-1080x1080.png' 1080 1080
Draw 'BoxArt-2160x2160.png' 2160 2160
Get-ChildItem $dir *.png | ForEach-Object { "$($_.Name)" }
