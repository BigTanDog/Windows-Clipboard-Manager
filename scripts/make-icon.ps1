# 从一张方形源图生成应用图标（Assets/app.ico）。
#
# 为什么需要它：应用图标必须是真实的 .ico 文件才能被 <ApplicationIcon> 嵌进 exe；
# 生成后同时供窗口图标与托盘图标使用（同一份字节，改图标只改一处）。
#
# 处理内容：① 裁掉源图外围少量留白 ② 缩放到 16/32/48/64/256 ③ 给圆角方块外侧做透明
# （AI 生成的源图四周是白底，直接当图标会在深色任务栏上出现白色方角）④ 组装多尺寸 ICO（PNG 压缩项）。
#
# 用法：.\scripts\make-icon.ps1 -Source src\ClipboardManager.App\Assets\app-icon-source.png
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [string]$Output,
    [int[]]$Sizes = @(16, 32, 48, 64, 256)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not $Output) {
    $Output = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/ClipboardManager.App/Assets/app.ico'
}
if (-not (Test-Path -LiteralPath $Source)) { throw "源图不存在：$Source" }

# 与源图圆角一致：源图圆角约 19.5% 边长，取 20% 并外扩 0.5px 让边缘不掉像素
$cornerRatio = 0.20
$featherPx = 1.2

function New-IconBitmap([System.Drawing.Image]$image, [int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new([int]$size, [int]$size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.DrawImage($image, (New-Object System.Drawing.Rectangle 0, 0, $size, $size))
    $graphics.Dispose()
    return $bitmap
}

# 圆角外透明化：用圆角矩形的近似有符号距离场做 1.2px 羽化
function Set-RoundedAlpha([System.Drawing.Bitmap]$bitmap) {
    $size = $bitmap.Width
    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadWrite, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $size)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)

    $half = $size / 2.0
    $radius = $size * $cornerRatio
    $inner = $half - $radius

    for ($y = 0; $y -lt $size; $y++) {
        $py = $y + 0.5 - $half
        for ($x = 0; $x -lt $size; $x++) {
            $px = $x + 0.5 - $half
            $dx = [Math]::Abs($px) - $inner
            $dy = [Math]::Abs($py) - $inner

            $outside = [Math]::Sqrt([Math]::Max($dx, 0) * [Math]::Max($dx, 0) + [Math]::Max($dy, 0) * [Math]::Max($dy, 0))
            $inside = [Math]::Min([Math]::Max($dx, $dy), 0)
            $distance = $outside + $inside - $radius

            $coverage = 0.5 - ($distance / $featherPx)
            if ($coverage -ge 1) { continue }      # 完全在内：保持原 alpha
            if ($coverage -le 0) { $alpha = 0 } else { $alpha = [int](255 * $coverage) }

            $offset = ($y * $data.Stride) + ($x * 4) + 3
            $current = $bytes[$offset]
            if ($alpha -lt $current) { $bytes[$offset] = [byte]$alpha }
        }
    }

    [System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $data.Scan0, $bytes.Length)
    $bitmap.UnlockBits($data)
}

Write-Host "源图：$Source"
# 用字节流加载：① 不锁文件（FromFile 会持有句柄直到 Dispose）② 避免 PathInfo → string 的隐式转换歧义
# 注意：变量名不要叫 $source —— 它与参数 [string]$Source 同名（PowerShell 大小写不敏感），
# 参数的类型约束会在赋值时把 Image 转成字符串 "System.Drawing.Bitmap"，导致后续尺寸全是 0。
$imageBytes = [System.IO.File]::ReadAllBytes([System.IO.Path]::GetFullPath($Source))
$imageStream = [System.IO.MemoryStream]::new($imageBytes, $false)
$sourceImage = [System.Drawing.Image]::FromStream($imageStream)
if ($null -eq $sourceImage) { throw "无法读取源图：$Source" }
Write-Host ("  源尺寸 {0}x{1}" -f $sourceImage.Width, $sourceImage.Height)

# 裁掉外围 1.2% 留白（AI 源图四周是白底，裁掉后圆角方块更饱满）
$trimX = [int]($sourceImage.Width * 0.012)
$trimY = [int]($sourceImage.Height * 0.012)
$side = [int][Math]::Min($sourceImage.Width - ($trimX * 2), $sourceImage.Height - ($trimY * 2))
Write-Host ("  裁剪 {0}px 后为 {1}px" -f $trimX, $side)
$cropped = [System.Drawing.Bitmap]::new($side, $side, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$cropGraphics = [System.Drawing.Graphics]::FromImage($cropped)
$cropGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$cropGraphics.DrawImage($sourceImage, (New-Object System.Drawing.Rectangle 0, 0, $side, $side), (New-Object System.Drawing.Rectangle $trimX, $trimY, $side, $side), [System.Drawing.GraphicsUnit]::Pixel)
$cropGraphics.Dispose()

# 逐尺寸缩放 + 透明圆角 + PNG 编码
$payloads = @()
foreach ($size in $Sizes) {
    $bitmap = New-IconBitmap $cropped $size
    Set-RoundedAlpha $bitmap
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $payloads += , @{ Size = $size; Bytes = $stream.ToArray() }
    $stream.Dispose()
    $bitmap.Dispose()
    Write-Host ("  生成 {0}x{0} → {1} 字节" -f $size, $payloads[-1].Bytes.Length)
}
$cropped.Dispose()
$sourceImage.Dispose()
$imageStream.Dispose()

# 组装 ICO：ICONDIR(6) + ICONDIRENTRY(16 × n) + PNG 数据
$headerSize = 6 + (16 * $payloads.Count)
$total = $headerSize + ($payloads | ForEach-Object { $_.Bytes.Length } | Measure-Object -Sum).Sum
$buffer = New-Object byte[] $total
$buffer[2] = 1                                  # 类型：图标
$buffer[4] = [byte]$payloads.Count              # 图像数量

$offset = $headerSize
for ($index = 0; $index -lt $payloads.Count; $index++) {
    $item = $payloads[$index]
    $entry = 6 + ($index * 16)
    $dimension = if ($item.Size -eq 256) { 0 } else { $item.Size }
    $buffer[$entry] = [byte]$dimension
    $buffer[$entry + 1] = [byte]$dimension
    $buffer[$entry + 4] = 1                     # 颜色平面
    $buffer[$entry + 6] = 32                    # 位深
    [BitConverter]::GetBytes([int]$item.Bytes.Length).CopyTo($buffer, $entry + 8)
    [BitConverter]::GetBytes([int]$offset).CopyTo($buffer, $entry + 12)
    $item.Bytes.CopyTo($buffer, $offset)
    $offset += $item.Bytes.Length
}

$outputPath = [System.IO.Path]::GetFullPath($Output)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($outputPath)) | Out-Null
[System.IO.File]::WriteAllBytes($outputPath, $buffer)
Write-Host ("已写出：{0}（{1} 字节，{2} 个尺寸）" -f $outputPath, $buffer.Length, $payloads.Count)
