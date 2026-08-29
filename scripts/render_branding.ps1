# Renders SuperScroll branding PNGs from the geometry in assets/superscroll-mark.svg.
# System.Drawing only - no ImageMagick, no Inkscape, no npm.
# Keep the numbers here in sync with the SVG if the mark ever changes.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'assets'
if (-not (Test-Path $assets)) { New-Item -ItemType Directory -Path $assets | Out-Null }

# Mark geometry, in the SVG's 256x256 design space. Colours come from the settings
# page's own palette (src/Controls/SettingsResources.xaml) so the addon reads as one
# thing: the field is SsSurface over SsGround, the chevrons are the SsTrackOn ramp.
$DESIGN = 256.0
$CORNER = 56.0
$STROKE = 24.0
$FIELD_TOP = '#FF1A212B'
$FIELD_BOTTOM = '#FF12161C'
$MOTION_TOP = '#FF1F6E5C'
$MOTION_BOTTOM = '#FF69DCBB'
$TEXT_PRIMARY = '#FFE6EBF2'
$TEXT_MUTED = '#FF94A3B4'

# Each chevron: left-x, arm-y, apex-y, right-x, alpha. Apex gaps run 50 then 34
# design units - the same shrinking step the scroll easing takes each frame.
$CHEVRONS = @(
    @{ L = 68.0; ArmY = 70.0;  ApexY = 104.0; R = 188.0; A = 165 },
    @{ L = 82.0; ArmY = 128.0; ApexY = 154.0; R = 174.0; A = 210 },
    @{ L = 94.0; ArmY = 170.0; ApexY = 188.0; R = 162.0; A = 255 }
)
$GRAD_TOP = 58.0     # top of the chevron ink, including round caps
$GRAD_BOTTOM = 200.0 # bottom of same

function ConvertTo-Color([string]$hex) {
    [System.Drawing.ColorTranslator]::FromHtml($hex.Replace('#FF', '#'))
}

function New-VerticalBrush([single]$x, [single]$y0, [single]$y1, [string]$from, [string]$to, [int]$alpha) {
    $c0 = ConvertTo-Color $from
    $c1 = ConvertTo-Color $to
    if ($alpha -lt 255) {
        $c0 = [System.Drawing.Color]::FromArgb($alpha, $c0)
        $c1 = [System.Drawing.Color]::FromArgb($alpha, $c1)
    }
    # Pad the endpoints: GDI+ can sample outside the declared band and wrap.
    New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF($x, ($y0 - 2))),
        (New-Object System.Drawing.PointF($x, ($y1 + 2))),
        $c0, $c1)
}

function New-RoundedRectPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc(($x + $w - $d), $y, $d, $d, 270, 90)
    $p.AddArc(($x + $w - $d), ($y + $h - $d), $d, $d, 0, 90)
    $p.AddArc($x, ($y + $h - $d), $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

# Draws the mark scaled into a size x size box at (x, y). -NoField omits the
# rounded plate, which would otherwise ghost against an already-dark lockup.
function Add-Mark([System.Drawing.Graphics]$g, [single]$x, [single]$y, [single]$size, [switch]$NoField) {
    $s = $size / $DESIGN
    if (-not $NoField) {
        $fieldBrush = New-VerticalBrush $x $y ($y + $size) $FIELD_TOP $FIELD_BOTTOM 255
        $field = New-RoundedRectPath $x $y $size $size ($CORNER * $s)
        $g.FillPath($fieldBrush, $field)
        $field.Dispose(); $fieldBrush.Dispose()
    }

    $gy0 = $y + ($GRAD_TOP * $s)
    $gy1 = $y + ($GRAD_BOTTOM * $s)
    foreach ($c in $CHEVRONS) {
        $brush = New-VerticalBrush ($x + $size / 2) $gy0 $gy1 $MOTION_TOP $MOTION_BOTTOM $c.A
        $pen = New-Object System.Drawing.Pen($brush, ($STROKE * $s))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $pts = @(
            (New-Object System.Drawing.PointF(($x + $c.L * $s), ($y + $c.ArmY * $s))),
            (New-Object System.Drawing.PointF(($x + $size / 2), ($y + $c.ApexY * $s))),
            (New-Object System.Drawing.PointF(($x + $c.R * $s), ($y + $c.ArmY * $s)))
        )
        $g.DrawLines($pen, $pts)
        $pen.Dispose(); $brush.Dispose()
    }
}

# Plateless mark, sized and placed by its ink rather than by the design box.
function Add-Glyph([System.Drawing.Graphics]$g, [single]$cx, [single]$cy, [single]$inkHeight) {
    $size = $inkHeight * $DESIGN / ($GRAD_BOTTOM - $GRAD_TOP)
    Add-Mark $g ($cx - $size / 2) ($cy - $size / 2) $size -NoField
}

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    ,@($bmp, $g)
}

function Save-Canvas([System.Drawing.Bitmap]$bmp, [System.Drawing.Graphics]$g, [string]$path) {
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  $path" -ForegroundColor Green
}

function New-Font([string]$family, [single]$px) {
    New-Object System.Drawing.Font($family, $px, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
}

# The SVG is the master, but the numbers above are a hand copy of it, and a copy that
# nothing checks is a copy that quietly rots. Fail loudly instead of shipping a PNG that
# disagrees with the vector.
function Assert-SvgInSync {
    $svgPath = Join-Path $assets 'superscroll-mark.svg'
    if (-not (Test-Path $svgPath)) { throw "Missing master SVG: $svgPath" }
    $svg = Get-Content $svgPath -Raw
    $expected = @($FIELD_TOP, $FIELD_BOTTOM, $MOTION_TOP, $MOTION_BOTTOM) |
        ForEach-Object { $_.Replace('#FF', '#') }
    foreach ($c in $CHEVRONS) {
        if ($c.A -lt 255) {
            $o = [math]::Round($c.A / 255, 2).ToString([cultureinfo]::InvariantCulture)
            $expected += ('opacity="{0}"' -f ($o -replace '^0', ''))
        }
    }
    $missing = $expected | Where-Object { $svg -notmatch [regex]::Escape($_) }
    if ($missing) {
        throw ("superscroll-mark.svg is out of sync with this script. Not found in the SVG: " +
               ($missing -join ', '))
    }
}

Assert-SvgInSync

Write-Host 'Rendering SuperScroll branding...' -ForegroundColor Cyan

# --- icon.png: what Playnite ships. Transparent outside the rounded field. ---
$c = New-Canvas 256 256
Add-Mark $c[1] 0 0 256
Save-Canvas $c[0] $c[1] (Join-Path $root 'icon.png')

# --- banner.png: README lockup. ---
$W = 1200; $H = 320
$c = New-Canvas $W $H
$g = $c[1]
$bg = New-VerticalBrush ($W / 2) 0 $H $FIELD_TOP $FIELD_BOTTOM 255
$g.FillRectangle($bg, 0, 0, $W, $H)
$bg.Dispose()
Add-Glyph $g 160 160 150
$titleFont = New-Font 'Segoe UI Semibold' 72
$tagFont = New-Font 'Segoe UI' 26
$titleBrush = New-Object System.Drawing.SolidBrush((ConvertTo-Color $TEXT_PRIMARY))
$tagBrush = New-Object System.Drawing.SolidBrush((ConvertTo-Color $TEXT_MUTED))
$g.DrawString('SuperScroll', $titleFont, $titleBrush, 296, 92)
$g.DrawString('Smooth, pixel-accurate scrolling for Playnite', $tagFont, $tagBrush, 300, 194)
Save-Canvas $c[0] $g (Join-Path $assets 'banner.png')

# --- addon-header.png: Playnite addon listing / release art. ---
$W = 1280; $H = 720
$c = New-Canvas $W $H
$g = $c[1]
$bg = New-VerticalBrush ($W / 2) 0 $H $FIELD_TOP $FIELD_BOTTOM 255
$g.FillRectangle($bg, 0, 0, $W, $H)
$bg.Dispose()
Add-Glyph $g ($W / 2) 280 210
$centered = New-Object System.Drawing.StringFormat
$centered.Alignment = [System.Drawing.StringAlignment]::Center
$hTitle = New-Font 'Segoe UI Semibold' 88
$hTag = New-Font 'Segoe UI' 30
$g.DrawString('SuperScroll', $hTitle, $titleBrush, ($W / 2), 452, $centered)
$g.DrawString('Smooth, pixel-accurate scrolling for Playnite', $hTag, $tagBrush, ($W / 2), 570, $centered)
Save-Canvas $c[0] $g (Join-Path $assets 'addon-header.png')

$titleFont.Dispose(); $tagFont.Dispose(); $hTitle.Dispose(); $hTag.Dispose()
$titleBrush.Dispose(); $tagBrush.Dispose()
Write-Host 'Done.' -ForegroundColor Cyan
