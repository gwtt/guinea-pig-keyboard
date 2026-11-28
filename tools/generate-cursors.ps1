$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$artDir = Join-Path $PSScriptRoot 'cursor-art'
$outDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets/cursors'
$roles = @('arrow', 'hand', 'ibeam', 'busy', 'resize-ns', 'resize-we', 'resize-nwse', 'resize-nesw')
$format = [Drawing.Imaging.PixelFormat]::Format32bppArgb

function Resize-Frame($image, [int]$size, [bool]$pixelArt) {
    $frame = [Drawing.Bitmap]::new($size, $size, $format)
    $g = [Drawing.Graphics]::FromImage($frame)
    try {
        $g.InterpolationMode = if ($pixelArt) { 'NearestNeighbor' } else { 'HighQualityBicubic' }
        $g.PixelOffsetMode = 'Half'
        $g.DrawImage($image, [Drawing.Rectangle]::new(0, 0, $size, $size))
    } finally {
        $g.Dispose()
    }
    return $frame
}

function Add-LightEdge($frame) {
    $width = $frame.Width
    $edged = [Drawing.Bitmap]::new($width, $width, $format)
    # A warm outer pixel keeps the dark ink readable on a dark desktop.
    for ($y = 1; $y -lt $width - 1; $y++) {
        for ($x = 1; $x -lt $width - 1; $x++) {
            $alpha = [Math]::Max($frame.GetPixel($x - 1, $y).A, $frame.GetPixel($x + 1, $y).A)
            $alpha = [Math]::Max($alpha, $frame.GetPixel($x, $y - 1).A)
            $alpha = [Math]::Max($alpha, $frame.GetPixel($x, $y + 1).A)
            if ($alpha -gt 8) {
                $edged.SetPixel($x, $y, [Drawing.Color]::FromArgb([int]($alpha * 0.86), 255, 248, 231))
            }
        }
    }
    $g = [Drawing.Graphics]::FromImage($edged)
    try { $g.DrawImageUnscaled($frame, 0, 0) } finally { $g.Dispose() }
    return $edged
}

function Export-Cursor($strip, [int]$index, [bool]$pixelArt, $path) {
    $side = $strip.Height
    $nativeSize = if ($pixelArt) { 32 } else { 64 }
    $source = $frame = $edged = $enlarged = $null
    try {
        $source = $strip.Clone([Drawing.Rectangle]::new($index * $side, 0, $side, $side), $format)
        $frame = Resize-Frame $source $nativeSize $pixelArt
        $edged = Add-LightEdge $frame
        $output = $edged
        if ($pixelArt) {
            $enlarged = Resize-Frame $edged 64 $true
            $output = $enlarged
        }
        $output.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    } finally {
        foreach ($bitmap in $source, $frame, $edged, $enlarged) {
            if ($null -ne $bitmap) { $bitmap.Dispose() }
        }
    }
}

foreach ($theme in 'tricolor', 'carrot', 'pixel') {
    $strip = [Drawing.Bitmap]::new((Join-Path $artDir "$theme.png"))
    try {
        $side = $strip.Height
        if ($strip.Width -ne $side * $roles.Count) { throw "Expected $($roles.Count) square frames in $theme.png" }
        $pixelArt = $theme -eq 'pixel'
        $dest = Join-Path $outDir $theme
        $null = [IO.Directory]::CreateDirectory($dest)
        for ($i = 0; $i -lt $roles.Count; $i++) {
            Export-Cursor $strip $i $pixelArt (Join-Path $dest ($roles[$i] + '.png'))
        }
    } finally { $strip.Dispose() }
}
Write-Host 'Generated 3 cursor themes.'
