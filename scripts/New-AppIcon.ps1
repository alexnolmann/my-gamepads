# Rebuild the original vector-style icon using Windows GDI+; no external packages.
Add-Type -AssemblyName System.Drawing
$iconRoot = Join-Path $PSScriptRoot '../src/MyGamepads/Assets'
New-Item -ItemType Directory -Path $iconRoot -Force | Out-Null
$frames = [Collections.Generic.List[byte[]]]::new()
$iconSizes = @(16,20,24,32,40,48,64,128,256)
foreach ($iconSize in $iconSizes + @(512)) {
    $bitmap = [Drawing.Bitmap]::new($iconSize,$iconSize,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($iconSize/256.0,$iconSize/256.0)
    $dark = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#131822'))
    $mint = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#5BDEAF'))
    $background = [Drawing.Drawing2D.GraphicsPath]::new()
    $background.AddArc(8,8,104,104,180,90)
    $background.AddArc(144,8,104,104,270,90)
    $background.AddArc(144,144,104,104,0,90)
    $background.AddArc(8,144,104,104,90,90)
    $background.CloseFigure()
    $graphics.FillPath($dark,$background)
    $pad = [Drawing.Drawing2D.GraphicsPath]::new()
    $pad.StartFigure()
    $pad.AddBezier(85,72,64,72,51,87,45,110)
    $pad.AddLine(45,110,29,166)
    $pad.AddBezier(29,166,23,188,46,201,63,184)
    $pad.AddLine(63,184,87,159)
    $pad.AddLine(87,159,169,159)
    $pad.AddLine(169,159,193,184)
    $pad.AddBezier(193,184,210,201,233,188,227,166)
    $pad.AddLine(227,166,211,110)
    $pad.AddBezier(211,110,205,87,192,72,171,72)
    $pad.CloseFigure()
    $graphics.FillPath($mint,$pad)
    $graphics.FillRectangle($dark,54,106,42,14)
    $graphics.FillRectangle($dark,68,92,14,42)
    foreach ($circle in @(@(178,95,7),@(196,113,7),@(178,131,7),@(160,113,7),@(107,140,10),@(143,140,10))) {
        $graphics.FillEllipse($dark,$circle[0]-$circle[2],$circle[1]-$circle[2],2*$circle[2],2*$circle[2])
    }
    $graphics.FillRectangle($dark,109,95,12,5)
    $graphics.FillRectangle($dark,135,95,12,5)
    $memory = [IO.MemoryStream]::new()
    $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
    if ($iconSize -eq 512) { [IO.File]::WriteAllBytes((Join-Path $iconRoot 'app-preview.png'),$memory.ToArray()) }
    else { $frames.Add($memory.ToArray()) }
    $memory.Dispose();$pad.Dispose();$background.Dispose();$dark.Dispose();$mint.Dispose();$graphics.Dispose();$bitmap.Dispose()
}
$icoFile = [IO.File]::Create((Join-Path $iconRoot 'app.ico'))
$writer = [IO.BinaryWriter]::new($icoFile)
try {
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
    $offset = 6 + 16*$frames.Count
    for ($frameIndex=0; $frameIndex -lt $frames.Count; $frameIndex++) {
        $dimension = if ($iconSizes[$frameIndex] -eq 256) { 0 } else { $iconSizes[$frameIndex] }
        $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
        $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frames[$frameIndex].Length);$writer.Write([uint32]$offset)
        $offset += $frames[$frameIndex].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
} finally { $writer.Dispose();$icoFile.Dispose() }
