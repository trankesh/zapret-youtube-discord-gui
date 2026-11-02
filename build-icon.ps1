$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$frames = @()
foreach ($size in @(16,32,48,64,128,256)) {
    $bitmap = [Drawing.Bitmap]::new($size,$size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 64.0, $size / 64.0)
    $tile = [Drawing.Drawing2D.GraphicsPath]::new()
    $tile.AddArc(0,0,24,24,180,90); $tile.AddArc(40,0,24,24,270,90); $tile.AddArc(40,40,24,24,0,90); $tile.AddArc(0,40,24,24,90,90); $tile.CloseFigure()
    $brush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(17,17,21)); $graphics.FillPath($brush,$tile)
    $ring = [Drawing.Pen]::new([Drawing.Color]::FromArgb(255,173,88),4)
    $ring.StartCap = $ring.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawArc($ring,10,10,44,44,-50,300)
    $letter = [Drawing.Pen]::new([Drawing.Color]::FromArgb(243,243,247),4.2)
    $letter.StartCap = $letter.EndCap = [Drawing.Drawing2D.LineCap]::Round; $letter.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $points = [Drawing.PointF[]]@([Drawing.PointF]::new(23,23),[Drawing.PointF]::new(42,23),[Drawing.PointF]::new(23,41),[Drawing.PointF]::new(42,41))
    $graphics.DrawLines($letter,$points)
    $memory = [IO.MemoryStream]::new(); $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
    $frames += @{ Size=$size; Bytes=$memory.ToArray() }
    if ($size -eq 256) { $bitmap.Save((Join-Path $PSScriptRoot 'assets\app.png'),[Drawing.Imaging.ImageFormat]::Png) }
    $memory.Dispose(); $letter.Dispose(); $ring.Dispose(); $brush.Dispose(); $tile.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$file = [IO.File]::Create((Join-Path $PSScriptRoot 'assets\app.ico')); $writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
