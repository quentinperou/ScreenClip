<#
.SYNOPSIS
    Generates the application icon (.ico), its large in-app image, the README icon (.png) and an SVG copy of the
    design (docs\icons).
.DESCRIPTION
    The generated files are committed: run this script again only to change the design.
    Design (validated by the author on the mock-up, October 19, 2026): four white capture corners around a small
    white rounded window, on a rounded square with a violet-to-pink gradient (#7B61FF to #E5489A).
    Small sizes (16 to 40 px, the notification area and File Explorer) are drawn on whole pixels to stay sharp.
    Requires Windows PowerShell 5.1 (System.Drawing).
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot

$gradientStart = [System.Drawing.Color]::FromArgb(255, 0x7B, 0x61, 0xFF)
$gradientEnd = [System.Drawing.Color]::FromArgb(255, 0xE5, 0x48, 0x9A)

# The 256 px design: corners drawn with a 22 px round pen (each: a 24 px arm, a 24 px radius, a 24 px arm), the
# window 84 x 64 with a 12 px radius; background radius 23 % of its side, inside a 4 % transparent margin.
$design = @{
    Corners = @(
        @{ Start = @(56, 104); Arc = @(56, 56, 180); End = @(104, 56) },     # top left
        @{ Start = @(152, 56); Arc = @(152, 56, 270); End = @(200, 104) },   # top right
        @{ Start = @(200, 152); Arc = @(152, 152, 0); End = @(152, 200) },   # bottom right
        @{ Start = @(104, 200); Arc = @(56, 152, 90); End = @(56, 152) }     # bottom left
    )
    Pen = 22
    Window = @(86, 96, 84, 64); WindowRadius = 12
    Bg = 0.23; Margin = 0.04
}

# Under this size (taskbar, notification area, File Explorer lists), the design is put on whole pixels so that its
# lines stay sharp.
$smallUnder = 48

# The proportions of the 256 px design, on whole pixels for a small size: corners 20 % in from the edges, 8 % thick,
# arms 25 % long, outer corner radius 12 %; the window 30 x 23 % in the middle (an even margin on each side, and at
# least 2 px wider than high). Background radius 23 % and no transparent margin, as in the sister apps.
function Get-SmallSpec([int]$size) {
    function Center([double]$wanted) { $size - 2 * [Math]::Round(($size - $wanted) / 2) }
    $t = [Math]::Max(1, [Math]::Round($size * 0.08))
    $h = Center ($size * 0.23)
    $w = [Math]::Max((Center ($size * 0.30)), $h + 2)
    $ro = [Math]::Round($size * 0.12 * 2) / 2
    @{
        Bg = [Math]::Round($size * 0.23 * 2) / 2
        A = [Math]::Round($size * 0.2); L = [Math]::Round($size * 0.25); T = $t
        Ro = $ro; Ri = [Math]::Max(0, $ro - $t)
        Win = @((($size - $w) / 2), (($size - $h) / 2), $w, $h)
        WR = [Math]::Max(1, [Math]::Round($size * 0.06 * 2) / 2)
    }
}

# One corner of the small sizes, top left: an L of thickness T with rounded corners, square ends.
function New-SmallCornerPath($spec) {
    $a = [float]$spec.A; $l = [float]$spec.L; $t = [float]$spec.T; $ro = [float]$spec.Ro; $ri = [float]$spec.Ri
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddLine($a, ($a + $l), $a, ($a + $ro))
    $p.AddArc($a, $a, (2 * $ro), (2 * $ro), [float]180, [float]90)
    $p.AddLine(($a + $ro), $a, ($a + $l), $a)
    $p.AddLine(($a + $l), $a, ($a + $l), ($a + $t))
    $p.AddLine(($a + $l), ($a + $t), ($a + $t + $ri), ($a + $t))
    if ($ri -gt 0) { $p.AddArc(($a + $t), ($a + $t), (2 * $ri), (2 * $ri), [float]270, [float]-90) }
    $p.AddLine(($a + $t), ($a + $t + $ri), ($a + $t), ($a + $l))
    $p.CloseFigure()
    return $p
}

function New-RoundedRectPath([System.Drawing.RectangleF]$r, [float]$radius) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($radius -le 0) { $p.AddRectangle($r); return $p }
    $d = [Math]::Min($radius * 2, [Math]::Min($r.Width, $r.Height))
    $p.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

    if ($size -lt $smallUnder) {
        $spec = Get-SmallSpec $size
        $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
        $bg = New-RoundedRectPath $rect ([float]$spec.Bg)
    }
    else {
        $m = [float]($size * $design.Margin)
        $s = [float]($size - 2 * $m)
        $rect = New-Object System.Drawing.RectangleF $m, $m, $s, $s
        $bg = New-RoundedRectPath $rect ([float]($s * $design.Bg))
    }
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $gradientStart, $gradientEnd, ([float]45)
    $g.FillPath($brush, $bg)

    if ($size -lt $smallUnder) {
        # The top left corner, then the same shape mirrored for the three others: straight edges on pixel edges.
        $mirrors = @(@(1, 1, 0, 0), @(-1, 1, $size, 0), @(-1, -1, $size, $size), @(1, -1, 0, $size))
        foreach ($m in $mirrors) {
            $corner = New-SmallCornerPath $spec
            $matrix = New-Object System.Drawing.Drawing2D.Matrix ([float]$m[0]), 0, 0, ([float]$m[1]), ([float]$m[2]), ([float]$m[3])
            $corner.Transform($matrix)
            $g.FillPath($white, $corner)
            $corner.Dispose(); $matrix.Dispose()
        }
        $w = $spec.Win
        $window = New-RoundedRectPath (New-Object System.Drawing.RectangleF $w[0], $w[1], $w[2], $w[3]) ([float]$spec.WR)
        $g.FillPath($white, $window)
        $window.Dispose()
    }
    else {
        $k = $s / 256
        $g.TranslateTransform($m, $m)
        $g.ScaleTransform($k, $k)
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([float]$design.Pen)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        foreach ($c in $design.Corners) {
            $p = New-Object System.Drawing.Drawing2D.GraphicsPath
            $p.AddLine([float]$c.Start[0], [float]$c.Start[1], [float]$c.Start[0], [float]$c.Start[1])
            $p.AddArc([float]$c.Arc[0], [float]$c.Arc[1], [float]48, [float]48, [float]$c.Arc[2], [float]90)
            $p.AddLine([float]$c.End[0], [float]$c.End[1], [float]$c.End[0], [float]$c.End[1])
            $g.DrawPath($pen, $p)
            $p.Dispose()
        }
        $w = $design.Window
        $window = New-RoundedRectPath (New-Object System.Drawing.RectangleF $w[0], $w[1], $w[2], $w[3]) ([float]$design.WindowRadius)
        $g.FillPath($white, $window)
        $window.Dispose(); $pen.Dispose()
    }

    $brush.Dispose(); $bg.Dispose(); $white.Dispose(); $g.Dispose()
    return $bmp
}

# ---------------------------------------------------------------- Files

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

function Save-Png([int]$size, [string]$path) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
    $bmp = New-IconBitmap $size
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  $path"
}

# ICO file made of PNG-compressed entries (supported since Windows Vista).
function Save-Ico([int[]]$sizes, [string]$path) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
    $images = foreach ($size in $sizes) { $b = New-IconBitmap $size; , (Get-PngBytes $b); $b.Dispose() }
    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([UInt16]1); $bw.Write([UInt16]32)
        $bw.Write([UInt32]$images[$i].Length); $bw.Write([UInt32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($img in $images) { $bw.Write([byte[]]$img) }
    $bw.Dispose()
    Write-Host "  $path"
}

# The 256 px design as SVG (docs\icons), to rework the icon in any vector editor. The app does not use it, and the
# small sizes stay drawn on whole pixels by this script.
function Get-Hex([System.Drawing.Color]$c) { '#' + $c.R.ToString('X2') + $c.G.ToString('X2') + $c.B.ToString('X2') }

function Get-AppSvg {
    $m = 256 * $design.Margin
    $k = (256 - 2 * $m) / 256
    $w = $design.Window
    @"
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <!-- ScreenClip app icon, 256 px design. Written by scripts/make-icons.ps1: change the design there. -->
  <defs>
    <linearGradient id="gradient" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="$(Get-Hex $gradientStart)" />
      <stop offset="1" stop-color="$(Get-Hex $gradientEnd)" />
    </linearGradient>
  </defs>
  <g transform="translate($m $m) scale($k)">
    <rect width="256" height="256" rx="$(256 * $design.Bg)" fill="url(#gradient)" />
    <path d="M56 104V80a24 24 0 0 1 24-24h24M152 56h24a24 24 0 0 1 24 24v24M200 152v24a24 24 0 0 1-24 24h-24M104 200H80a24 24 0 0 1-24-24v-24"
          fill="none" stroke="#FFFFFF" stroke-width="$($design.Pen)" stroke-linecap="round" stroke-linejoin="round" />
    <rect x="$($w[0])" y="$($w[1])" width="$($w[2])" height="$($w[3])" rx="$($design.WindowRadius)" fill="#FFFFFF" />
  </g>
</svg>
"@
}

function Save-Svg([string]$path, [string]$svg) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
    [System.IO.File]::WriteAllText($path, $svg.Replace("`r`n", "`n") + "`n", (New-Object System.Text.UTF8Encoding $false))
    Write-Host "  $path"
}

Write-Host 'Generated icons:'
Save-Ico @(16, 20, 24, 30, 32, 36, 40, 48, 64, 256) (Join-Path $root 'src\ScreenClip\Assets\ScreenClip.ico')
Save-Png 256 (Join-Path $root 'docs\icon-256.png')
# Large image shown inside the app (About card, Settings title): an .ico would display its first, 16 px, frame.
Save-Png 256 (Join-Path $root 'src\ScreenClip\Assets\ScreenClip.png')
Save-Svg (Join-Path $root 'docs\icons\ScreenClip.svg') (Get-AppSvg)
