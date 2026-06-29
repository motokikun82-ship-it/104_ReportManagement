param([string]$OutputPath = "app.ico")

Add-Type -AssemblyName System.Drawing

$size = 32
$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'HighQuality'
$g.Clear([System.Drawing.Color]::FromArgb(0, 0, 0, 0))

$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0x00, 0xC8, 0x53))
$g.FillEllipse($brush, 4, 4, 24, 24)

$font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$brush2 = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$g.DrawString("RM", $font, $brush2, 5, 5)
$g.Dispose()

$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmpData = $ms.ToArray()
$ms.Dispose()
$bmp.Dispose()

$pixelOffset = 54
$pixelSize = $size * $size * 4
$maskRowBytes = [math]::Ceiling($size / 8)
$maskSize = $maskRowBytes * $size
$dibSize = 40 + $pixelSize + $maskSize
$icoSize = 22 + $dibSize
$ico = New-Object byte[] $icoSize

$sw = [System.IO.BinaryWriter]::new([System.IO.MemoryStream]::new($ico))
# ICO header
$sw.Write([UInt16]0); $sw.Write([UInt16]1); $sw.Write([UInt16]1)
# Directory entry
$sw.Write([byte]32); $sw.Write([byte]32)
$sw.Write([byte]0); $sw.Write([byte]0)
$sw.Write([UInt16]1); $sw.Write([UInt16]32)
$sw.Write([Int32]$dibSize); $sw.Write([Int32]22)
# BITMAPINFOHEADER
$sw.Write([Int32]40); $sw.Write([Int32]$size); $sw.Write([Int32]($size * 2))
$sw.Write([UInt16]1); $sw.Write([UInt16]32)
$sw.Write([Int32]0); $sw.Write([Int32]0)
$sw.Write([Int32]0); $sw.Write([Int32]0)
$sw.Write([Int32]0); $sw.Write([Int32]0)
# Pixel data
$sw.Write($bmpData, $pixelOffset, $pixelSize)
# AND mask
$sw.Write([byte[]]::new($maskSize), 0, $maskSize)
$sw.Dispose()

[System.IO.File]::WriteAllBytes($OutputPath, $ico)
Write-Host "Generated $OutputPath"
