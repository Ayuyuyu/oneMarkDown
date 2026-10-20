<#
.SYNOPSIS
    生成 OneMarkDown 的托盘/程序图标（多尺寸 ICO）。

.DESCRIPTION
    设计：圆角深蓝渐变底 + 白色 Markdown 标记（M + 向下箭头）。
    每个尺寸单独用矢量重绘，不靠缩放，16x16 也清晰。

    ICO 内部使用标准 DIB 帧（BITMAPINFOHEADER + 32 位 BGRA + AND 掩码），
    而不是 PNG 压缩帧 —— 因为 System.Drawing.Icon 对 PNG 帧支持不完整，
    new Icon(path, 16, 16) 会抛「参数 picture 必须是可用作 Icon 的图片」。
    DIB 帧在 GDI+、资源管理器、托盘三处都稳。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\make-icon.ps1
#>
[CmdletBinding()]
param(
    [string]$Output,
    [string]$PreviewPng
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not $Output) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $Output = Join-Path $repoRoot 'src\OneMarkDown.Shell\app.ico'
}

$sizes = @(256, 128, 64, 48, 32, 24, 16)

function New-IconBitmap {
    param([int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1.0, $Size * 0.06)
    # New-Object 是命令参数模式，带 - 和 * 的表达式会解析错，所以先算好。
    $inner = $Size - 2 * $pad
    $box = New-Object System.Drawing.RectangleF($pad, $pad, $inner, $inner)
    $radius = $Size * 0.22

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($box.X, $box.Y, $d, $d, 180, 90)
    $path.AddArc($box.Right - $d, $box.Y, $d, $d, 270, 90)
    $path.AddArc($box.Right - $d, $box.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($box.X, $box.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF($Size, $Size)),
        [System.Drawing.Color]::FromArgb(255, 61, 111, 191),
        [System.Drawing.Color]::FromArgb(255, 31, 56, 100))
    $g.FillPath($brush, $path)
    $brush.Dispose()
    $path.Dispose()

    $stroke = [Math]::Max(1.2, $Size * 0.082)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $stroke)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $sx = { param($v) [float]($v * $Size) }

    if ($Size -le 16) {
        # 16x16 放不下两个字形，只保留居中的 M，托盘里才认得出。
        $points = @(
            (New-Object System.Drawing.PointF((& $sx 0.235), (& $sx 0.72))),
            (New-Object System.Drawing.PointF((& $sx 0.235), (& $sx 0.27))),
            (New-Object System.Drawing.PointF((& $sx 0.50), (& $sx 0.55))),
            (New-Object System.Drawing.PointF((& $sx 0.765), (& $sx 0.27))),
            (New-Object System.Drawing.PointF((& $sx 0.765), (& $sx 0.72)))
        )
        $g.DrawLines($pen, $points)
    }
    else {
        $points = @(
            (New-Object System.Drawing.PointF((& $sx 0.19), (& $sx 0.71))),
            (New-Object System.Drawing.PointF((& $sx 0.19), (& $sx 0.29))),
            (New-Object System.Drawing.PointF((& $sx 0.395), (& $sx 0.52))),
            (New-Object System.Drawing.PointF((& $sx 0.60), (& $sx 0.29))),
            (New-Object System.Drawing.PointF((& $sx 0.60), (& $sx 0.71)))
        )
        $g.DrawLines($pen, $points)

        $shaftTop = New-Object System.Drawing.PointF((& $sx 0.755), (& $sx 0.31))
        $shaftBottom = New-Object System.Drawing.PointF((& $sx 0.755), (& $sx 0.60))
        $g.DrawLine($pen, $shaftTop, $shaftBottom)

        $head = @(
            (New-Object System.Drawing.PointF((& $sx 0.755), (& $sx 0.74))),
            (New-Object System.Drawing.PointF((& $sx 0.665), (& $sx 0.565))),
            (New-Object System.Drawing.PointF((& $sx 0.845), (& $sx 0.565)))
        )
        $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        $g.FillPolygon($white, $head)
        $white.Dispose()
    }

    $pen.Dispose()
    $g.Dispose()

    return $bitmap
}

function Get-DibBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $size = $Bitmap.Width
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    try {
        $stream = New-Object System.IO.MemoryStream
        $writer = New-Object System.IO.BinaryWriter($stream)

        # BITMAPINFOHEADER：高度写成两倍（XOR 图 + AND 掩码）
        $writer.Write([UInt32]40)
        $writer.Write([Int32]$size)
        $writer.Write([Int32]($size * 2))
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]0)
        $writer.Write([UInt32]($size * $size * 4))
        $writer.Write([Int32]0)
        $writer.Write([Int32]0)
        $writer.Write([UInt32]0)
        $writer.Write([UInt32]0)

        # 像素数据：自下而上，每行 BGRA
        $row = New-Object byte[] $data.Stride
        for ($y = $size - 1; $y -ge 0; $y--) {
            $pointer = [IntPtr]::Add($data.Scan0, $y * $data.Stride)
            [System.Runtime.InteropServices.Marshal]::Copy($pointer, $row, 0, $data.Stride)
            $writer.Write($row, 0, $size * 4)
        }

        # AND 掩码：32 位图靠 alpha 通道，掩码全 0 即可（不透明区域由 alpha 决定）
        $maskStride = [int]([Math]::Floor(($size + 31) / 32) * 4)
        $maskRow = New-Object byte[] $maskStride
        for ($y = 0; $y -lt $size; $y++) {
            $writer.Write($maskRow, 0, $maskStride)
        }

        $writer.Flush()
        $bytes = $stream.ToArray()
        $writer.Dispose()
        $stream.Dispose()
        return $bytes
    }
    finally {
        $Bitmap.UnlockBits($data)
    }
}

$frames = @()
foreach ($size in $sizes) {
    $bitmap = New-IconBitmap -Size $size

    # 必须显式声明 [byte[]]：PowerShell 函数返回数组时会被展开，
    # 直接用 (Get-DibBytes ...) 会拿到 Object[]，之后 BinaryWriter 只写进去 1 个字节。
    [byte[]]$frameData = Get-DibBytes -Bitmap $bitmap
    $frames += , @{ Size = $size; Data = $frameData }

    if ($size -eq 256 -and $PreviewPng) {
        $bitmap.Save($PreviewPng, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "预览图: $PreviewPng"
    }

    $bitmap.Dispose()
}

$outputDir = Split-Path -Parent $Output
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$file = [System.IO.File]::Create($Output)
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$frames.Count)

    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = 0
        if ($frame.Size -lt 256) { $dimension = $frame.Size }

        $writer.Write([Byte]$dimension)
        $writer.Write([Byte]$dimension)
        $writer.Write([Byte]0)
        $writer.Write([Byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$frame.Data.Length)
        $writer.Write([UInt32]$offset)
        $offset += $frame.Data.Length
    }

    foreach ($frame in $frames) {
        $blob = [byte[]]$frame.Data
        $writer.Write($blob, 0, $blob.Length)
    }

    Write-Host ("已生成 {0}（{1} 个尺寸: {2}）" -f $Output, $frames.Count, ($sizes -join ', '))
}
finally {
    $writer.Dispose()
    $file.Dispose()
}
