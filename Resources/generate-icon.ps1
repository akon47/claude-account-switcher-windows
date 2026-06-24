# Generates Resources\app.ico — a Claude-clay circle with two white "swap/refresh"
# arrows. Regenerate after tweaking the design:  powershell Resources\generate-icon.ps1
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
$outPath = Join-Path $PSScriptRoot 'app.ico'
$sizes   = @(16, 24, 32, 48, 64, 128, 256)

# Claude-clay circle, white arrows.
$clay  = [System.Drawing.Color]::FromArgb(255, 0xD9, 0x77, 0x57)
$white = [System.Drawing.Color]::FromArgb(255, 0xFF, 0xFF, 0xFF)

function New-Frame([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Filled circle (slight inset so anti-aliased edge isn't clipped)
    $inset = [Math]::Max(1.0, $S * 0.03)
    $d = $S - 2 * $inset
    $clayBrush = New-Object System.Drawing.SolidBrush($clay)
    $g.FillEllipse($clayBrush, $inset, $inset, $d, $d)

    # Two circular arrows (refresh/swap), white.
    $cx = $S / 2.0
    $cy = $S / 2.0
    $R  = $S * 0.255          # arc radius
    $t  = [Math]::Max(1.5, $S * 0.085)   # stroke thickness

    $pen = New-Object System.Drawing.Pen($white, $t)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round

    $rect = New-Object System.Drawing.RectangleF(($cx - $R), ($cy - $R), (2 * $R), (2 * $R))

    # GDI+ angles: 0=right, 90=down, clockwise positive.
    # Top arc sweeps over the top; bottom arc over the bottom — mirrored, leaving
    # a gap on each side for an arrowhead.
    $sweep = 150
    $g.DrawArc($pen, $rect, 200, $sweep)   # top arc:    200° -> 350°
    $g.DrawArc($pen, $rect, 20,  $sweep)   # bottom arc:  20° -> 170°

    $whiteBrush = New-Object System.Drawing.SolidBrush($white)
    $hw = $t * 1.35   # arrowhead half-width
    $hl = $t * 1.9    # arrowhead length

    function Add-Head([double]$endDeg) {
        $rad = $endDeg * [Math]::PI / 180.0
        $px = $cx + $R * [Math]::Cos($rad)
        $py = $cy + $R * [Math]::Sin($rad)
        # tangent for increasing (clockwise) angle: (-sin, cos)
        $tx = -[Math]::Sin($rad); $ty = [Math]::Cos($rad)
        # normal
        $nx = -$ty; $ny = $tx
        $tip  = New-Object System.Drawing.PointF([float]($px + $tx * $hl), [float]($py + $ty * $hl))
        $b1   = New-Object System.Drawing.PointF([float]($px + $nx * $hw), [float]($py + $ny * $hw))
        $b2   = New-Object System.Drawing.PointF([float]($px - $nx * $hw), [float]($py - $ny * $hw))
        $g.FillPolygon($whiteBrush, @($tip, $b1, $b2))
    }
    Add-Head 350    # end of top arc -> points down-right
    Add-Head 170    # end of bottom arc -> points up-left

    $g.Dispose()
    $clayBrush.Dispose(); $whiteBrush.Dispose(); $pen.Dispose()
    return $bmp
}

# Build an ICO whose frames are PNG-encoded (Windows Vista+ supports PNG in ICO).
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Frame $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $bmp.Dispose(); $ms.Dispose()
}

$fs = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($fs)
# ICONDIR
$bw.Write([UInt16]0)              # reserved
$bw.Write([UInt16]1)              # type = icon
$bw.Write([UInt16]$sizes.Count)   # count
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $bytes = $pngs[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([Byte]$dim)         # width
    $bw.Write([Byte]$dim)         # height
    $bw.Write([Byte]0)            # palette
    $bw.Write([Byte]0)            # reserved
    $bw.Write([UInt16]1)          # planes
    $bw.Write([UInt16]32)         # bpp
    $bw.Write([UInt32]$bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($bytes in $pngs) { $bw.Write($bytes) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($outPath, $fs.ToArray())
$bw.Dispose(); $fs.Dispose()
Write-Output "Wrote $outPath ($([Math]::Round((Get-Item $outPath).Length / 1KB, 1)) KB, $($sizes.Count) frames)"
