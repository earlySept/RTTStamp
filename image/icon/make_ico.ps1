Add-Type -AssemblyName System.Drawing

# 生成指定尺寸的图标 PNG（深色圆角块 + ">_"）
function New-IconPng([int]$size, [string]$path) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Max(4, [int]($size * 0.24))
    $gp.AddArc(0, 0, $d, $d, 180, 90)
    $gp.AddArc($size - $d, 0, $d, $d, 270, 90)
    $gp.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $gp.AddArc(0, $size - $d, $d, $d, 90, 90)
    $gp.CloseFigure()
    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(27, 31, 39))
    $g.FillPath($bg, $gp)
    $gp.Dispose()
    $bg.Dispose()

    $font = New-Object System.Drawing.Font('Consolas', ($size * 0.40), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF(0, ($size * 0.04), $size, $size)
    $g.DrawString('>_', $font, [System.Drawing.Brushes]::White, $rect, $sf)
    $font.Dispose(); $sf.Dispose(); $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$dir = $PSScriptRoot
$pngFiles = @()
foreach ($s in $sizes) {
    $pf = Join-Path $dir ("ico_{0}.png" -f $s)
    New-IconPng $s $pf
    $pngFiles += ,@( $s, $pf )
}

# 组装 ICO 容器（PNG 帧）
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([UInt16]0)            # reserved
$bw.Write([UInt16]1)            # type = icon
$bw.Write([UInt16]$pngFiles.Count)
$offset = 6 + 16 * $pngFiles.Count
foreach ($entry in $pngFiles) {
    $s = $entry[0]
    $bytes = [System.IO.File]::ReadAllBytes($entry[1])
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim)       # width (0 = 256)
    $bw.Write([byte]$dim)       # height
    $bw.Write([byte]0)          # palette
    $bw.Write([byte]0)          # reserved
    $bw.Write([UInt16]1)        # planes
    $bw.Write([UInt16]32)       # bpp
    $bw.Write([UInt32]$bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($entry in $pngFiles) {
    $bw.Write([System.IO.File]::ReadAllBytes($entry[1]))
}
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $dir 'app.ico'), $ms.ToArray())
$bw.Dispose(); $ms.Dispose()

# 清理临时 png（保留 256 预览）
foreach ($entry in $pngFiles) {
    if ($entry[0] -ne 256) { Remove-Item $entry[1] -ErrorAction SilentlyContinue }
}
Rename-Item (Join-Path $dir 'ico_256.png') 'icon_preview.png' -ErrorAction SilentlyContinue
Write-Output ("app.ico created: " + (Get-Item (Join-Path $dir 'app.ico')).Length + " bytes")
