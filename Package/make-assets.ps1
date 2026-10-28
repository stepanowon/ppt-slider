# Draws the MSIX logo PNGs in Package\Assets: an orange rounded tile with a white slide and a pen stroke.
# Also the Store's 1:1 icons (300, 150, 71) in Package\StoreArt. Run again after changing the design; the PNGs are committed so a normal build does not need this.
Add-Type -AssemblyName System.Drawing
$dir = Join-Path $PSScriptRoot 'Assets'
New-Item -ItemType Directory -Force $dir | Out-Null

function Draw([string]$name, [int]$w, [int]$h, [string]$to = $dir) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    # the icon is a square centred in the image (wide tiles keep empty sides)
    $s = [Math]::Min($w, $h)
    $x0 = ($w - $s) / 2.0; $y0 = ($h - $s) / 2.0
    function Box([double]$x, [double]$y, [double]$rw, [double]$rh) { New-Object System.Drawing.RectangleF ([float]($x0 + $x * $s)), ([float]($y0 + $y * $s)), ([float]($rw * $s)), ([float]($rh * $s)) }
    function Rounded($rect, [double]$r) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = [float]($r * 2)
        $p.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
        $p.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
        $p.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
        $p.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
        $p.CloseFigure(); $p
    }
    $tile = Box 0.06 0.06 0.88 0.88
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0xC4, 0x3E, 0x1C))), (Rounded $tile ($s * 0.16)))
    $slide = Box 0.2 0.27 0.6 0.42
    $g.FillPath([System.Drawing.Brushes]::White, (Rounded $slide ($s * 0.04)))
    $bar = Box 0.27 0.34 0.3 0.06
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0xC4, 0x3E, 0x1C))), $bar)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(0x2E, 0x9E, 0x8E)), ([float]($s * 0.045))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $pts = @((0.28, 0.6), (0.4, 0.5), (0.5, 0.58), (0.7, 0.46)) | ForEach-Object { New-Object System.Drawing.PointF ([float]($x0 + $_[0] * $s)), ([float]($y0 + $_[1] * $s)) }
    $g.DrawCurve($pen, [System.Drawing.PointF[]]$pts)
    $g.Dispose()
    $bmp.Save((Join-Path $to $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

Draw 'Square44x44Logo.png' 44 44
Draw 'Square150x150Logo.png' 150 150
Draw 'Wide310x150Logo.png' 310 150
Draw 'StoreLogo.png' 50 50
$store = Join-Path $PSScriptRoot 'StoreArt'
New-Item -ItemType Directory -Force $store | Out-Null
foreach ($n in 300, 150, 71) { Draw "StoreIcon-${n}x$n.png" $n $n $store }
Get-ChildItem $dir *.png | ForEach-Object { "$($_.Name)" }
