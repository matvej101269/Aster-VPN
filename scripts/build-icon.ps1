$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
$images = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([Drawing.Color]::Transparent)
    $dark = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#151811'))
    $lime = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#C2DF51'))
    $g.FillEllipse($dark, $size * .03, $size * .03, $size * .94, $size * .94)
    $points = [Drawing.PointF[]]@(
        [Drawing.PointF]::new($size*.5, $size*.13),
        [Drawing.PointF]::new($size*.61, $size*.39),
        [Drawing.PointF]::new($size*.87, $size*.5),
        [Drawing.PointF]::new($size*.61, $size*.61),
        [Drawing.PointF]::new($size*.5, $size*.87),
        [Drawing.PointF]::new($size*.39, $size*.61),
        [Drawing.PointF]::new($size*.13, $size*.5),
        [Drawing.PointF]::new($size*.39, $size*.39))
    $g.FillPolygon($lime, $points)
    $g.FillEllipse($dark, $size*.44, $size*.44, $size*.12, $size*.12)
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    if ($size -eq 256) { $bitmap.Save((Join-Path $assets 'aster.png'), [Drawing.Imaging.ImageFormat]::Png) }
    $images += ,@{ Size = $size; Data = $stream.ToArray() }
    $stream.Dispose(); $g.Dispose(); $bitmap.Dispose(); $dark.Dispose(); $lime.Dispose()
}
$file = [IO.File]::Create((Join-Path $assets 'aster.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($entry in $images) {
        $dimension = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$entry.Data.Length); $writer.Write([uint32]$offset)
        $offset += $entry.Data.Length
    }
    foreach ($entry in $images) { $writer.Write([byte[]]$entry.Data) }
} finally { $writer.Dispose(); $file.Dispose() }
