# Generates Installer\welcome.bmp — the NSIS MUI welcome/finish side banner (164x314).
# Dark theme background + the app icon (clay circle + swap arrows) + app name.
# Regenerate:  powershell Installer\generate-welcome.ps1
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$W = 164; $H = 314
$out = Join-Path $PSScriptRoot 'welcome.bmp'

$clay  = [System.Drawing.Color]::FromArgb(0xD9, 0x77, 0x57)
$white = [System.Drawing.Color]::White

$bmp = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode    = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$g.PixelOffsetMode  = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

# Vertical dark gradient background (matches the app's #1E1E1F theme).
$rect = New-Object System.Drawing.Rectangle(0, 0, $W, $H)
$c1 = [System.Drawing.Color]::FromArgb(0x26, 0x26, 0x28)
$c2 = [System.Drawing.Color]::FromArgb(0x18, 0x18, 0x19)
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 90)
$g.FillRectangle($bg, $rect)

# ---- App icon: clay circle + two white swap arrows ----
$cx = $W / 2.0
$cy = 96.0
$rc = 46.0                 # circle radius
$clayBrush = New-Object System.Drawing.SolidBrush($clay)
$g.FillEllipse($clayBrush, ($cx - $rc), ($cy - $rc), (2 * $rc), (2 * $rc))

$R = $rc * 0.55            # arrow ring radius
$t = $rc * 0.17           # stroke thickness
$pen = New-Object System.Drawing.Pen($white, $t)
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
$ring = New-Object System.Drawing.RectangleF(($cx - $R), ($cy - $R), (2 * $R), (2 * $R))
$g.DrawArc($pen, $ring, 200, 150)
$g.DrawArc($pen, $ring, 20,  150)

$whiteBrush = New-Object System.Drawing.SolidBrush($white)
$hw = $t * 1.35; $hl = $t * 1.9
function Add-Head([double]$deg) {
    $r = $deg * [Math]::PI / 180.0
    $px = $cx + $R * [Math]::Cos($r); $py = $cy + $R * [Math]::Sin($r)
    $tx = -[Math]::Sin($r); $ty = [Math]::Cos($r)
    $nx = -$ty; $ny = $tx
    $tip = New-Object System.Drawing.PointF([float]($px + $tx * $hl), [float]($py + $ty * $hl))
    $b1  = New-Object System.Drawing.PointF([float]($px + $nx * $hw), [float]($py + $ny * $hw))
    $b2  = New-Object System.Drawing.PointF([float]($px - $nx * $hw), [float]($py - $ny * $hw))
    $g.FillPolygon($whiteBrush, @($tip, $b1, $b2))
}
Add-Head 350
Add-Head 170

# ---- App name ----
$title = "Claude`nAccount`nSwitcher"
$font  = New-Object System.Drawing.Font("Segoe UI Semibold", 16, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$titleRect = New-Object System.Drawing.RectangleF(8, 158, ($W - 16), 90)
$g.DrawString($title, $font, $whiteBrush, $titleRect, $sf)

# Accent underline (Azure).
$azure = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(0x00, 0x78, 0xD4))
$g.FillRectangle($azure, ($cx - 22), 246, 44, 3)

# Subtitle.
$subFont = New-Object System.Drawing.Font("Segoe UI", 11, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$sage = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(0x9E, 0xA2, 0xAE))
$subRect = New-Object System.Drawing.RectangleF(8, 258, ($W - 16), 40)
$g.DrawString("Claude Code accounts", $subFont, $sage, $subRect, $sf)

$g.Dispose()
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmp.Dispose()
Write-Output "Wrote $out ($W x $H, 24-bit BMP)"
