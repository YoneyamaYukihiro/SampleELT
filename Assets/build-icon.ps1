# Build app.ico from the design defined in Assets/app.svg using GDI+.
# Renders sizes 16/24/32/48/64/128/256 as PNGs and packs them into a
# multi-size PNG-embedded ICO. Windows PowerShell 5.1 compatible.
#
# Design: BreezeFlow tilted leaf (with stem) + breeze streaks + data dots.

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outIco    = Join-Path $scriptDir 'app.ico'
$sizes     = 16, 24, 32, 48, 64, 128, 256

function Add-QuadCurve {
    # Append a quadratic Bezier (P0 -> Q -> P1) to a GraphicsPath
    # by converting it to its cubic equivalent.
    param(
        [System.Drawing.Drawing2D.GraphicsPath]$Path,
        [single]$X0, [single]$Y0,
        [single]$Qx, [single]$Qy,
        [single]$X1, [single]$Y1
    )
    $c1x = $X0 + (2.0 / 3.0) * ($Qx - $X0)
    $c1y = $Y0 + (2.0 / 3.0) * ($Qy - $Y0)
    $c2x = $X1 + (2.0 / 3.0) * ($Qx - $X1)
    $c2y = $Y1 + (2.0 / 3.0) * ($Qy - $Y1)
    $Path.AddBezier($X0, $Y0, $c1x, $c1y, $c2x, $c2y, $X1, $Y1)
}

function Add-Dot {
    param(
        [System.Drawing.Graphics]$G,
        [System.Drawing.Color]$Color,
        [single]$Cx, [single]$Cy, [single]$R
    )
    $brush = New-Object System.Drawing.SolidBrush $Color
    $G.FillEllipse($brush, $Cx - $R, $Cy - $R, $R * 2, $R * 2)
    $brush.Dispose()
}

function New-IconPng {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    $scale = $Size / 256.0
    $g.ScaleTransform($scale, $scale)

    # ---- background: rounded square with cyan gradient ----
    $rectF  = New-Object System.Drawing.RectangleF 8, 8, 240, 240
    $bgPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r      = 40.0
    $bgPath.AddArc($rectF.X,                       $rectF.Y,                       $r, $r, 180, 90)
    $bgPath.AddArc($rectF.X + $rectF.Width - $r,   $rectF.Y,                       $r, $r, 270, 90)
    $bgPath.AddArc($rectF.X + $rectF.Width - $r,   $rectF.Y + $rectF.Height - $r,  $r, $r,   0, 90)
    $bgPath.AddArc($rectF.X,                       $rectF.Y + $rectF.Height - $r,  $r, $r,  90, 90)
    $bgPath.CloseFigure()

    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF 8, 8),
        (New-Object System.Drawing.PointF 248, 248),
        ([System.Drawing.Color]::FromArgb(255, 128, 222, 234)),
        ([System.Drawing.Color]::FromArgb(255,   0, 131, 143)))
    $g.FillPath($bgBrush, $bgPath)
    $bgBrush.Dispose()
    $bgPath.Dispose()

    # ---- breeze streaks (white, alpha ~0.4) ----
    $breezePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(102, 255, 255, 255)), 5
    $breezePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $breezePen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round

    $bp = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-QuadCurve -Path $bp -X0 24 -Y0 84  -Qx 60 -Qy 76  -X1 96  -Y1 84
    $g.DrawPath($breezePen, $bp); $bp.Reset()
    Add-QuadCurve -Path $bp -X0 20 -Y0 124 -Qx 56 -Qy 116 -X1 86  -Y1 124
    $g.DrawPath($breezePen, $bp); $bp.Reset()
    Add-QuadCurve -Path $bp -X0 36 -Y0 168 -Qx 72 -Qy 160 -X1 104 -Y1 168
    $g.DrawPath($breezePen, $bp)
    $bp.Dispose()
    $breezePen.Dispose()

    # ---- stem ----
    $stemPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 46, 125, 50)), 9
    $stemPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $stemPen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $stemPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-QuadCurve -Path $stemPath -X0 50 -Y0 222 -Qx 74 -Qy 204 -X1 96 -Y1 184
    $g.DrawPath($stemPen, $stemPath)
    $stemPath.Dispose()
    $stemPen.Dispose()

    # ---- leaf body ----
    $leafPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $leafPath.AddBezier( 96, 184,  50, 130,  90,  60, 200,  56)
    $leafPath.AddBezier(200,  56, 180, 130, 150, 180,  96, 184)
    $leafPath.CloseFigure()

    $leafBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF 0, 256),
        (New-Object System.Drawing.PointF 256, 0),
        ([System.Drawing.Color]::FromArgb(255,  46, 125,  50)),
        ([System.Drawing.Color]::FromArgb(255, 139, 195,  74)))
    $g.FillPath($leafBrush, $leafPath)
    $leafBrush.Dispose()
    $leafPath.Dispose()

    # ---- midrib (white, alpha ~0.55) ----
    $midPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(140, 255, 255, 255)), ([single]3.5)
    $midPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $midPen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $midPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-QuadCurve -Path $midPath -X0 96 -Y0 184 -Qx 142 -Qy 122 -X1 200 -Y1 56
    $g.DrawPath($midPen, $midPath)
    $midPath.Dispose()
    $midPen.Dispose()

    # ---- side veins (white, alpha ~0.45) ----
    $veinPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(115, 255, 255, 255)), 2
    $veinPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $veinPen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $vp = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-QuadCurve -Path $vp -X0 118 -Y0 156 -Qx 132 -Qy 144 -X1 150 -Y1 138
    $g.DrawPath($veinPen, $vp); $vp.Reset()
    Add-QuadCurve -Path $vp -X0 142 -Y0 124 -Qx 158 -Qy 114 -X1 174 -Y1 108
    $g.DrawPath($veinPen, $vp); $vp.Reset()
    Add-QuadCurve -Path $vp -X0 132 -Y0 168 -Qx 152 -Qy 154 -X1 174 -Y1 144
    $g.DrawPath($veinPen, $vp)
    $vp.Dispose()
    $veinPen.Dispose()

    # ---- data dots ----
    Add-Dot -G $g -Color ([System.Drawing.Color]::FromArgb(255, 255, 255, 255)) -Cx 218 -Cy 40  -R 6
    Add-Dot -G $g -Color ([System.Drawing.Color]::FromArgb(217, 255, 255, 255)) -Cx 232 -Cy 58  -R 4
    Add-Dot -G $g -Color ([System.Drawing.Color]::FromArgb(178, 255, 255, 255)) -Cx 206 -Cy 24  -R 3
    Add-Dot -G $g -Color ([System.Drawing.Color]::FromArgb(217, 255, 255, 255)) -Cx 40  -Cy 48  -R 5
    Add-Dot -G $g -Color ([System.Drawing.Color]::FromArgb(217, 255, 255, 255)) -Cx 220 -Cy 218 -R 5
    Add-Dot -G $g -Color ([System.Drawing.Color]::FromArgb(178, 255, 255, 255)) -Cx 32  -Cy 200 -R 4
    Add-Dot -G $g -Color ([System.Drawing.Color]::FromArgb(255, 220, 237, 200)) -Cx 170 -Cy 92  -R ([single]3.5)
    Add-Dot -G $g -Color ([System.Drawing.Color]::FromArgb(230, 220, 237, 200)) -Cx 124 -Cy 148 -R 3

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
