$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bmp = [Drawing.Bitmap]::new(32,32)
$g = [Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$white = [Drawing.Brushes]::White
$dark = [Drawing.Brushes]::DarkSlateGray
$green = [Drawing.Brushes]::MediumSeaGreen
$g.FillEllipse($white,1,2,14,13); $g.FillEllipse($white,17,2,14,13)
$g.FillRectangle($white,8,10,5,17); $g.FillRectangle($white,19,10,5,17)
$g.FillEllipse($white,8,24,5,5); $g.FillEllipse($white,19,24,5,5)
$g.FillEllipse($dark,3,6,4,6); $g.FillEllipse($dark,25,6,4,6)
$g.FillEllipse($green,11,20,11,11)
$icon = [Drawing.Icon]::FromHandle($bmp.GetHicon())
$stream = [IO.File]::Create((Join-Path $PSScriptRoot 'Tray\AirPods.ico'))
try { $icon.Save($stream) } finally { $stream.Dispose(); $icon.Dispose(); $g.Dispose(); $bmp.Dispose() }
