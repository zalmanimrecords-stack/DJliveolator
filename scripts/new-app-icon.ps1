<#
.SYNOPSIS
    Regenerates src/Liveolator.App/Liveolator.ico - the Windows app/installer icon.

.DESCRIPTION
    Renders the Liveolator mark (deep-navy rounded tile, accent-blue "L" with a beat-bar
    underline - the docs/19 design line: navy + single blue accent #2F80F6) at the standard
    Windows icon sizes and packs them into one multi-image .ico (PNG-compressed entries,
    valid on Vista+). Run only when the branding changes; the .ico is committed.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/new-app-icon.ps1
#>
[CmdletBinding()]
param(
    [string]$OutPath
)

$ErrorActionPreference = 'Stop'
# $PSScriptRoot is not populated in param defaults under Windows PowerShell 5.1 - resolve here.
if (-not $OutPath) {
    $OutPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/Liveolator.App/Liveolator.ico'
}
Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$Size) {
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        $navy = [System.Drawing.Color]::FromArgb(255, 13, 21, 38)     # deep-navy tile (docs/19 surface)
        $edge = [System.Drawing.Color]::FromArgb(255, 33, 48, 77)     # hairline edge
        $blue = [System.Drawing.Color]::FromArgb(255, 47, 128, 246)   # the single accent #2F80F6

        # Rounded tile
        $r = [Math]::Max(2, [int]($Size * 0.18))
        $rect = New-Object System.Drawing.Rectangle(0, 0, ($Size - 1), ($Size - 1))
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $r * 2
        $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
        $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
        $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
        $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
        $path.CloseFigure()

        $bgBrush = New-Object System.Drawing.SolidBrush($navy)
        $g.FillPath($bgBrush, $path)
        if ($Size -ge 24) {
            $edgePen = New-Object System.Drawing.Pen($edge, [Math]::Max(1, $Size / 64))
            $g.DrawPath($edgePen, $path)
            $edgePen.Dispose()
        }
        $bgBrush.Dispose()
        $path.Dispose()

        # The "L" - two thick accent strokes (legible down to 16px, no font dependency)
        $blueBrush = New-Object System.Drawing.SolidBrush($blue)
        $stroke = [Math]::Max(2, [int]($Size * 0.16))
        $left   = [int]($Size * 0.28)
        $top    = [int]($Size * 0.22)
        $bottom = [int]($Size * 0.78)
        $right  = [int]($Size * 0.72)
        $g.FillRectangle($blueBrush, $left, $top, $stroke, ($bottom - $top))                       # vertical
        $g.FillRectangle($blueBrush, $left, ($bottom - $stroke), ($right - $left), $stroke)        # horizontal
        $blueBrush.Dispose()

        # Beat-bar ticks above the L's foot (the audio<->visual clock motif), larger sizes only
        if ($Size -ge 48) {
            $tickBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(190, 47, 128, 246))
            $tw = [Math]::Max(1, [int]($Size * 0.045))
            $gap = [int]($Size * 0.10)
            $bx = $left + $stroke + $gap
            $heights = @(0.16, 0.26, 0.20, 0.32)
            foreach ($h in $heights) {
                if (($bx + $tw) -gt $right) { break }
                $th = [int]($Size * $h)
                $g.FillRectangle($tickBrush, $bx, ($bottom - $stroke - $gap - $th), $tw, $th)
                $bx += $gap
            }
            $tickBrush.Dispose()
        }
    }
    finally { $g.Dispose() }

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    # Leading comma keeps the byte[] intact - PowerShell would otherwise unroll it.
    return , $ms.ToArray()
}

# Pack PNGs into an .ico container: ICONDIR + ICONDIRENTRY[] + image data
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($s in $sizes) { $images.Add([byte[]](New-IconPng $s)) }

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([uint16]0)               # reserved
$w.Write([uint16]1)               # type: icon
$w.Write([uint16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # width (0 = 256)
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # height
    $w.Write([byte]0)             # palette
    $w.Write([byte]0)             # reserved
    $w.Write([uint16]1)           # planes
    $w.Write([uint16]32)          # bpp
    $w.Write([uint32]$images[$i].Length)
    $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write([byte[]]$img) }
$w.Flush()

[System.IO.File]::WriteAllBytes($OutPath, $out.ToArray())
$w.Dispose(); $out.Dispose()
Write-Host "Wrote $OutPath ($([Math]::Round((Get-Item $OutPath).Length / 1KB, 1)) KB, sizes: $($sizes -join ', '))"
