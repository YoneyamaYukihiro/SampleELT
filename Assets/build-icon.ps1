# Build app.ico from the design defined in Assets/app.svg using GDI+.
# Renders sizes 16/24/32/48/64/128/256 as PNGs and packs them into a
# multi-size PNG-embedded ICO. Windows PowerShell 5.1 compatible.

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outIco    = Join-Path $scriptDir 'app.ico'
$sizes     = 16, 24, 32, 48, 64, 128, 256

function Add-Cylinder {
    param(
        [System.Drawing.Graphics]$G,
        [single]$X, [single]$Y,
        [single]$W = 52, [single]$H = 64
    )
    $ellH    = 20.0
    $white   = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $accent  = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255,124,28,14)), 3
    $accent.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $accent.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round

    # silhouette: top ellipse + body rect + bottom ellipse (all white)
    $G.FillEllipse($white, $X, $Y, $W, $ellH)
    $G.FillRectangle($white, $X, $Y + $ellH/2, $W, $H - $ellH)
    $G.FillEllipse($white, $X, $Y + $H - $ellH, $W, $ellH)

    # accent: outline top rim (open top cue)
    $G.DrawEllipse($accent, $X, $Y, $W, $ellH)

    # accent: middle platter curve (suggests disk stack)
    $midPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $midPath.AddArc($X, $Y + ($H/2) - 6, $W, 14, 0, 180)
    $G.DrawPath($accent, $midPath)
    $midPath.Dispose()

    $accent.Dispose()
    $white.Dispose()
}

function New-IconPng {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    $scale = $Size / 256.0
    $g.ScaleTransform($scale, $scale)

    # ---- background: rounded square with gradient ----
    $rectF = New-Object System.Drawing.RectangleF 8, 8, 240, 240
    $path  = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r     = 40.0
    $path.AddArc($rectF.X,                    $rectF.Y,                    $r, $r, 180, 90)
    $path.AddArc($rectF.X + $rectF.Width - $r, $rectF.Y,                    $r, $r, 270, 90)
    $path.AddArc($rectF.X + $rectF.Width - $r, $rectF.Y + $rectF.Height - $r, $r, $r, 0,   90)
    $path.AddArc($rectF.X,                    $rectF.Y + $rectF.Height - $r, $r, $r, 90,  90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF 8,8),
        (New-Object System.Drawing.PointF 248,248),
        ([System.Drawing.Color]::FromArgb(255,245,124,0)),
        ([System.Drawing.Color]::FromArgb(255,211,47,47)))
    $g.FillPath($brush, $path)
    $brush.Dispose()
    $path.Dispose()

    # ---- "ELT" text as a vector path ----
    $textPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $family   = New-Object System.Drawing.FontFamily 'Segoe UI'
    $style    = [System.Drawing.FontStyle]::Bold
    $emSize   = 100.0
    $fmt      = [System.Drawing.StringFormat]::GenericTypographic.Clone()
    $fmt.Alignment     = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Near
    $textPath.AddString('ELT', $family, [int]$style, $emSize, (New-Object System.Drawing.RectangleF 0, 0, 256, 256), $fmt)

    $bounds  = $textPath.GetBounds()
    $offsetX = 128 - ($bounds.X + $bounds.Width / 2)
    $offsetY = 40  - $bounds.Y
    $mx = New-Object System.Drawing.Drawing2D.Matrix
    $mx.Translate($offsetX, $offsetY)
    $textPath.Transform($mx)
    $mx.Dispose()

    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.FillPath($white, $textPath)
    $textPath.Dispose()
    $family.Dispose()

    # ---- two DB cylinders ----
    Add-Cylinder -G $g -X 20  -Y 156
    Add-Cylinder -G $g -X 184 -Y 156

    # ---- flow arrow between cylinders (shortened so it doesn't touch the cylinders) ----
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 8
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLine($pen, 100, 188, 148, 188)
    $arrowHead = @(
        (New-Object System.Drawing.Point 140, 178),
        (New-Object System.Drawing.Point 160, 188),
        (New-Object System.Drawing.Point 140, 198)
    )
    $g.DrawLines($pen, [System.Drawing.Point[]]$arrowHead)
    $pen.Dispose()
    $white.Dispose()

    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$pngs = @{}
foreach ($s in $sizes) {
    $pngs[$s] = New-IconPng -Size $s
    Write-Host ("  {0,3}x{1,-3}: {2,7:N0} bytes" -f $s, $s, $pngs[$s].Length)
}

$fs = [System.IO.File]::Open($outIco, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter $fs
try {
    $bw.Write([UInt16]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]$sizes.Count)

    $headerSize = 6 + 16 * $sizes.Count
    $offset     = $headerSize

    foreach ($s in $sizes) {
        $data = $pngs[$s]
        if ($s -ge 256) { $dim = [Byte]0 } else { $dim = [Byte]$s }
        $bw.Write($dim)
        $bw.Write($dim)
        $bw.Write([Byte]0)
        $bw.Write([Byte]0)
        $bw.Write([UInt16]1)
        $bw.Write([UInt16]32)
        $bw.Write([UInt32]$data.Length)
        $bw.Write([UInt32]$offset)
        $offset += $data.Length
    }

    foreach ($s in $sizes) {
        $bw.Write($pngs[$s])
    }
}
finally {
    $bw.Dispose()
    $fs.Dispose()
}

Write-Host ""
Write-Host ("Generated: {0} ({1:N0} bytes)" -f $outIco, (Get-Item $outIco).Length)
