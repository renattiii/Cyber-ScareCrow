param(
    [string]$OutPath = (Join-Path $PSScriptRoot "..\src\CyberScarecrow.App\assets\scarecrow.ico")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-RoundedPath {
    param(
        [System.Drawing.Rectangle]$Rect,
        [int]$Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($Rect.X, $Rect.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Rect.Right - $diameter, $Rect.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Rect.Right - $diameter, $Rect.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Rect.X, $Rect.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$size = 256
$bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$graphics.Clear([System.Drawing.Color]::Transparent)

# Rounded dark tile.
$tileRect = New-Object System.Drawing.Rectangle(8, 8, 240, 240)
$tilePath = New-RoundedPath -Rect $tileRect -Radius 52
$tileBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 18, 20, 24))
$graphics.FillPath($tileBrush, $tilePath)
$tilePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 45, 49, 58), 2)
$graphics.DrawPath($tilePen, $tilePath)

# Warm glow behind the scarecrow.
$glowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(34, 245, 158, 11))
$graphics.FillEllipse($glowBrush, 42, 42, 172, 172)

# Post and arms.
$woodBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 120, 72, 32))
$graphics.FillRectangle($woodBrush, 120, 176, 16, 68)
$graphics.FillRectangle($woodBrush, 52, 190, 152, 14)

# Face.
$orange = [System.Drawing.Color]::FromArgb(255, 245, 158, 11)
$faceBrush = New-Object System.Drawing.SolidBrush($orange)
$graphics.FillEllipse($faceBrush, 72, 84, 112, 112)

# Hat brim and crown.
$hatBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 104, 61, 26))
$graphics.FillEllipse($hatBrush, 50, 86, 156, 32)

$crown = New-Object System.Drawing.Drawing2D.GraphicsPath
$crownPoints = [System.Drawing.Point[]]@(
    [System.Drawing.Point]::new(88, 42),
    [System.Drawing.Point]::new(168, 42),
    [System.Drawing.Point]::new(156, 92),
    [System.Drawing.Point]::new(100, 92)
)
$crown.AddPolygon($crownPoints)
$graphics.FillPath($hatBrush, $crown)

# Straw poking out of the brim.
$strawPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 234, 179, 8), 4)
$graphics.DrawLine($strawPen, 62, 98, 42, 106)
$graphics.DrawLine($strawPen, 194, 98, 214, 106)
$graphics.DrawLine($strawPen, 58, 108, 40, 120)
$graphics.DrawLine($strawPen, 198, 108, 216, 120)

# Eyes and stitched mouth.
$inkBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 18, 20, 24))
$graphics.FillEllipse($inkBrush, 100, 124, 16, 20)
$graphics.FillEllipse($inkBrush, 140, 124, 16, 20)

$mouthPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 18, 20, 24), 6)
$graphics.DrawLine($mouthPen, 108, 164, 148, 164)
for ($x = 114; $x -le 142; $x += 10) {
    $graphics.DrawLine($mouthPen, $x, 157, $x, 171)
}

# Cheek stitches.
$stitchPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 120, 72, 32), 3)
$graphics.DrawLine($stitchPen, 88, 148, 98, 148)
$graphics.DrawLine($stitchPen, 158, 148, 168, 148)

# Small amber badge in the corner.
$badgeBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 245, 158, 11))
$graphics.FillEllipse($badgeBrush, 182, 182, 42, 42)
$badgePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 18, 20, 24), 5)
$graphics.DrawLine($badgePen, 192, 203, 214, 203)
$graphics.DrawLine($badgePen, 203, 192, 203, 214)

# Build the multi-resolution ICO using uncompressed 32-bit DIB frames.
# .NET's System.Drawing.Icon does not reliably read PNG-compressed ICO frames,
# so DIB frames keep both Windows and WinForms happy.
$frames = New-Object System.Collections.Generic.List[object]
foreach ($frameSize in @(16, 24, 32, 48, 64, 128, 256)) {
    $frame = New-Object System.Drawing.Bitmap($frameSize, $frameSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $frameGraphics = [System.Drawing.Graphics]::FromImage($frame)
    $frameGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $frameGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $frameGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $frameGraphics.DrawImage($bitmap, 0, 0, $frameSize, $frameSize)

    $rect = New-Object System.Drawing.Rectangle(0, 0, $frameSize, $frameSize)
    $locked = $frame.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $locked.Stride
    $raw = New-Object byte[] ($stride * $frameSize)
    [System.Runtime.InteropServices.Marshal]::Copy($locked.Scan0, $raw, 0, $raw.Length)
    $frame.UnlockBits($locked)

    # DIB rows are bottom-up.
    $xor = New-Object byte[] ($frameSize * $frameSize * 4)
    for ($y = 0; $y -lt $frameSize; $y++) {
        $sourceRow = ($frameSize - 1 - $y) * $stride
        $destinationRow = $y * $frameSize * 4
        [Array]::Copy($raw, $sourceRow, $xor, $destinationRow, $frameSize * 4)
    }

    # 1-bit AND mask, padded to 4-byte rows. Alpha handles transparency.
    $maskStride = [Math]::Floor(($frameSize + 31) / 32) * 4
    $mask = New-Object byte[] ($maskStride * $frameSize)

    $imageStream = New-Object System.IO.MemoryStream
    $imageWriter = New-Object System.IO.BinaryWriter($imageStream)
    $imageWriter.Write([UInt32]40)                                  # biSize
    $imageWriter.Write([Int32]$frameSize)                           # biWidth
    $imageWriter.Write([Int32]($frameSize * 2))                     # biHeight (XOR + AND)
    $imageWriter.Write([UInt16]1)                                   # biPlanes
    $imageWriter.Write([UInt16]32)                                  # biBitCount
    $imageWriter.Write([UInt32]0)                                   # biCompression
    $imageWriter.Write([UInt32]($frameSize * $frameSize * 4))       # biSizeImage
    $imageWriter.Write([Int32]0)                                    # biXPelsPerMeter
    $imageWriter.Write([Int32]0)                                    # biYPelsPerMeter
    $imageWriter.Write([UInt32]0)                                   # biClrUsed
    $imageWriter.Write([UInt32]0)                                   # biClrImportant
    $imageWriter.Write($xor)
    $imageWriter.Write($mask)
    $imageWriter.Dispose()

    $frames.Add([pscustomobject]@{ Size = $frameSize; Bytes = $imageStream.ToArray() })
    $imageStream.Dispose()
    $frameGraphics.Dispose()
    $frame.Dispose()
}

$directory = Split-Path -Parent $OutPath
New-Item -ItemType Directory -Force -Path $directory | Out-Null

$fileStream = [System.IO.File]::Create($OutPath)
$writer = New-Object System.IO.BinaryWriter($fileStream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$frames.Count)

$offset = 6 + (16 * $frames.Count)
foreach ($frame in $frames) {
    $dimension = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
    $writer.Write([Byte]$dimension)
    $writer.Write([Byte]$dimension)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frame.Bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $frame.Bytes.Length
}

foreach ($frame in $frames) {
    $writer.Write($frame.Bytes)
}

$writer.Dispose()
$fileStream.Dispose()

$graphics.Dispose()
$bitmap.Dispose()

Write-Host "Wrote $OutPath"
