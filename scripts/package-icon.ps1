$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectDirectory = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $projectDirectory 'native\Assets\Icon'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
# A small vector-drawn application mark. Hardware raster sprites are Forge assets.
$frames = @()
foreach ($size in @(16,32,48,128,256)) {
    $bitmap = New-Object Drawing.Bitmap($size,$size)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.ScaleTransform($size/256.0,$size/256.0)
    $g.Clear([Drawing.Color]::Transparent)
    $dark = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#30363D'))
    $ivory = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#E3E7EB'))
    $red = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#565F69'))
    $glass = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#88929D'))
    $g.FillRectangle($ivory,22,35,212,174)
    $g.FillRectangle($dark,32,45,192,154)
    $g.FillRectangle($glass,45,58,135,113)
    $g.FillEllipse($ivory,190,62,20,20)
    $g.FillEllipse($ivory,190,99,20,20)
    $g.FillRectangle($red,55,178,145,49)
    $g.FillRectangle($ivory,68,185,118,29)
    $g.FillRectangle($dark,84,190,9,21)
    $g.FillRectangle($dark,78,196,21,9)
    $g.FillEllipse($red,148,194,12,12)
    $g.FillEllipse($red,165,194,12,12)
    $stream = New-Object IO.MemoryStream
    $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@($size,$stream.ToArray())
    $stream.Dispose(); $g.Dispose(); $bitmap.Dispose(); $dark.Dispose(); $ivory.Dispose(); $red.Dispose(); $glass.Dispose()
}
$file = [IO.File]::Create((Join-Path $outDir 'FamicomPlayer.ico'))
$writer = New-Object IO.BinaryWriter($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
$offset = 6 + 16*$frames.Count
foreach ($frame in $frames) {
    $size = $frame[0]; $bytes = $frame[1]
    $writer.Write([byte]($size % 256)); $writer.Write([byte]($size % 256)); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$bytes.Length); $writer.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame[1]) }
$writer.Dispose()
